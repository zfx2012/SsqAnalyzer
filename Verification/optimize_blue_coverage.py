"""Explore fixed single-anchor diagonal candidates; retrospective, never a live count cap."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('data', type=Path)
parser.add_argument('audit', type=Path)
parser.add_argument('replacements', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
rows = [l.split() for l in args.data.read_text(encoding='utf-8-sig').splitlines() if l.strip()]
history = [int(r[8]) for r in rows]
audit = json.loads(args.audit.read_text(encoding='utf-8'))
assert int(rows[-1][0]) == audit['Through'] and len(history)-audit['Start'] == len(audit['History']) == 500
assert history[audit['Start']:] == [r['Blue'] for r in audit['History']]
start = audit['Start']
mask = lambda balls: sum(1 << (b-1) for b in set(balls))
rules = [r for r in audit['Rules'] if r['Rule']['ballType'] == 'Blue']
outputs = {r['Rule']['ruleId']: [mask(v) for v in r['Outputs']] for r in rules}
overrides = {r['ruleId'] for r in json.loads(args.replacements.read_text(encoding='utf-8'))}
replaceable = sorted([r for r in rules if r['Rule']['ruleId'].startswith('B-S-B-') and r['Rule']['ruleId'] not in overrides],
    key=lambda r: (sum(bool(v) for v in r['Outputs'][:-1]), r['Rule']['ruleId']))

def union(items):
    result = [0]*501
    for values in items: result = [a|b for a,b in zip(result,values)]
    return result

def summarize(values):
    sizes = [16-v.bit_count() for v in values[:-1]]
    retained = [not(v & (1 << (n-1))) for v,n in zip(values[:-1],history[start:])]
    return dict(CurrentRemaining=16-values[-1].bit_count(), CurrentBalls=[b for b in range(1,17) if not values[-1] & (1 << (b-1))],
        AverageRemaining=sum(sizes)/500, TargetPeriods=sum(1<=s<=6 for s in sizes), Zero=sum(s==0 for s in sizes),
        Retained=sum(retained), TargetAndRetained=sum(1<=s<=6 and ok for s,ok in zip(sizes,retained)))

def loss(values):
    # Pending output is not used for selection; strongly penalize empty historical sets.
    return sum(max(0,16-v.bit_count()-6)**2 + 100*(v.bit_count()==16) for v in values[:-1])

trials = []
for gap in range(1,16):
    for direction in [0,1,-1]:
        signs = [direction] if direction else [-1,1]
        values = [0]+[mask(n+s*gap for s in signs if 1<=n+s*gap<=16) for n in history]
        indices = [i for i in range(1,len(history)) if values[i]][-50:]
        killed = sum(values[i].bit_count() for i in indices)
        correct = killed-sum(bool(values[i] & (1 << (history[i]-1))) for i in indices)
        recent_killed = sum(v.bit_count() for v in values[start:-1])
        recent_correct = recent_killed-sum(bool(v & (1 << (n-1))) for v,n in zip(values[start:-1],history[start:]))
        trials.append(dict(Parameters=dict(geometryVersion=1,offsets=[0],target=gap,blue=True,direction=direction),
            Score=dict(Triggers=len(indices),Killed=killed,Correct=correct,Accuracy=correct/killed if killed else 0,
                Recent500Triggers=sum(bool(v) for v in values[start:-1]),Recent500Accuracy=recent_correct/recent_killed if recent_killed else 0),Values=values[start:]))
before = summarize(union(outputs.values()))
selected = []; occupied = set()
for old in replaceable:
    rid = old['Rule']['ruleId']; rest = union(v for k,v in outputs.items() if k!=rid)
    eligible = [t for t in trials if t['Score']['Triggers']==50 and t['Score']['Accuracy']>=.94 and t['Parameters']['target'] not in occupied]
    if not eligible: break
    chosen = min(eligible,key=lambda t:(loss([a|b for a,b in zip(rest,t['Values'])]),-t['Score']['Recent500Triggers']))
    combined = [a|b for a,b in zip(rest,chosen['Values'])]
    if loss(combined)>=loss(union(outputs.values())): continue
    p = chosen['Parameters']; occupied.add(p['target'])
    rule = copy.deepcopy(old['Rule'])
    rule.update(name=f"单锚点斜距{p['target']}"+{0:'双向',1:'正向',-1:'反向'}[p['direction']]+'杀蓝',params=p,
        jsCode=next(r['Rule']['jsCode'] for r in rules if 'ctx.params.direction' in r['Rule']['jsCode']),
        description='以上一期蓝球为单个锚点，下一行按固定列距排除对应位置；不是多点已成形规律。越界舍弃，不循环，不按推荐数量临时补删。间距与方向经过历史优化，近50次94%门槛和近500期覆盖参与选择，不是独立验证。',
        createdAt='2026-09-29T00:00:00Z',tags=['短斜线','单锚点','历史优化'])
    outputs[rid]=chosen['Values']
    selected.append(dict(Id=rid,OldName=old['Rule']['name'],Rule=rule,Score=chosen['Score'],Combined=summarize(combined)))
    print(rid,p,selected[-1]['Combined'])
    if selected[-1]['Combined']['TargetPeriods']>=375: break
result = dict(Through=audit['Through'],DataSha256=hashlib.sha256(args.data.read_bytes()).hexdigest(),CandidateCount=len(trials),
    Before=before,After=summarize(union(outputs.values())),Selected=selected,Trials=[{k:v for k,v in t.items() if k!='Values'} for t in trials])
args.output.mkdir(parents=True,exist_ok=True)
(args.output/'selection.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('RESULT',result['Before'],result['After'])
