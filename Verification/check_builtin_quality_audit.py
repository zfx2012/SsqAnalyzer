import argparse, json, math
from pathlib import Path
parser=argparse.ArgumentParser(description="Independently recompute a fixed-rule audit from its replay masks.")
parser.add_argument("directory", type=Path)
parser.add_argument("--ledger", type=Path, help="Optional local submission ledger; only read, never updated.")
args=parser.parse_args()
if not __debug__: raise RuntimeError("Run without -O: verification uses assertions.")
out=args.directory
audit=json.loads((out/"quality-audit.json").read_text(encoding="utf-8-sig"))
replay=json.loads((out/"replay.json").read_text(encoding="utf-8-sig"))
defs=json.loads((out/"fixed-definitions.json").read_text(encoding="utf-8-sig"))
data=[line.split() for line in Path(audit["Source"]).read_text(encoding="utf-8-sig").splitlines() if line.strip()]
n=len(data)
assert len(audit["Rules"])==100 and len(replay["Masks"])==100 and n==audit["Count"]
actual=[(sum(1<<(int(b)-1) for b in row[2:8]),1<<(int(row[8])-1)) for row in data]
assert replay["Periods"]==[int(row[0]) for row in data]
masks=replay["Masks"]; fails=replay["Failures"]
assert all(not any(x) for x in fails), "Inspect failure-handling separately if execution failures exist"
def score(r, indices):
    valid=list(indices)
    kills=sum(masks[r][i].bit_count() for i in valid)
    wrong=sum((masks[r][i]&actual[i][defs[r]["BallType"]]).bit_count() for i in valid)
    return dict(Evaluated=len(valid),Triggered=sum(bool(masks[r][i]) for i in valid),Failed=0,Killed=kills,WrongBalls=wrong,WrongPeriods=sum(bool(masks[r][i]&actual[i][defs[r]["BallType"]]) for i in valid))
for r,row in enumerate(audit["Rules"]):
    triggered=[i for i in range(1,n) if masks[r][i]][-50:]
    for key,indices in [("All",range(1,n)),("Recent500",range(n-500,n)),("Recent100",range(n-100,n)),("Last50Triggers",triggered)]:
        for field,expected in score(r,indices).items():
            assert row[key][field]==expected,(row["Id"],key,field)
    assert row["Last50SpanDraws"]==(n-triggered[0] if triggered else 0)
    unique=wrong_unique=0
    for i in range(n-500,n):
        others=0
        for j,d in enumerate(defs):
            if j!=r and d["BallType"]==defs[r]["BallType"]: others|=masks[j][i]
        only=masks[r][i]&~others
        unique+=only.bit_count()
        wrong_unique+=(only&actual[i][defs[r]["BallType"]]).bit_count()
    assert (row["UniqueKills500"],row["UniqueWrong500"])==(unique,wrong_unique),row["Id"]
by_id={d["RuleId"]:i for i,d in enumerate(defs)}
for pair in audit["Pairs"]:
    left,right=by_id[pair["A"]],by_id[pair["B"]]
    either=both=same=shared_wrong=0
    for i in range(n-500,n):
        a,b=masks[left][i],masks[right][i]
        either+=bool(a or b)
        both+=bool(a and b)
        same+=bool(a and a==b)
        drawn=actual[i][defs[left]["BallType"]]
        shared_wrong+=bool(a&drawn and b&drawn)
    assert (pair["EitherActive"],pair["BothActive"],pair["SameOutputs"],pair["SharedWrongPeriods"])==(either,both,same,shared_wrong)
for c,start in zip(audit["Combined"],[120,n-500,n-100]):
    stats=[]
    for i in range(start,n):
        red=blue=0
        for r,d in enumerate(defs):
            if d["BallType"]==0:red|=masks[r][i]
            else:blue|=masks[r][i]
        nr,nb=33-red.bit_count(),16-blue.bit_count()
        kept=6-(red&actual[i][0]).bit_count()
        rf,bf=kept==6,not(blue&actual[i][1])
        rt,bt=6<=nr<=15,1<=nb<=5
        rp=(math.comb(nr,6) if nr>=6 else 0)/math.comb(33,6)*nb/16
        stats.append((nr,nb,kept,rf,bf,rt,bt,rp))
    target=[s for s in stats if s[5] and s[6]]
    checks=dict(Draws=len(stats),RedTarget=sum(s[5] for s in stats),BlueTarget=sum(s[6] for s in stats),JointTarget=len(target),
      RedFullyRetained=sum(s[3] for s in stats),BlueRetained=sum(s[4] for s in stats),BothFullyRetained=sum(s[3] and s[4] for s in stats),
      TargetAndRedFull=sum(s[3] for s in target),TargetAndBlueFull=sum(s[4] for s in target),TargetAndBothFull=sum(s[3] and s[4] for s in target),
      RedUnder6=sum(s[0]<6 for s in stats),BlueZero=sum(s[1]==0 for s in stats),FailedDraws=0)
    for k,v in checks.items(): assert c[k]==v,(c["Range"],k,c[k],v)
    for k,v in dict(AverageRedRemaining=sum(s[0] for s in stats)/len(stats),AverageBlueRemaining=sum(s[1] for s in stats)/len(stats),
      AverageRetainedReds=sum(s[2] for s in stats)/len(stats),RandomExpectedBothFull=sum(s[7] for s in stats),
      TargetAverageRetainedReds=sum(s[2] for s in target)/len(target) if target else None,
      TargetRandomExpectedBothFull=sum(s[7] for s in target) if target else None).items():
        assert (c[k] is None if v is None else math.isclose(c[k],v,rel_tol=1e-10,abs_tol=1e-10)),(k,c[k],v)
result=dict(RulesChecked=100,CombinedWindowsChecked=3,PairCountsChecked=len(audit["Pairs"]))
if args.ledger:
    ledger=json.loads(args.ledger.read_text(encoding="utf-8-sig"))
    entry=next(e for e in reversed(ledger["Entries"]) if e["Submission"]["TargetPeriod"]==audit["Last"])
    assert len(entry["Submission"]["Rules"])==100
    by_id={d["RuleId"]:(i,d) for i,d in enumerate(defs)}
    index=replay["Periods"].index(entry["Submission"]["TargetPeriod"])
    for saved in entry["Submission"]["Rules"]:
        i,d=by_id[saved["Definition"]["RuleId"]]
        for k in ("RuleId","BallType","Category","Code"): assert d[k]==saved["Definition"][k],(d["RuleId"],k)
        assert json.loads(d["Parameters"])==json.loads(saved["Definition"]["Parameters"])
        assert masks[i][index]==sum(1<<(b-1) for b in saved["KilledBalls"])
    result.update(SubmissionPeriod=entry["Submission"]["TargetPeriod"],SubmissionRuleVersionsAndOutputsMatched=100)
(out/"independent-check.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(result,ensure_ascii=False))
