"""Freeze two observation variants plus their combination; no prediction is created here."""
import argparse
import hashlib
import json
from pathlib import Path

SELECTED = ['B-S-B-022/v1','B-G-R-001-HS/v2']

def freeze(proposals, verified):
    if proposals['DataHash']!=verified['DataHash'] or proposals['Through']!=verified['Through']:
        raise ValueError('Verification and proposal data versions differ')
    candidates={r['Key']:r for r in verified['Candidates']}
    baselines={r['RuleId']:r['Fingerprint'] for r in verified['BaselineFingerprints']}
    variants=[]
    for key, keys in [('baseline',[]),('blue-right',SELECTED[:1]),('red-wide-return',SELECTED[1:]),('both',SELECTED)]:
        fingerprints=baselines.copy()
        for candidate in keys:
            definition=candidates[candidate]['Definition']
            fingerprints[definition['RuleId']]=candidates[candidate]['Fingerprint']
        rule_hash=hashlib.sha256('\n'.join(fingerprints[rid] for rid in sorted(fingerprints)).encode()).hexdigest().upper()
        variants.append(dict(Key=key,Overrides=[candidates[k]['Definition'] for k in keys],RuleSetHash=rule_hash))
    return dict(FormatVersion=1,DefinitionFreezeDate='2026-09-30',SourceThrough=verified['Through'],SourceDataHash=verified['DataHash'],
        SourceRevision=proposals['SourceRevision'],Purpose='Observation candidates, not approved replacements. No forward prediction has been created.',
        ObservationMinimumDraws=50,BaselineHash=verified['BaselineHash'],BaselineDefinitions=verified['BaselineDefinitions'],Variants=variants)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('proposals',type=Path);parser.add_argument('verified',type=Path);parser.add_argument('output',type=Path);args=parser.parse_args()
    manifest=freeze(json.loads(args.proposals.read_bytes()),json.loads(args.verified.read_bytes()))
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('Frozen definitions for',len(manifest['Variants']),'variants; forward predictions: 0')
