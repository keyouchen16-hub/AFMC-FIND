from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import urllib.request,json,xml.etree.ElementTree as ET,re
out=Path('docs/evidence/v067/probe')
targets={
 'csv-doc':'https://codeberg.org/heretic/torrents-csv-server/raw/branch/main/README.md',
 'csv-page2':'https://torrents-csv.com/service/search?q=ubuntu&after=755294',
 'csv-empty':'https://torrents-csv.com/service/search?q=afmcnoresult472918',
 'lime-empty':'https://www.limetorrents.fun/search/all/afmcnoresult472918/seeds/1/',
 'lime-rss':'https://www.limetorrents.fun/searchrss/ubuntu/',
 'tokyo-empty':'https://www.tokyotosho.info/rss.php?terms=afmcnoresult472918',
 'academic-database':'https://academictorrents.com/database.xml'
}
def run(pair):
 name,url=pair
 try:
  req=urllib.request.Request(url,headers={'User-Agent':'Mozilla/5.0 (compatible; AFMC-FIND/0.6.7)'})
  with urllib.request.urlopen(req,timeout=25) as r:
   data=r.read(16*1024*1024+1)
   if len(data)>16*1024*1024:raise ValueError('over16MB')
   (out/(name+'.txt')).write_bytes(data)
   result=dict(name=name,status=r.status,bytes=len(data),final=r.url)
   if data.startswith(b'{'):
    j=json.loads(data);result.update(keys=list(j),count=len(j.get('torrents',[])),next=j.get('next'))
   elif data.startswith(b'<?xml'):
    root=ET.fromstring(data);items=root.findall('.//item');result.update(count=len(items),first=ET.tostring(items[0],encoding='unicode')[:1800] if items else None)
   else:result['sample']=data[:180].decode('utf-8','replace')
   return result
 except Exception as e:return dict(name=name,error=str(e))
with ThreadPoolExecutor(max_workers=3) as pool:r=list(pool.map(run,targets.items()))
(out/'formats-summary.json').write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(r,ensure_ascii=False,indent=2))
