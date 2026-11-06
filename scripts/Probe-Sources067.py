"""Bounded metadata-only candidate discovery. Never fetch torrent payloads."""
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import urllib.request, urllib.error, json, time
out=Path('docs/evidence/v067/probe');out.mkdir(parents=True,exist_ok=True)
targets={
 'torrentscsv':'https://torrents-csv.com/service/search?q=ubuntu&size=20',
 'torrentproject':'https://torrentproject.se/?s=ubuntu&out=json&num=20',
 'limetorrents':'https://www.limetorrents.lol/search/all/ubuntu/',
 'btdig':'https://btdig.com/search?q=ubuntu',
 'tokyotosho':'https://www.tokyotosho.info/rss.php?terms=blender',
 'academic':'https://academictorrents.com/browse.php?search=mnist',
 'eztv':'https://eztvx.to/api/get-torrents?limit=1',
 '1337x':'https://1337x.to/search/ubuntu/1/'
}
def probe(pair):
 name,url=pair;start=time.monotonic()
 try:
  request=urllib.request.Request(url,headers={'User-Agent':'Mozilla/5.0 (compatible; AFMC-FIND/0.6.7)'})
  with urllib.request.urlopen(request,timeout=18) as response:
   data=response.read(2*1024*1024+1)
   if len(data)>2*1024*1024:raise ValueError('response too large')
   (out/(name+'.txt')).write_bytes(data)
   return dict(name=name,url=url,final=response.url,status=response.status,bytes=len(data),seconds=round(time.monotonic()-start,2),sample=data[:110].decode('utf-8','replace'))
 except Exception as error:return dict(name=name,url=url,error=str(error),seconds=round(time.monotonic()-start,2))
with ThreadPoolExecutor(max_workers=3) as pool:results=list(pool.map(probe,targets.items()))
(out/'summary.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(results,ensure_ascii=False,indent=2))
