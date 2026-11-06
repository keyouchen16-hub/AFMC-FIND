"""Local worker boundary tests. No tokens or resource addresses in output."""
import json, pathlib, secrets, subprocess, time, urllib.request, urllib.error, uuid, shutil, sys, threading
root = pathlib.Path(__file__).resolve().parent.parent
cache_parent = root / '.tools' / 'worker-boundary-checks'
cache_parent.mkdir(exist_ok=True)
cache = cache_parent / uuid.uuid4().hex
cache.mkdir()
token = secrets.token_hex(32)
process = subprocess.Popen([str(root / '.tools/libtorrent-runtime/python.exe'), '-I', '-u',
    str(root / 'src/Afmc.Find.App/Streaming/stream_worker.py')], stdin=subprocess.PIPE,
    stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, creationflags=subprocess.CREATE_NO_WINDOW)
def request(path, data=None, authenticated=True, headers=None):
    request_headers = {'X-AFMC-Token': token} if authenticated else {}
    request_headers.update(headers or {})
    req = urllib.request.Request(base + path, data=data, headers=request_headers)
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()
try:
    process.stdin.write(json.dumps({'token': token, 'cache': str(cache),
        'input': str(root / '.tools/player-installed-fixture/AFMC-original.torrent')}) + '\n')
    process.stdin.flush()
    ready = json.loads(process.stdout.readline())
    base = 'http://127.0.0.1:' + str(ready['port'])
    assert request('/status', authenticated=False)[0] == 403
    assert request('/status', headers={'X-AFMC-Token': 'wrong'})[0] == 403
    print('PASS unauthorized and wrong-token clients rejected')
    assert request('/rate', b'{"rate":-1}')[0] == 400
    assert request('/rate', b'{"rate":0}', authenticated=False)[0] == 403
    assert request('/rate', b'{"rate":0}')[0] == 200
    print('PASS foreground rate promotion requires owner token and a valid limit')
    until = time.monotonic() + 10
    while time.monotonic() < until:
        code, body = request('/status')
        status = json.loads(body)
        if status['metadata']: break
        time.sleep(.1)
    assert status['metadata'] and status['files']
    assert status['verified'] == 0 and status['downloaded'] == 0
    print('PASS metadata alone downloads no payload and grants no verified status')
    assert request('/select', b'{"id":99999}')[0] == 400
    assert request('/file/0?generation=0', headers={'Range':'bytes=0-8'})[0] == 503
    print('PASS invalid selection and unselected file reads rejected')
    file = status['files'][0]
    code, body = request('/select', json.dumps({'id':file['id']}).encode())
    assert code == 200
    generation = json.loads(body)['generation']
    assert request('/file/'+str(file['id'])+'?generation='+str(generation), headers={'Range':'bytes=0-1048576'})[0] == 416
    print('PASS oversized range rejected before allocation')
    if '--ranges' in sys.argv:
        def monitor():
            for _ in range(12):
                time.sleep(2)
                try:
                    state = json.loads(request('/status')[1])
                    print('READ', state['reading'], state['downloaded'], state['diagnostic'], flush=True)
                except Exception: return
        threading.Thread(target=monitor, daemon=True).start()
        offset = 0
        while offset < 2*1024*1024:
            code, body = request('/file/'+str(file['id'])+'?generation='+str(generation), headers={'Range':f'bytes={offset}-{offset+128*1024-1}'})
            assert code == 206 and 0 < len(body) <= 128*1024
            offset += len(body)
        print('PASS 2 MiB continuous verified reads including short-read boundaries',flush=True)
    started = time.monotonic()
    process.stdin.close()
    process.wait(timeout=8)
    assert process.returncode == 0
    print('PASS owner pipe EOF stops worker in %.2fs' % (time.monotonic()-started))
finally:
    if process.poll() is None:
        process.kill(); process.wait()
    if cache.resolve().parent == cache_parent.resolve():
        shutil.rmtree(cache)
