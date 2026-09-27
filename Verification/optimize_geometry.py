"""Bounded retrospective geometry search. Writes proposals, never edits application resources."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("data", type=Path)
parser.add_argument("rules", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
rows = [line.split() for line in args.data.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
reds = [set(map(int, r[2:8])) for r in rows]
blues = [{int(r[8])} for r in rows]
rules = json.loads(args.rules.read_text(encoding="utf-8-sig"))

def score(params):
    path, target = params["offsets"], params["target"]
    data, limit = (blues, 16) if params["blue"] else (reds, 33)
    direction = params.get("direction", 0)
    signs = [direction] if direction else [-1, 1]
    observations = []
    for index in range(len(path), len(rows)):
        history = data[index-len(path):index]
        killed = set()
        for first in history[0]:
            for sign in signs:
                anchor = first-sign*path[0]
                candidate = anchor+sign*target
                if 1 <= candidate <= limit and all(anchor+sign*offset in values for offset, values in zip(path, history)):
                    killed.add(candidate)
        if killed:
            observations.append((len(killed), len(killed & data[index])))
    recent = observations[-50:]
    count, wrong = sum(v[0] for v in recent), sum(v[1] for v in recent)
    return dict(AllTriggers=len(observations), Triggers=len(recent), Killed=count, Correct=count-wrong,
                Accuracy=(count-wrong)/count if count else 0)

def signature(params):
    values = params["offsets"] + [params["target"]]
    signs = [params["direction"]] if params.get("direction") else [-1, 1]
    return (params["blue"], tuple(sorted(set(tuple(sign*v for v in values) for sign in signs))))

baseline = {r["ruleId"]: score(r["params"]) for r in rules}
# Reserve unchanged rules first, so tuning cannot turn weak rules into copies of existing passing rules.
occupied = {signature(r["params"]) for r in rules if baseline[r["ruleId"]]["Accuracy"] >= (.94 if r["params"]["blue"] else .82)}
audit = []
for rule in rules:
    original = copy.deepcopy(rule["params"])
    before = baseline[rule["ruleId"]]
    gate = .94 if original["blue"] else .82
    trials = []
    selected = None
    if before["Accuracy"] < gate:
        seen = set()
        for numerator, denominator in [(1, 1), (2, 1), (1, 2)]:
            values = original["offsets"] + [original["target"]]
            if any(v*numerator % denominator for v in values):
                continue
            scaled = [v*numerator//denominator for v in values]
            if max(scaled)-min(scaled) >= (16 if original["blue"] else 33):
                continue
            for direction in [0, -1, 1]:
                candidate = dict(original, offsets=scaled[:-1], target=scaled[-1], direction=direction)
                key = (tuple(candidate["offsets"]), candidate["target"], direction)
                if key in seen:
                    continue
                seen.add(key)
                measured = score(candidate)
                duplicate = signature(candidate) in occupied
                eligible = (not duplicate and measured["Triggers"] >= min(30, before["Triggers"])
                            and measured["AllTriggers"]*2 >= before["AllTriggers"] and measured["Killed"] > 0)
                trials.append(dict(Parameters=candidate, Score=measured, Eligible=eligible, Duplicate=duplicate, Scale=f"{numerator}/{denominator}"))
        passing = [t for t in trials if t["Eligible"] and t["Score"]["Accuracy"] >= gate]
        if passing:
            selected = max(passing, key=lambda t: (t["Score"]["AllTriggers"], t["Scale"] == "1/1", t["Parameters"]["direction"] == 0))
            rule["params"] = selected["Parameters"]
            rule["jsCode"] = rule["jsCode"].replace("var next=anchor+sign*target;", "if(ctx.params.direction && sign!==ctx.params.direction) continue;\n      var next=anchor+sign*target;")
            symbol = lambda x: "n" if x == 0 else f"n{x:+d}"
            direction_text = {0: "左右镜像", 1: "正向", -1: "反向"}[rule["params"]["direction"]]
            rule["name"] += f"（{direction_text}，图距×{selected['Scale']}）"
            rule["description"] = ("连续实际开奖行依次出现 " + " → ".join(map(symbol, rule["params"]["offsets"]))
                + "，下一行杀 " + symbol(rule["params"]["target"]) + f"；{direction_text}识别，其他位置不限制，越界舍弃，候选去重。"
                + "方向和间距经过历史优化；近期成绩参与选择，不是独立验证。不足50次使用全部实际触发。")
    occupied.add(signature(rule["params"]))
    audit.append(dict(Id=rule["ruleId"], Before=before, After=selected["Score"] if selected else before,
                      Changed=selected is not None, Selected=selected, Trials=trials))
args.output.mkdir(parents=True, exist_ok=True)
(args.output/"extra-proposal.json").write_text(json.dumps(rules, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
(args.output/"extra-trials.json").write_text(json.dumps(dict(Through=int(rows[-1][0]), Count=len(rows),
    DataFileSha256=hashlib.sha256(args.data.read_bytes()).hexdigest(), Rules=audit), ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
print(json.dumps(dict(FailingBefore=sum(r["Before"]["Accuracy"] < (.94 if "-B-" in r["Id"] else .82) for r in audit),
    Changed=sum(r["Changed"] for r in audit), Trials=sum(len(r["Trials"]) for r in audit)), ensure_ascii=False))
