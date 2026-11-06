import concurrent.futures, urllib.request, json, pathlib, re
out=pathlib.Path('docs/evidence/global-sources');out.mkdir(parents=True,exist_ok=True)
targets={
 'nyaa':'https://nyaa.si/?page=rss&q=Big%20Buck%20Bunny&c=0_0&f=0',
 '1337x':'https://1337x.to/search/ubuntu/1/',
 'limetorrents':'https://www.limetorrents.lol/search/all/ubuntu/',
 'bitsearch':'https://bitsearch.to/search?q=ubuntu',
 'solidtorrents':'https://solidtorrents.to/search?q=ubuntu',
 'knaben':'https://api.knaben.org/v1',
 'eztv':'https://eztvx.to/ezrss.xml',
}
def probe(item):
 name,url=item
 try:
  data=json.dumps({'search_field':'title','query':'ubuntu','order_by':'seeders','order_direction':'desc','from':0,'size':10}).encode() if name=='knaben' else None
  req=urllib.request.Request(url,data=data,headers={'User-Agent':'Mozilla/5.0 (compatible; AFMC-FIND/0.4.0)','Content-Type':'application/json'} if data else {'User-Agent':'Mozilla/5.0'})
  with urllib.request.urlopen(req,timeout=18) as r:
   body=r.read(4*1024*1024);(out/(name+'.txt')).write_bytes(body)
   return {'name':name,'url':url,'final':r.url,'status':r.status,'bytes':len(body),'type':r.headers.get('Content-Type'),'magnetCount':body.count(b'magnet:?'),'title':re.findall(b'<title>(.*?)</title>',body)[:1].__str__()}
 except Exception as e:return {'name':name,'url':url,'error':str(e)}
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
 results=list(pool.map(probe,targets.items()))
(out/'probe.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
print(json.dumps(results,indent=2))
