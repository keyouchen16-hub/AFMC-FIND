import urllib.request,urllib.parse,pathlib,re
p=pathlib.Path('docs/evidence/global-sources')
data=urllib.parse.urlencode({'classid':'1,2','show':'title','tempid':'1','keyboard':'特洛伊'}).encode()
req=urllib.request.Request('https://www.cilixiong.cc/e/search/index.php',data=data,headers={'User-Agent':'Mozilla/5.0'})
with urllib.request.urlopen(req,timeout=20) as r:
 body=r.read(2000000);(p/'cilixiong-search.html').write_bytes(body);print(r.status,r.url);s=body.decode('utf-8');i=s.find('/movie/');print(s[max(0,i-700):i+1000])
with urllib.request.urlopen(urllib.request.Request('https://www.cilixiong.cc/movie/4745.html',headers={'User-Agent':'Mozilla/5.0'}),timeout=20) as r:
 body=r.read(2000000);(p/'cilixiong-detail.html').write_bytes(body);s=body.decode('utf-8');i=s.find('magnet:');print(s[max(0,i-400):i+800]);print(re.findall(r'<img[^>]+rounded-2[^>]+>',s)[:2])
