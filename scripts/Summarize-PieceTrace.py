"""Summarize bounded, anonymized piece traces without emitting network identities."""
import json
import pathlib
import sys

folder = pathlib.Path(sys.argv[1])
report = folder / 'afmc.json'
if report.exists():
    result = json.loads(report.read_text(encoding='utf-8-sig'))
    print(json.dumps({k: v for k, v in result.items() if k != 'Samples'}, ensure_ascii=False))
trace = folder / 'pieces.jsonl'
rows = []
if trace.exists():
    for line in trace.read_text(encoding='utf-8-sig').splitlines():
        try:
            rows.append(json.loads(line))
        except json.JSONDecodeError:
            pass  # The active worker may be appending its final line.
good = [r for r in rows if 'required' in r]
if good:
    def brief(r):
        blocks = r.get('missing', [])
        owners = {b['peer'] for b in blocks}
        return {**{k: r.get(k) for k in ('seconds', 'required', 'have', 'rate', 'downloaded', 'verifiedPieces', 'redundantBytes', 'failedBytes', 'hashFailures')},
                'missingBlocks': len(blocks),
                'remainingBytes': sum((b.get('size') or 0)-(b.get('progress') or 0) for b in blocks),
                'oldestObservedAssignment': max((b['age'] for b in blocks), default=0),
                'owners': [{k: p.get(k) for k in ('id','rate','has','queue','receivingPiece','remoteChoking','snubbed')} for p in r['peers'] if p['id'] in owners],
                'otherCapableSuppliers': sum(p['has'] and p['id'] not in owners and p['rate'] > 16384 for p in r['peers'])}
    worst = max(good, key=lambda r: max((b['age'] for b in r['missing']), default=0))
    print(json.dumps({'samples': len(good), 'traceErrors': len(rows)-len(good),
        'peakConcurrentBlockCopies': max((b.get('copies') or 0 for r in good for b in r['missing']), default=0),
        'samplesWithDuplicateRequests': sum(any((b.get('copies') or 0)>1 for b in r['missing']) for r in good),
        'last': brief(good[-1]), 'longestAssignment': brief(worst)}, ensure_ascii=False))
