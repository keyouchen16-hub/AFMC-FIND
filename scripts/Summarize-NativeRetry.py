import json,pathlib
root=pathlib.Path(__file__).resolve().parent.parent/'docs/evidence/piece-reassignment'
results=[]
for p in sorted(root.glob('*/afmc.json')):
    r=json.loads(p.read_text(encoding='utf-8-sig'))
    rows=[]
    trace=p.with_name('pieces.jsonl')
    if trace.exists():
        for line in trace.read_text(encoding='utf-8-sig').splitlines():
            try: rows.append(json.loads(line))
            except json.JSONDecodeError: pass
    rows=[x for x in rows if 'required' in x]
    waits=[]; active=None
    for row in rows:
        if row['required'] >= 0 and not row['have']:
            if active is None or active['piece'] != row['required']:
                active={'piece':row['required'],'start':row['seconds'],'duration':0,'maxBlockAge':0,'maxRate':0,'retryStart':row.get('retryBlocks',0)}
                waits.append(active)
            active['duration']=round(row['seconds']-active['start'],2)
            active['maxBlockAge']=max(active['maxBlockAge'], max((b['age'] for b in row['missing']),default=0))
            active['maxRate']=max(active['maxRate'],row['rate'])
            active['retryEnd']=row.get('retryBlocks',0)
        else: active=None
    samples=r.get('Samples',[])
    after=[s for s in samples if r.get('FirstDecodedSeconds') and s['Seconds']>=r['FirstDecodedSeconds'] and s.get('Status')]
    supply=None
    if len(after)>1:
        supply=round((after[-1]['Status']['Downloaded']-after[0]['Status']['Downloaded'])/(after[-1]['Seconds']-after[0]['Seconds'])/1024,2)
    metrics={k:r.get(k) for k in ('MetadataSeconds','FirstDecodedSeconds','PostStartObservedSeconds','PostStartObservationComplete','StallSeconds','LongestStallSeconds','Outcome')}
    if r.get('FirstDecodedSeconds') is None:
        metrics['StallSeconds']=None
        metrics['LongestStallSeconds']=None
    results.append({'run':p.parent.name,**metrics,
        'postStartAverageKiBps':supply,'retryBlocks':max((x.get('retryBlocks',0) for x in rows),default=0),
        'hashFailures':max((x.get('hashFailures',0) for x in rows),default=0),
        'longestRequiredWaits':sorted(waits,key=lambda x:x['duration'],reverse=True)[:4]})
(root/'summary.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
print(json.dumps(results))
