import concurrent.futures, urllib.request, json, re
from pathlib import Path
from html.parser import HTMLParser
urls = ['https://www.tpbproxypirate.com/', 'https://m.zhongzilou.com/', 'https://qiankun6.com/', 'https://btaa.xyz/', 'https://www.cilihezi.top/', 'https://heimacili.com/']
folder = Path('docs/evidence/source-audit'); folder.mkdir(parents=True, exist_ok=True)
class Parser(HTMLParser):
    def __init__(self): super().__init__(); self.forms=[]; self.scripts=[]; self.inputs=[]; self.links=[]
    def handle_starttag(self, tag, attrs):
        a=dict(attrs)
        if tag=='form': self.forms.append(a)
        if tag=='script' and a.get('src'): self.scripts.append(a['src'])
        if tag=='input': self.inputs.append(a)
        if tag=='a' and a.get('href','').startswith('magnet:'): self.links.append(a['href'])
def probe(url):
    try:
        req=urllib.request.Request(url, headers={'User-Agent':'Mozilla/5.0'})
        with urllib.request.urlopen(req, timeout=20) as r:
            raw=r.read(2000000); content=raw.decode('utf-8',errors='replace'); p=Parser(); p.feed(content)
            host=urllib.parse.urlparse(url).hostname
            (folder/(host+'.html')).write_text(content,encoding='utf-8')
            return dict(url=url, final=r.url, status=r.status, title=re.findall(r'<title[^>]*>(.*?)</title>',content,re.S)[:1],forms=p.forms,inputs=p.inputs[:15],scripts=p.scripts[:15],magnets=len(p.links))
    except Exception as e: return dict(url=url,error=type(e).__name__+': '+str(e))
with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool: results=list(pool.map(probe,urls))
(folder/'audit.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(results,ensure_ascii=False,indent=2))
