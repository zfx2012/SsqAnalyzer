"""Read-only marginal-contribution audit of a fixed catalog. No fitting or app writes."""
import argparse
import gzip
import json
import math
from pathlib import Path

SHORT_BLUE = {'B-S-B-006','B-S-B-007','B-S-B-008','B-S-B-014','B-S-B-015','B-S-B-021','B-S-B-022','B-S-B-023'}

def mask(balls):
    return sum(1 << (b-1) for b in set(balls))

def union(series, n):
    result = [0]*n
    for values in series:
        result = [a|b for a,b in zip(result,values)]
    return result

def metrics(values, actual, color):
    limit, drawn, low, high = (33,6,6,15) if color == 'Red' else (16,1,1,6)
    sizes = [limit-v.bit_count() for v in values]
    kept = [drawn-(v&a).bit_count() for v,a in zip(values,actual)]
    n = len(values)
    full_expected = sum(math.comb(s,drawn)/math.comb(limit,drawn) if s>=drawn else 0 for s in sizes)
    return dict(Draws=n,AverageRemaining=sum(sizes)/n,Target=sum(low<=s<=high for s in sizes),
        UnderMinimum=sum(s<low for s in sizes),AverageActualRetained=sum(kept)/n,
        RandomAverageRetained=sum(sizes)*drawn/limit/n,Full=sum(k==drawn for k in kept),
        RandomExpectedFull=full_expected,TargetAndFull=sum(low<=s<=high and k==drawn for s,k in zip(sizes,kept)))

def contribution(own, others, actual, color):
    limit, drawn = (33,6) if color=='Red' else (16,1)
    unique = [a & ~b for a,b in zip(own,others)]
    total = sum(v.bit_count() for v in unique)
    wrong = sum((v&a).bit_count() for v,a in zip(unique,actual))
    return dict(UniqueKills=total,UniqueWrong=wrong,UniqueCorrectRate=1-wrong/total if total else None,
        RandomExpectedWrong=total*drawn/limit,ExcessWrong=wrong-total*drawn/limit,
        RestoredFull=sum((a&u)!=0 and (a&o)==0 for u,o,a in zip(unique,others,actual)))

def family(rule):
    p=rule['params']
    if 'geometryVersion' in p:
        if len(p['offsets'])==1: return 'single-anchor'  # all distances count as ONE mechanism
        path=p['offsets']+[p['target']]
        centered=[v-path[0] for v in path]
        divisor=math.gcd(*centered) or 1
        normalized=tuple(v//divisor for v in centered)
        return 'trajectory:'+str(min(normalized,tuple(-v for v in normalized)))
    return '-'.join(rule['ruleId'].split('-')[:4])  # scoped variants are not independent votes

def audit(data):
    history=data['History']; n=len(history)
    assert n==500 and len({r['Rule']['ruleId'] for r in data['Rules']})==len(data['Rules'])
    actual={'Red':[mask(h['Red']) for h in history], 'Blue':[mask([h['Blue']]) for h in history]}
    rules=data['Rules']; outputs={r['Rule']['ruleId']:[mask(v) for v in r['Outputs'][:-1]] for r in rules}
    assert all(len(v)==n for v in outputs.values())
    groups={c:[r for r in rules if r['Rule']['ballType']==c] for c in actual}
    baseline={c:union((outputs[r['Rule']['ruleId']] for r in groups[c]),n) for c in actual}
    rows=[]
    for r in rules:
        rule=r['Rule']; rid=rule['ruleId']; color=rule['ballType']; own=outputs[rid]; a=actual[color]
        others=union((outputs[x['Rule']['ruleId']] for x in groups[color] if x['Rule']['ruleId']!=rid),n)
        con=contribution(own,others,a,color)
        halves=[contribution(own[i:i+250],others[i:i+250],a[i:i+250],color) for i in (0,250)]
        killed=sum(v.bit_count() for v in own); wrong=sum((v&v2).bit_count() for v,v2 in zip(own,a))
        accuracy=1-wrong/killed if killed else None; base=27/33 if color=='Red' else 15/16
        per100=[]
        for i in range(0,n,100):
            k=sum(v.bit_count() for v in own[i:i+100]); w=sum((v&v2).bit_count() for v,v2 in zip(own[i:i+100],a[i:i+100]))
            per100.append(dict(From=history[i]['Period'],Through=history[i+99]['Period'],Kills=k,Wrong=w,Accuracy=1-w/k if k else None))
        if con['UniqueKills'] < 30:
            status='观察'; reason='独有排除不足30个，难以判断独立贡献'
        elif con['ExcessWrong']>=3 and all(h['UniqueKills']>=10 and h['ExcessWrong']>0 for h in halves) and accuracy < base:
            status='暂时停用候选'; reason='两段独有排除均比同数量随机基准多错杀，且近500期单条低于基准'
        elif con['ExcessWrong']>=3:
            status='优先修改'; reason='独有排除比同数量随机基准多错杀'
        elif con['ExcessWrong']<=-3 and all(h['ExcessWrong']<=0 for h in halves):
            status='保留'; reason='两段独有排除均未高于随机错杀基准，继续前向观察'
        else:
            status='观察'; reason='净差异不足3个或两段方向不一致，暂不依据小幅波动调整'
        bm=metrics(baseline[color],a,color); removed=metrics(others,a,color)
        rows.append(dict(Id=rid,Name=rule['name'],Color=color,Family=family(rule),Status=status,Reason=reason,
            Recent50Accuracy=r['Accuracy'],Recent50Triggers=r['Triggers'],Recent500Accuracy=accuracy,
            Triggered=sum(bool(v) for v in own),Killed=killed,Wrong=wrong,Contribution=con,Halves=halves,Blocks100=per100,
            UniqueFraction=con['UniqueKills']/killed if killed else 0,
            RemovingAddsAverageBalls=removed['AverageRemaining']-bm['AverageRemaining'],
            RemovingLosesTargetPeriods=bm['Target']-removed['Target'],AfterRemoval=removed))
    pairs=[]
    for color, items in groups.items():
        for i,left in enumerate(items):
            for right in items[i+1:]:
                lid=left['Rule']['ruleId']; rid=right['Rule']['ruleId']; x=outputs[lid]; y=outputs[rid]
                intersection=sum((v&w).bit_count() for v,w in zip(x,y)); either=sum((v|w).bit_count() for v,w in zip(x,y))
                if intersection:
                    pairs.append(dict(A=lid,B=rid,Color=color,Intersection=intersection,Jaccard=intersection/either))
    scenarios={}
    def record(name, series):
        entry={c:metrics(series[c],actual[c],c) for c in actual}
        entry['BothFull']=sum(not(series['Red'][i]&actual['Red'][i]) and not(series['Blue'][i]&actual['Blue'][i]) for i in range(n))
        entry['JointTargetAndFull']=sum(6<=33-series['Red'][i].bit_count()<=15 and 1<=16-series['Blue'][i].bit_count()<=6
            and not(series['Red'][i]&actual['Red'][i]) and not(series['Blue'][i]&actual['Blue'][i]) for i in range(n))
        scenarios[name]=entry
    record('全部100条',baseline)
    for name, excluded in [('移除8条蓝球短斜线',SHORT_BLUE),
        ('移除暂时停用候选',{r['Id'] for r in rows if r['Status']=='暂时停用候选'}),
        ('仅当前启用规则',{r['Rule']['ruleId'] for r in rules if not r['Enabled']})]:
        record(name,{c:union((outputs[r['Rule']['ruleId']] for r in groups[c] if r['Rule']['ruleId'] not in excluded),n) for c in actual})
    confirmations={}
    for color, items in groups.items():
        families={}
        for r in items: families.setdefault(family(r['Rule']),[]).append(outputs[r['Rule']['ruleId']])
        merged=[union(v,n) for v in families.values()]
        confirmations[color]=[mask(b for b in range(1,34 if color=='Red' else 17) if sum(bool(v[i] & (1<<(b-1))) for v in merged)>=2) for i in range(n)]
    record('两个不同图形类型确认',confirmations)
    return dict(Through=data['Through'],DataHash=data['DataHash'],SourceRevision=data.get('SourceRevision','未记录'),
        AuditDate=data.get('AuditDate','日期未记录'),Periods=[history[0]['Period'],history[-1]['Period']],
        Rules=rows,Scenarios=scenarios,OverlapPairs=sorted(pairs,key=lambda p:p['Jaccard'],reverse=True),
        StatusCounts={s:sum(r['Status']==s for r in rows) for s in ('保留','优先修改','暂时停用候选','观察')})

def pct(value): return '—' if value is None else f'{value*100:.2f}%'

def markdown(result):
    text=[f"# {len(result['Rules'])}条规则边际贡献排查（{result['AuditDate']}）",'',
        f"固定规则版本{result['SourceRevision']}，截至{result['Through']}期；使用{result['Periods'][0]}～{result['Periods'][1]}共500期正式执行器输出。仅分析，不修改规则、启用状态或提交记录。",'',
        '## 口径和限制','',
        '移除一条规则后，只能恢复它独自排除的号码；共同错杀不会归功于某一条。独有错杀减去随机预期错杀（红球独有排除数×6/33、蓝球×1/16）用于相同新增候选数量的描述性比较。负数表示这条规则的独有排除少于随机预期错杀，正数表示更多。',
        '同数量随机基准按每期实际剩余数量计算，完整保留基准为C(剩余数,开奖号码数)/C(总数,开奖号码数)。它不是另一套经过验证的选号模型，也不是统计显著性检验。',
        '全部历史已被规则筛选使用。两段250期、五段100期都只是稳定性检查，不是留出验证。单条边际作用不能相加；一次移除多个规则须重新合并。停用名单是待验证候选，没有自动停用。','',
        '## 组合对照','',
        '|方案|红球平均剩余|红球数量达标|红球平均保留实际号|蓝球平均剩余|蓝球数量达标|蓝球保留率|蓝球同数量随机预期|6红+1蓝全保留|数量同时达标且全保留|',
        '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
    for name, s in result['Scenarios'].items():
        r,b=s['Red'],s['Blue']
        text.append(f"|{name}|{r['AverageRemaining']:.3f}|{r['Target']}/500|{r['AverageActualRetained']:.3f}|{b['AverageRemaining']:.3f}|{b['Target']}/500|{pct(b['Full']/500)}|{pct(b['RandomExpectedFull']/500)}|{s['BothFull']}|{s['JointTargetAndFull']}|")
    text+=['','## 八条蓝球短斜线','',
        '|ID|近50次正确排除率|近500期正确排除率|独有排除|独有错杀|相对随机多错杀|移除后恢复开奖蓝球期数|移除后数量达标减少期数|建议|',
        '|---|---:|---:|---:|---:|---:|---:|---:|---|']
    for r in result['Rules']:
        if r['Id'] not in SHORT_BLUE: continue
        c=r['Contribution']
        text.append(f"|{r['Id']}|{pct(r['Recent50Accuracy'])}|{pct(r['Recent500Accuracy'])}|{c['UniqueKills']}|{c['UniqueWrong']}|{c['ExcessWrong']:+.2f}|{c['RestoredFull']}|{r['RemovingLosesTargetPeriods']}|{r['Status']}|")
    text+=['','## 建议规则','', '；'.join(f'{k}：{v}条' for k,v in result['StatusCounts'].items()),'',
        '独有排除不足30个列为观察。与随机预期错杀差异小于3个也列为观察，避免把0.2个之类的差异当成改动理由。多错杀至少3个、两段各至少10个独有排除且都比随机基准多错杀、近500期整体也低于基准时列为暂时停用候选；其余多错杀至少3个列为优先修改；少错杀至少3个且两段均不高于基准列为保留，其余观察。30、10、3均为本轮描述性排查阈值，不是显著性结论。','',
        '## 下一轮实施顺序','',
        '1. 先针对B-S-B-022（单锚点斜距7双向）做少量图形确认候选。其独有错杀偏多，但直接移除会明显损失蓝球数量覆盖，因此先保留正式版、比较候选版，不直接关闭。',
        '2. 红球先处理B-G-R-009-HS，其次B-G-R-011、B-G-R-001-HS；另检查B-S-R-013、B-S-R-021的新增覆盖。优先减少独有错杀，同时记录候选数量变化。',
        '3. 暂不采用全局双类型确认。它让红球平均剩24.902个、蓝球15.216个，无法满足数量目标。移除全部8条短斜线也无法满足目标。',
        '4. 每条规则仅预先列出少量可解释候选，固定训练截止点、参数和版本；按时间滚动评估。现有历史均已被探索，因此滚动结果仍作历史参考，不命名为全新独立验证。',
        '5. 冻结选定候选及基线，同时保存每期开奖前输出。后续至少记录50个连续开奖期作为第一轮观察，再按触发样本、相同候选数量下的保留差异和错杀复盘评估；不足证据继续观察，不自动替换或把50期视为有效性的证明。',
        '6. 门槛82%/94%继续作为单条筛选条件。正式替换还需同时检查红球6～15个、蓝球1～6个的覆盖，以及同数量基准差异；当前资料尚不足以宣称某个候选有未来优势。','',
        '## 全部100条','',
        '|ID|名称|触发/500|近50次正确率|近500期正确率|独有排除|独有错杀|相对随机多错杀|移除后恢复完整保留期数|移除后数量达标减少期数|建议|',
        '|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|']
    for r in result['Rules']:
        c=r['Contribution']
        text.append(f"|{r['Id']}|{r['Name']}|{r['Triggered']}|{pct(r['Recent50Accuracy'])}|{pct(r['Recent500Accuracy'])}|{c['UniqueKills']}|{c['UniqueWrong']}|{c['ExcessWrong']:+.2f}|{c['RestoredFull']}|{r['RemovingLosesTargetPeriods']}|{r['Status']}|")
    text+=['','## 排除结果重叠最高的10对','',
        '|规则A|规则B|共同排除次数|排除交并比|','|---|---|---:|---:|']
    for p in result['OverlapPairs'][:10]: text.append(f"|{p['A']}|{p['B']}|{p['Intersection']}|{pct(p['Jaccard'])}|")
    text+=['','分段结果、各规则移除后的完整指标及全部重叠对见[JSON记录](Fixtures/rule-contributions-20260929.json)。',
        '可复现输入为[压缩执行器输出](Fixtures/rule-contribution-replay-20260929.json.gz)，包含固定定义、500期输出和下一期输出；分析函数只使用已开奖的500期。','',
        '```powershell','python Verification/rule_contributions.py Verification/Fixtures/rule-contribution-replay-20260929.json.gz tmp/contribution/reproduce','```','']
    return '\n'.join(text)

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('input',type=Path); parser.add_argument('output',type=Path); args=parser.parse_args()
    data=json.loads(gzip.decompress(args.input.read_bytes()) if args.input.suffix=='.gz' else args.input.read_bytes())
    result=audit(data); args.output.mkdir(parents=True,exist_ok=True)
    (args.output/'analysis.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (args.output/'report.md').write_text(markdown(result),encoding='utf-8')
    print(json.dumps(dict(StatusCounts=result['StatusCounts'],Scenarios=result['Scenarios']),ensure_ascii=False,indent=2))
