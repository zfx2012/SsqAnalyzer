"""Propose eight fixed geometric replacements; all scores are retrospective selection scores."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("data", type=Path)
parser.add_argument("extras", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
data = [r.split() for r in args.data.read_text(encoding="utf-8-sig").splitlines() if r.strip()]
reds = [set(map(int,r[2:8])) for r in data]
blues = [{int(r[8])} for r in data]
extras = json.loads(args.extras.read_text(encoding="utf-8-sig"))
ids = ["B-G-R-001", "B-G-R-001-OE", "B-S-R-002", "B-S-R-009", "B-S-R-015", "B-G-B-001", "B-S-B-010", "B-S-B-013"]

def signature(p):
    values=p["offsets"]+[p["target"]]
    # Same path with fewer directions is a subset, not a new replacement mechanism.
    return (p["blue"], min(tuple(values), tuple(-v for v in values)))

def evaluate(p):
    history=blues if p["blue"] else reds
    limit=16 if p["blue"] else 33
    path=p["offsets"]; signs=[p["direction"]] if p["direction"] else [-1,1]
    observations=[]
    for i in range(len(path),len(data)):
        previous=history[i-len(path):i]; kills=set()
        for n in previous[0]:
            for s in signs:
                candidate=n+s*p["target"]
                if 1<=candidate<=limit and all(n+s*offset in row for offset,row in zip(path,previous)):
                    kills.add(candidate)
        if kills: observations.append((i,len(kills),len(kills & history[i])))
    recent=observations[-50:]
    killed=sum(r[1] for r in recent);wrong=sum(r[2] for r in recent)
    return dict(AllTriggers=len(observations), Recent500=sum(r[0]>=len(data)-500 for r in observations),
                Triggers=len(recent), Killed=killed, Correct=killed-wrong, Accuracy=(killed-wrong)/killed if killed else 0)

templates=[("平台上行",[0,1,1],2),("竖后斜接",[0,0,1],2),("短尖顶回折",[0,1,2],1),
    ("折后反穿",[0,1,0],-1),("平台回折",[0,1,1],0),("双台阶续接",[0,0,1],1),
    ("斜线回摆",[0,1,2,1],2),("三列短循环",[0,1,2],0)]
trials=[]
occupied={signature(r["params"]) for r in extras if r["ruleId"] not in ids}
for blue in [False,True]:
    shapes=[(f"跨度{2*d}中点回折",[0,2*d],d) for d in range(3,8)] if blue else [
        (f"{name}（图距{width}）",[v*width for v in path],target*width) for name,path,target in templates for width in [1,2]]
    for name,path,target in shapes:
        for direction in [0,1,-1]:
            p=dict(geometryVersion=1,offsets=path,target=target,blue=blue,direction=direction)
            trials.append(dict(Name=name,Parameters=p,Score=evaluate(p),ExistingDuplicate=signature(p) in occupied))

selected=[]
for rule_id in ids:
    blue="-B-" in rule_id
    eligible=[t for t in trials if t["Parameters"]["blue"]==blue and signature(t["Parameters"]) not in occupied
        and t["Score"]["Triggers"]>=50 and t["Score"]["Accuracy"]>=(.94 if blue else .82)]
    if not eligible: raise RuntimeError(f"No qualifying distinct candidate for {rule_id}; proposals not written")
    chosen=max(eligible,key=lambda t:(t["Score"]["Recent500"],t["Score"]["AllTriggers"],t["Parameters"]["direction"]==0))
    occupied.add(signature(chosen["Parameters"]))
    rule=copy.deepcopy(extras[0]);p=chosen["Parameters"]
    # Use an existing geometry evaluator supporting directional matches, independent of source rule's parameters.
    rule["jsCode"]=next(r["jsCode"] for r in extras if "ctx.params.direction" in r["jsCode"])
    orientation={0:"双向",1:"正向",-1:"反向"}[p["direction"]]
    rule.update(ruleId=rule_id,name=chosen["Name"]+orientation+"图形杀"+("蓝" if blue else "红"),
        ballType="Blue" if blue else "Red",params=p,minAccuracy=.94 if blue else .82,
        createdAt="2026-09-27T00:00:00Z",tags=["图形","替换规则","历史优化"])
    symbol=lambda n:"n" if n==0 else f"n{n:+d}"
    rule["description"]=("替换原不达标规则，改用连续实际开奖行："+" → ".join(map(symbol,p["offsets"]))
        +"，下一行杀"+symbol(p["target"])+f"；{orientation}识别，越界舍弃、候选去重。"
        +"图形及方向经过历史优化，近50次成绩参与选择，不是独立验证；不足50次按全部实际触发。旧规则快照保持原样。")
    selected.append(dict(Id=rule_id,Candidate=chosen,Rule=rule))
args.output.mkdir(parents=True,exist_ok=True)
(args.output/"replacements.json").write_text(json.dumps([s["Rule"] for s in selected],ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
(args.output/"selection.json").write_text(json.dumps(dict(Through=int(data[-1][0]),Count=len(data),
    DataSha256=hashlib.sha256(args.data.read_bytes()).hexdigest(),Trials=trials,Selected=selected),ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
for s in selected: print(s["Id"],s["Candidate"]["Name"],s["Candidate"]["Score"])
