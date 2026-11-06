"""Local pre-upload hygiene check. Reports paths/rules only, never secret values."""
import subprocess, pathlib, re, json, sys, urllib.parse

root = pathlib.Path(__file__).resolve().parent.parent
raw = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=root)
names = sorted(set(n for n in raw.decode('utf-8').split('\0') if n))
rules = {
    'github-token': re.compile(r'\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b'),
    'openai-token': re.compile(r'\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{40,}\b'),
    'aws-access-id': re.compile(r'\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'),
    'private-key': re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'),
    'credential-url': re.compile(r'https?://[^\s/\"\x27:@]+:[^\s/\"\x27@]+@'),
    'literal-api-key': re.compile(r'(?i)(?:api[_-]?key|access[_-]?token|client[_-]?secret)\s*[=:]\s*[\"\x27]([A-Za-z0-9_\-]{24,})[\"\x27]'),
}
findings, sizes = [], []
# Exact, reviewed negative-validation fixtures, not a blanket tests/ exemption.
fixture_hosts = {
    'tests/Afmc.Find.Checks/BrowserDiscoveryChecks.cs': {'pan.quark.cn'},
    'tests/Afmc.Find.Checks/DiscoveryChecks.cs': {'unlisted.example'},
    'tests/Afmc.Find.Checks/JackettChecks.cs': {'example.org'},
    'tests/Afmc.Find.Checks/Program.cs': {'example.com','image.tmdb.org'},
}
fixture_credentials = {('user','pass'),('user','secret'),('u','p'),('name','pass'),('a','b'),('user','password')}
reviewed_fixtures = 0
binary = {'.png','.jpg','.jpeg','.ico','.gif','.webp','.woff','.woff2'}
for name in names:
    path = root/name
    if not path.is_file():
        continue
    size = path.stat().st_size
    sizes.append((name, size))
    if size > 20*1024*1024:
        findings.append({'path':name, 'rule':'large-file'})
    if any(p in {'.tools','artifacts','bin','obj','__pycache__'} for p in path.relative_to(root).parts):
        findings.append({'path':name,'rule':'runtime-or-build-output'})
    if path.name in {'.env','sources.json','user.config','credentials.json','cookies.txt'} or path.suffix in {'.pfx','.p12','.key','.torrent','.fastresume','.mp4','.mkv','.sqlite','.db','.dmp'}:
        findings.append({'path':name,'rule':'private-or-payload-file'})
    if path.suffix.lower() in binary:
        continue
    try:
        text = path.read_text(encoding='utf-8-sig')
    except UnicodeError:
        findings.append({'path':name, 'rule':'unrecognized-binary'})
        continue
    for rule, pattern in rules.items():
        for match in pattern.finditer(text):
            if rule == 'credential-url':
                full = re.match(r'https?://[^\s"\x27]+', text[match.start():])
                parsed = urllib.parse.urlsplit(full[0]) if full else None
                if parsed and parsed.hostname in fixture_hosts.get(name,set()) and (parsed.username,parsed.password) in fixture_credentials:
                    reviewed_fixtures += 1
                    continue
            findings.append({'path':name,'line':text[:match.start()].count('\n')+1,'rule':rule})
report = {'files':len(sizes), 'bytes':sum(s for _,s in sizes), 'findings':findings, 'reviewedNegativeFixtures':reviewed_fixtures,
          'largest':sorted(sizes,key=lambda item:item[1],reverse=True)[:10]}
out = root/'.tools/github-sync'
out.mkdir(parents=True,exist_ok=True)
(out/'preflight.json').write_text(json.dumps(report,indent=2,ensure_ascii=True),encoding='utf-8')
print(json.dumps(report,ensure_ascii=True))
sys.exit(bool(findings))
