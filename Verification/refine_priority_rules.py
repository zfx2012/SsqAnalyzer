"""Bounded retrospective candidate comparison; outputs manifests, never rewrites the catalog."""
import argparse
import copy
import datetime as dt
import gzip
import hashlib
import json
from pathlib import Path
from rule_contributions import mask, union, metrics

GEOMETRY = '''function getKillBalls(ctx) {
  var p=ctx.params, h=p.scope?ctx.historyFor(p.scope,p.offsets.length):ctx.history(p.offsets.length);
  if(h.length!==p.offsets.length) return [];
  var max=p.blue?16:33, result={};
  for(var n=1;n<=max;n++) for(var sign=-1;sign<=1;sign+=2) {
    if(p.direction && p.direction!==sign) continue;
    var next=n+sign*p.target; if(next<1||next>max) continue;
    var valid=true;
    for(var j=0;j<h.length;j++) {
      var b=n+sign*p.offsets[j];
      if(b<1||b>max || (p.blue?h[j].blueBall!==b:h[j].redBalls.indexOf(b)<0)) { valid=false; break; }
    }
    if(valid) result[next]=true;
  }
  var balls=Object.keys(result).map(function(b){return parseInt(b,10);}).sort(function(a,b){return a-b;});
  if(p.frequencyWindow) {
    var actual=ctx.history(p.frequencyWindow); if(actual.length!==p.frequencyWindow) return [];
    balls=balls.filter(function(b){var count=0; for(var j=0;j<actual.length;j++) if(actual[j].redBalls.indexOf(b)>=0) count++; return count<=p.frequencyMax;});
  }
  return balls;
}'''
BLUE = '''function getKillBalls(ctx) {
  var p=ctx.params, h=ctx.history(p.lag+(p.trend?1:0));
  if(h.length!==p.lag+(p.trend?1:0)) return [];
  var n=h[h.length-p.lag].blueBall, direction=p.direction;
  if(p.trend) { var delta=h[h.length-1].blueBall-h[h.length-2].blueBall; if(delta===0) return []; direction=(delta>0?1:-1)*p.trend; }
  var result=[];
  for(var s=-1;s<=1;s+=2) if(!direction||s===direction) {var b=n+s*p.gap; if(b>=1&&b<=16) result.push(b);}
  return result.sort(function(a,b){return a-b;});
}'''

def next_date(date):
    date+=dt.timedelta(days=1)
    while date.weekday() not in (1,3,6): date+=dt.timedelta(days=1)
    return date

def score(outputs, actual, stop, window=50):
    indices=[i for i in range(1,stop) if outputs[i]][-window:]
    killed=sum(len(outputs[i]) for i in indices)
    wrong=sum(len(set(outputs[i])&actual[i]) for i in indices)
    return dict(Triggers=len(indices),Killed=killed,Correct=killed-wrong,Accuracy=1-wrong/killed if killed else None)

def build(data_path, replay_path):
    fields=[r.split() for r in data_path.read_text(encoding='utf-8-sig').splitlines() if r.strip()]
    records=[dict(Period=int(r[0]),Date=dt.date.fromisoformat(r[1]),Red=set(map(int,r[2:8])),Blue=int(r[8])) for r in fields]
    replay=json.loads(gzip.decompress(replay_path.read_bytes()))
    canonical='\n'.join(f"{r['Period']}|{r['Date'].isoformat()}|{','.join(str(v) for v in sorted(r['Red']))}|{r['Blue']}" for r in records)
    if hashlib.sha256(canonical.encode()).hexdigest().upper()!=replay['DataHash']:
        raise ValueError('Data differs from the frozen baseline replay; generate a fresh baseline first')
    n=len(records); start=n-500; split=n-100
    actual={'Red':[r['Red'] for r in records],'Blue':[{r['Blue']} for r in records]}
    rules={r['Rule']['ruleId']:r for r in replay['Rules']}
    base={rid:[mask(v) for v in r['Outputs'][:-1]] for rid,r in rules.items()}
    baseline={c:union((base[rid] for rid,r in rules.items() if r['Rule']['ballType']==c),500) for c in actual}
    scoped=[]; indexes={}
    for i in range(n+1):
        if i<n: target=records[i]['Period']
        else:
            date=next_date(records[-1]['Date']); target=date.year*1000+1 if date.year!=records[-1]['Date'].year else records[-1]['Period']+1
        scoped.append(indexes.get(target%1000,[])[:])
        if i<n: indexes.setdefault(records[i]['Period']%1000,[]).append(i)
    proposals=[]
    def add(rid,label,parameters,code):
        r=copy.deepcopy(rules[rid]['Rule']); r.update(name=label,params=parameters,jsCode=code,
            description='固定候选定义，供历史对照与后续冻结观察；未自动替换正式目录。',createdAt='2026-09-30T00:00:00Z')
        proposals.append(dict(Key=f'{rid}/v{1+sum(p["BaselineId"]==rid for p in proposals)}',BaselineId=rid,Rule=r))
    rid='B-S-B-022'
    for name,p in [('向右',dict(gap=7,direction=1,lag=1)),('向左',dict(gap=7,direction=-1,lag=1)),
        ('顺上段方向',dict(gap=7,direction=0,lag=1,trend=1)),('逆上段方向',dict(gap=7,direction=0,lag=1,trend=-1)),
        ('斜距6双向',dict(gap=6,direction=0,lag=1)),('斜距8双向',dict(gap=8,direction=0,lag=1)),
        ('前2期锚点',dict(gap=7,direction=0,lag=2)),('前3期锚点',dict(gap=7,direction=0,lag=3))]: add(rid,'蓝球候选：'+name,p,BLUE)
    for rid,scope,specs in [('B-G-R-009-HS','samePeriod',[("短斜续接",[0,1],2,0),("宽斜续接",[0,2],4,0),("短斜折返",[0,1],0,0),("宽斜折返",[0,2],0,0)]),
        ('B-G-R-001-HS','samePeriod',[("短折反穿",[0,1,0],-1,0),("宽折反穿",[0,2,0],-2,0),("短平台回折",[0,1,1],0,0),("宽平台回折",[0,2,2],0,0)]),
        ('B-G-R-011',None,[("正向三斜续接",[0,1,2],3,1),("反向三斜续接",[0,1,2],3,-1),("三斜中点回折",[0,1,2],1,0),("三斜原点回折",[0,1,2],0,0)])]:
        for label,path,target,direction in specs:
            p=dict(geometryVersion=1,offsets=path,target=target,blue=False,direction=direction)
            if scope: p['scope']=scope
            else: p.update(frequencyWindow=20,frequencyMax=4)
            add(rid,label+'杀红'+('（历史同期图）' if scope else ''),p,GEOMETRY)
    for proposal in proposals:
        p=proposal['Rule']['params']; color=proposal['Rule']['ballType']; outputs=[]
        for i in range(n+1):
            killed=set()
            if color=='Blue':
                if i>=p['lag']+(bool(p.get('trend'))):
                    anchor=records[i-p['lag']]['Blue']; direction=p['direction']
                    if p.get('trend'):
                        delta=records[i-1]['Blue']-records[i-2]['Blue']
                        direction=(1 if delta>0 else -1)*p['trend'] if delta else None
                    if direction is not None:
                        killed={anchor+s*p['gap'] for s in ([direction] if direction else [-1,1]) if 1<=anchor+s*p['gap']<=16}
            else:
                indices=scoped[i] if p.get('scope') else list(range(max(0,i-len(p['offsets'])),i))
                if len(indices)>=len(p['offsets']):
                    rows=[records[j]['Red'] for j in indices[-len(p['offsets']):]]
                    for anchor in rows[0]:
                        for s in ([p['direction']] if p['direction'] else [-1,1]):
                            b=anchor+s*p['target']
                            if 1<=b<=33 and all(anchor+s*d in row for d,row in zip(p['offsets'],rows)): killed.add(b)
                    if p.get('frequencyWindow'):
                        if i<p['frequencyWindow']: killed=set()
                        else: killed={b for b in killed if sum(b in r['Red'] for r in records[i-p['frequencyWindow']:i])<=p['frequencyMax']}
            outputs.append(sorted(killed))
        rid=proposal['BaselineId']; others=union((v for other,v in base.items() if other!=rid and rules[other]['Rule']['ballType']==color),500)
        masks=[mask(v) for v in outputs[start:n]]; combined=[a|b for a,b in zip(others,masks)]
        a=[mask(v) for v in actual[color][start:]]
        proposal.update(Train50=score(outputs,actual[color],split),Latest50=score(outputs,actual[color],n),
            Training=metrics(combined[:400],a[:400],color),Later=metrics(combined[400:],a[400:],color),
            All500=metrics(combined,a,color),ExpectedOutputs=outputs[start:])
        bm=metrics(baseline[color][:400],a[:400],color); m=proposal['Training']; gate=.94 if color=='Blue' else .82
        proposal['TrainingEligible']=proposal['Train50']['Triggers']==50 and proposal['Train50']['Accuracy']>=gate and m['Target']>=bm['Target'] and m['UnderMinimum']==0
        proposal['TrainingReasons']=(["触发不足50次"] if proposal['Train50']['Triggers']<50 else [])
        if proposal['Train50']['Accuracy'] is None or proposal['Train50']['Accuracy']<gate: proposal['TrainingReasons'].append('单条训练窗口未达门槛')
        if m['Target']<bm['Target']: proposal['TrainingReasons'].append('组合数量达标期数下降')
        if m['UnderMinimum']: proposal['TrainingReasons'].append('出现少于最低候选数的期次')
    chosen=[]
    for rid in ('B-S-B-022','B-G-R-009-HS','B-G-R-011','B-G-R-001-HS'):
        items=[p for p in proposals if p['BaselineId']==rid and p['TrainingEligible']]
        if items:
            selected=max(items,key=lambda p:p['Training']['AverageActualRetained']-p['Training']['RandomAverageRetained'])
            color=selected['Rule']['ballType']; a=[mask(v) for v in actual[color][start:]]
            bm=metrics(baseline[color][400:],a[400:],color); m=selected['Later']; gate=.94 if color=='Blue' else .82
            selected['LaterAccepted']=m['Target']>=bm['Target'] and m['UnderMinimum']==0 and m['Full']>=bm['Full'] and m['AverageActualRetained']-m['RandomAverageRetained']>bm['AverageActualRetained']-bm['RandomAverageRetained'] and selected['Latest50']['Accuracy']>=gate
            selected['LaterReasons']=[]
            if m['Target']<bm['Target']: selected['LaterReasons'].append('后100期数量达标期数下降')
            if m['UnderMinimum']: selected['LaterReasons'].append('后100期低于最少候选数')
            if m['Full']<bm['Full']: selected['LaterReasons'].append('后100期完整保留下降')
            if m['AverageActualRetained']-m['RandomAverageRetained']<=bm['AverageActualRetained']-bm['RandomAverageRetained']: selected['LaterReasons'].append('后100期同数量基准差异未改善')
            if selected['Latest50']['Accuracy']<gate: selected['LaterReasons'].append('最新50次未达门槛')
            chosen.append(selected['Key'])
    return dict(SourceRevision=replay.get('SourceRevision'),Through=records[-1]['Period'],DataHash=replay['DataHash'],
        Training=[records[start]['Period'],records[split-1]['Period']],Later=[records[split]['Period'],records[-1]['Period']],
        Baseline={c:dict(Training=metrics(v[:400],[mask(a) for a in actual[c][start:split]],c),Later=metrics(v[400:],[mask(a) for a in actual[c][split:]],c),All500=metrics(v,[mask(a) for a in actual[c][start:]],c)) for c,v in baseline.items()},
        ChosenByTraining=chosen,Proposals=proposals)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('data',type=Path);parser.add_argument('replay',type=Path);parser.add_argument('output',type=Path);args=parser.parse_args()
    result=build(args.data,args.replay);args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for p in result['Proposals']: print(p['Key'],p['Train50']['Accuracy'],p['Latest50']['Accuracy'],p['TrainingEligible'],p.get('LaterAccepted'),p['All500']['AverageRemaining'],p['All500']['Full'])
