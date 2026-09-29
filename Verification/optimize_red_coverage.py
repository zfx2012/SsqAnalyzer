"""Offline fixed-geometry search. Never trims a live recommendation or writes runtime data."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('data', type=Path)
parser.add_argument('audit', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
rows = [l.split() for l in args.data.read_text(encoding='utf-8-sig').splitlines() if l.strip()]
history = [set(map(int, r[2:8])) for r in rows]
audit = json.loads(args.audit.read_text(encoding='utf-8'))
assert len(history) - audit['Start'] == len(audit['History'])
assert int(rows[-1][0]) == audit['Through']
for a, b in zip(history[audit['Start']:], audit['History']):
    assert a == set(b['Red'])
start = audit['Start']
mask = lambda balls: sum(1 << (b-1) for b in set(balls))
actual = [mask(r) for r in history]
rules = [r for r in audit['Rules'] if r['Rule']['ballType'] == 'Red']
outputs = {r['Rule']['ruleId']: [mask(v) for v in r['Outputs']] for r in rules}
# Only replace sparse added red trajectories; preserve the user's original scoped rules.
replaceable = sorted([r for r in rules if r['Rule']['ruleId'].startswith('B-S-R-')
    and sum(bool(v) for v in r['Outputs'][:-1]) < 50], key=lambda r: (sum(bool(v) for v in r['Outputs'][:-1]), r['Rule']['ruleId']))

def summarize(values):
    sizes = [33-v.bit_count() for v in values[:-1]]
    hits = [(v & a).bit_count() for v, a in zip(values[:-1], actual[start:])]
    return dict(CurrentRemaining=33-values[-1].bit_count(), CurrentBalls=[b for b in range(1,34) if not values[-1] & (1 << (b-1))],
        AverageRemaining=sum(sizes)/len(sizes), TargetPeriods=sum(6<=s<=15 for s in sizes), Under6=sum(s<6 for s in sizes),
        FullyRetained=sum(h==0 for h in hits), AverageRetained=sum(6-h for h in hits)/len(hits),
        TargetAndFull=sum(6<=s<=15 and h==0 for s,h in zip(sizes,hits)))

def union(items):
    result = [0]*(len(history)-start+1)
    for values in items:
        result = [a|b for a,b in zip(result,values)]
    return result

def loss(values):
    # Optimize historical width, strongly penalizing less than the six needed reds.
    # The pending draw is expressly excluded from this selection objective.
    return sum(max(0,33-v.bit_count()-15)**2 + 100*max(0,6-(33-v.bit_count()))**2 for v in values[:-1])

trials = []
for width in range(1,9):
    for name, path, target in [('两行斜线延伸',[0,width],2*width),('两行原点折返',[0,width],0),
        ('两行中点回折',[0,2*width],width),('两行反向延伸',[0,width],-width)]:
        for direction in [0,1,-1]:
            p = dict(geometryVersion=1, offsets=path, target=target, blue=False, direction=direction)
            values = [0,0]
            for i in range(2,len(history)+1):
                balls = set()
                for n in history[i-2]:
                    for sign in ([direction] if direction else [-1,1]):
                        b = n+sign*target
                        if 1<=b<=33 and n+sign*path[1] in history[i-1]: balls.add(b)
                values.append(mask(balls))
            indices = [i for i in range(2,len(history)) if values[i]][-50:]
            killed = sum(values[i].bit_count() for i in indices)
            correct = killed - sum((values[i]&actual[i]).bit_count() for i in indices)
            recent_kills = sum(v.bit_count() for v in values[start:-1])
            recent_correct = recent_kills - sum((v&a).bit_count() for v,a in zip(values[start:-1],actual[start:]))
            score = dict(Triggers=len(indices), Killed=killed, Correct=correct, Accuracy=correct/killed if killed else 0,
                Recent500Accuracy=recent_correct/recent_kills if recent_kills else 0, Recent500Triggers=sum(bool(v) for v in values[start:-1]))
            trials.append(dict(Name=f'{name}（图距{width}）', Parameters=p, Score=score, Values=values[start:]))

before = union(outputs.values())
selected = []
occupied = set()
for old in replaceable:
    rule_id = old['Rule']['ruleId']
    rest = union(v for k,v in outputs.items() if k != rule_id)
    eligible = [t for t in trials if t['Score']['Triggers']==50 and t['Score']['Accuracy']>=.82
        and t['Score']['Recent500Accuracy']>=27/33 and (tuple(t['Parameters']['offsets']),t['Parameters']['target']) not in occupied]
    if not eligible: break
    chosen = min(eligible, key=lambda t: (loss([a|b for a,b in zip(rest,t['Values'])]), -t['Score']['Recent500Triggers']))
    combined = [a|b for a,b in zip(rest,chosen['Values'])]
    if loss(combined) >= loss(union(outputs.values())): continue
    p = chosen['Parameters']
    occupied.add((tuple(p['offsets']),p['target']))
    rule = copy.deepcopy(old['Rule'])
    rule['jsCode'] = next(r['Rule']['jsCode'] for r in rules if 'ctx.params.direction' in r['Rule']['jsCode'])
    rule.update(name=chosen['Name']+{0:'双向',1:'正向',-1:'反向'}[p['direction']]+'图形杀红', params=p,
        description='连续两期出现固定轨迹，按图形延伸或折返位置杀号，越界舍弃，多处匹配合并去重。历史优化同时考察近50次门槛与近500期组合覆盖；成绩不是独立验证，不保证每期剩余15个以内。',
        createdAt='2026-09-29T00:00:00Z', tags=['图形','组合覆盖','历史优化'])
    outputs[rule_id] = chosen['Values']
    selected.append(dict(Id=rule_id, OldName=old['Rule']['name'], Rule=rule, Score=chosen['Score'], Combined=summarize(combined)))
    print(rule_id,rule['name'],selected[-1]['Combined'])
    # Stop once most historical periods meet the width target, rather than
    # exhausting the library and removing more balls without a user need.
    if selected[-1]['Combined']['TargetPeriods'] >= .75 * len(audit['History']): break

args.output.mkdir(parents=True,exist_ok=True)
report = dict(Through=audit['Through'], DataSha256=hashlib.sha256(args.data.read_bytes()).hexdigest(),
    CandidateCount=len(trials), Before=summarize(before), After=summarize(union(outputs.values())), Selected=selected,
    Trials=[{k:v for k,v in t.items() if k!='Values'} for t in trials])
(args.output/'selection.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('RESULT',report['Before'],report['After'])
