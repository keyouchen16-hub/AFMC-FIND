"""Prepare app-local pinned Python/libtorrent; no global Python installation."""
import hashlib, json, pathlib, urllib.request, zipfile
root = pathlib.Path(__file__).resolve().parent.parent
cache = root / '.tools' / 'streaming-packages'
target = root / '.tools' / 'libtorrent-runtime'
cache.mkdir(parents=True, exist_ok=True)
target.mkdir(parents=True, exist_ok=True)
def download(url, name, expected=None):
    path = cache / name
    if not path.exists():
        with urllib.request.urlopen(url, timeout=60) as response:
            path.write_bytes(response.read())
    actual = hashlib.sha256(path.read_bytes()).hexdigest()
    if expected and actual != expected:
        raise ValueError('Package digest mismatch: ' + name)
    print(name, actual)
    return path, actual
with urllib.request.urlopen('https://pypi.org/pypi/libtorrent/2.0.15/json', timeout=30) as response:
    package = json.load(response)
wheel = next(p for p in package['urls'] if p['filename'].endswith('cp312-cp312-win_amd64.whl'))
if wheel['digests']['sha256'] != '948c0f5ecd49582c7c9e807f71535e895bb19c648310fa65c8fce1e527a4e514':
    raise ValueError('Pinned libtorrent wheel changed')
binary, binary_hash = download(wheel['url'], wheel['filename'], wheel['digests']['sha256'])
runtime, runtime_hash = download('https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip', 'python-3.12.10-embed-amd64.zip', '4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3')
for archive in (runtime, binary):
    with zipfile.ZipFile(archive) as z:
        for entry in z.infolist():
            dest = (target / entry.filename).resolve()
            if not dest.is_relative_to(target.resolve()):
                raise ValueError('Unsafe archive path')
        z.extractall(target)
(target / 'python312._pth').write_text('python312.zip\n.\n', encoding='utf-8')
license_file = cache / 'libtorrent-2.0.15-LICENSE.txt'
if not license_file.exists():
    with urllib.request.urlopen('https://raw.githubusercontent.com/arvidn/libtorrent/v2.0.15/LICENSE', timeout=30) as response:
        license_file.write_bytes(response.read())
(target / 'libtorrent-LICENSE.txt').write_bytes(license_file.read_bytes())
manifest = {'python': '3.12.10', 'libtorrent': '2.0.15', 'archives': {runtime.name: runtime_hash, binary.name: binary_hash},
            'files': {str(p.relative_to(target)).replace('\\','/'): hashlib.sha256(p.read_bytes()).hexdigest()
                      for p in target.rglob('*') if p.is_file() and p.name != 'manifest.json'}}
(target / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
