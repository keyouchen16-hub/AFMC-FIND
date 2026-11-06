import concurrent.futures,json,urllib.request,urllib.error
from pathlib import Path
out=Path('docs/evidence/v060/source-recheck');out.mkdir(parents=True,exist_ok=True)
urls=['https://btaa.xyz/','https://www.btaa.xyz/','https://heimacili.com/','https://heimacili.org/','https://heimamo.top/','https://heimaai.top/','https://qiankun6.com/','https://www.cilihezi.top/','https://july.xincili01ng.top/sh/?channel=&cid=2513']
def fetch(pair):
 i,url=pair
 try:
  req=urllib.request.Request(url,headers={'User-Agent':'Mozilla/5.0'})
  with urllib.request.urlopen(req,timeout=15) as r:
   body=r.read(1500000);(out/f'{i}.html').write_bytes(body)
   return {'url':url,'status':r.status,'final':r.url,'bytes':len(body),'file':f'{i}.html'}
 except Exception as e:return {'url':url,'error':str(e)}
with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool: rows=list(pool.map(fetch,enumerate(urls)))
(out/'summary.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(rows,ensure_ascii=False,indent=2))
