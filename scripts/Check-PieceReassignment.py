"""Real loopback BT wire fixture: one missing block, two independently timed peers.

Synthetic bytes are not a movie/startup benchmark. No tracker, DHT, WAN or keys.
"""
import argparse, hashlib, heapq, importlib.util, json, pathlib, secrets
import select, socket, struct, threading, time, uuid, os, sys
if os.environ.get('AFMC_NATIVE_MODULE'):
    sys.path.insert(0, os.environ['AFMC_NATIVE_MODULE'])
import libtorrent as lt

ROOT = pathlib.Path(__file__).resolve().parent.parent
PIECE = 256 * 1024
COUNT = 128
BLOCK = 16 * 1024

def payload(index, offset, length):
    pattern = hashlib.sha256(struct.pack('!I', index)).digest()
    data = pattern * (PIECE // len(pattern))
    return data[offset:offset+length]

class Peer:
    def __init__(self, name, infohash, events, clock, slow=False, dribble=False, fault=None):
        self.name, self.infohash, self.events, self.clock, self.slow = name, infohash, events, clock, slow
        self.dribble = dribble
        self.fault = fault
        self.listener = socket.socket()
        self.listener.bind(('127.0.0.1', 0))
        self.listener.listen(1)
        self.listener.settimeout(.2)
        self.port = self.listener.getsockname()[1]
        self.stop = threading.Event()
        self.held = threading.Event()
        self.connected = False
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def event(self, action, **fields):
        self.events.append({'seconds': round(time.monotonic()-self.clock, 4), 'peer': self.name,
                            'action': action, **fields})

    def run(self):
        connection = None
        try:
            while not self.stop.is_set():
                try:
                    connection, _ = self.listener.accept()
                    break
                except socket.timeout:
                    continue
            if connection is None:
                return
            connection.settimeout(.5)
            data = b''
            while len(data) < 68:
                chunk = connection.recv(68-len(data))
                if not chunk:
                    return
                data += chunk
            assert data[0] == 19 and data[28:48] == self.infohash
            connection.sendall(b'\x13BitTorrent protocol'+b'\0'*8+self.infohash+b'-AF0001-'+secrets.token_bytes(12))
            bits = b'\x80'+b'\0'*15 if self.slow else b'\xff'*16
            connection.sendall(struct.pack('!I', 1+len(bits))+b'\x05'+bits+b'\0\0\0\x01\x01')
            self.connected = True
            self.event('connected')
            queue, incoming, serial = [], b'', 0
            fragment, fragment_next = None, 0
            next_send = time.monotonic()
            while not self.stop.is_set():
                ready, _, _ = select.select([connection], [], [], .01)
                if ready:
                    chunk = connection.recv(65536)
                    if not chunk:
                        self.event('remote_closed')
                        return
                    incoming += chunk
                while len(incoming) >= 4:
                    length = struct.unpack('!I', incoming[:4])[0]
                    if length > 65536:
                        raise ValueError('unexpected message length')
                    if len(incoming) < 4+length:
                        break
                    message, incoming = incoming[4:4+length], incoming[4+length:]
                    if not message:
                        continue
                    if message[0] in (6, 8) and len(message) == 13:
                        index, begin, size = struct.unpack('!III', message[1:])
                        assert 0 <= index < COUNT and 0 < size <= BLOCK and begin+size <= PIECE
                        if message[0] == 8:
                            self.event('cancel', piece=index, block=begin//BLOCK)
                            queue = [q for q in queue if q[2:] != (index, begin, size)]
                            heapq.heapify(queue)
                        else:
                            self.event('request', piece=index, block=begin//BLOCK)
                            held = self.slow and index == 0 and begin == 0
                            delay = (.4 if self.dribble else 20) if held else .02
                            if held:
                                self.held.set()
                            serial += 1
                            heapq.heappush(queue, (time.monotonic()+delay, serial, index, begin, size))
                now = time.monotonic()
                if fragment is not None:
                    if now >= fragment_next:
                        connection.sendall(fragment[:128])
                        fragment = fragment[128:] or None
                        fragment_next = now + .2
                    continue  # Complete this BT message before sending another.
                if queue and queue[0][0] <= now and now >= next_send:
                    _, _, index, begin, size = heapq.heappop(queue)
                    if self.dribble and index == 0 and begin == 0:
                        connection.sendall(struct.pack('!IBII',size+9,7,index,begin)+payload(index,begin,1024))
                        fragment = payload(index,begin+1024,size-1024)
                        fragment_next = now + .2
                        self.event('partial',piece=index,block=0)
                        continue
                    body = payload(index,begin,size)
                    if self.fault and index == 0 and begin == 0:
                        fault,self.fault = self.fault,None
                        if fault == 'partial-disconnect':
                            connection.sendall(struct.pack('!IBII',size+9,7,index,begin)+body[:size//2])
                            self.event('partial-disconnect',piece=index,block=0)
                            return
                        if fault == 'corrupt-once':
                            body = bytes([body[0]^1])+body[1:]
                            self.event('corrupt-once',piece=index,block=0)
                    connection.sendall(struct.pack('!IBII', size+9, 7, index, begin)+body)
                    self.event('sent', piece=index, block=begin//BLOCK)
                    next_send = now + (0 if self.slow else size/(4*1024*1024))
        except (OSError, AssertionError, ValueError) as error:
            if not self.stop.is_set():
                self.event('error', kind=type(error).__name__)
        finally:
            self.connected = False
            if connection:
                connection.close()

    def close(self):
        self.stop.set()
        self.thread.join(timeout=2)
        self.listener.close()

class LocalNative:
    def __getattr__(self, key):
        return getattr(lt, key)
    def session(self, settings):
        return lt.session(dict(settings, enable_dht=False, enable_lsd=False,
            enable_upnp=False, enable_natpmp=False, listen_interfaces='127.0.0.1:0',
            outgoing_interfaces='127.0.0.1', enable_outgoing_utp=False, enable_incoming_utp=False,
            allow_multiple_connections_per_ip=True, out_enc_policy=lt.enc_policy.disabled,
            in_enc_policy=lt.enc_policy.disabled))

def run(mode):
    root = ROOT / '.tools/piece-reassign-fixture' / uuid.uuid4().hex
    cache = root / uuid.uuid4().hex
    cache.mkdir(parents=True)
    info = {b'name': b'controlled.mp4', b'length': PIECE*COUNT, b'piece length': PIECE,
            b'pieces': b''.join(hashlib.sha1(payload(i, 0, PIECE)).digest() for i in range(COUNT))}
    infohash = hashlib.sha1(lt.bencode(info)).digest()
    torrent = root / 'fixture.torrent'
    torrent.write_bytes(lt.bencode({b'info': info}))
    spec = importlib.util.spec_from_file_location('controlled_worker', ROOT/'src/Afmc.Find.App/Streaming/stream_worker.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.lt = LocalNative()
    worker = module.Worker({'token': secrets.token_hex(32), 'cache': str(cache), 'input': str(torrent)})
    events, errors, result = [], [], {}
    clock = time.monotonic()
    fault = {'native-partial-fallback':'partial-disconnect','native-corrupt':'corrupt-once'}.get(mode)
    a, b = Peer('slow', infohash, events, clock, True, mode=='native-dribble'), Peer('fast', infohash, events, clock,fault=fault)
    def update():
        try:
            while not worker.stop.wait(.03):
                worker.update()
        except Exception as error:
            errors.append(type(error).__name__)
    updater = threading.Thread(target=update, daemon=True)
    updater.start()
    reader = None
    try:
        while not worker.info and time.monotonic()-clock < 3:
            time.sleep(.03)
        assert worker.info and len(worker.videos) == 1
        generation = worker.select(0)['generation']
        worker.handle.connect_peer(('127.0.0.1', a.port))
        assert a.held.wait(5), 'slow fixture never received blocking request'
        time.sleep(.5)
        if mode != 'native-only-supplier':
            worker.handle.connect_peer(('127.0.0.1', b.port))
        def read():
            try:
                data = worker.read(0, 0, 128*1024-1, generation)
                result.update(readSeconds=time.monotonic()-clock, bytesMatch=data == payload(0, 0, len(data)))
            except Exception as error:
                result['readError'] = type(error).__name__
        reader = threading.Thread(target=read, daemon=True)
        reader.start()
        while time.monotonic()-clock < 6 and not worker.handle.have_piece(0):
            rates = {p.ip[1]: p.payload_down_speed for p in worker.handle.get_peer_info()}
            if rates.get(b.port, 0) > max(16384, rates.get(a.port, 0)*3):
                break
            time.sleep(.03)
        result['fixtureVersion'] = 6
        result['preRetryRates'] = {('slow' if p.ip[1] == a.port else 'fast'): p.payload_down_speed
                                  for p in worker.handle.get_peer_info()}
        if mode == 'priority-reset':
            worker.handle.piece_priority(0, 0)
            time.sleep(.3)
            worker.handle.piece_priority(0, 7)
        elif mode == 'deadline':
            worker.handle.unset_flags(lt.torrent_flags.sequential_download)
            worker.handle.set_piece_deadline(0, 0)
        elif mode == 'cancel-reassign':
            worker.handle.piece_priority(0, 0)
            worker.handle.unset_flags(lt.torrent_flags.sequential_download)
            worker.handle.clear_piece_deadlines()
            assert not worker.handle.have_piece(31), 'placeholder already complete'
            worker.handle.set_piece_deadline(31, 1000)
            time.sleep(.3)
            worker.handle.set_piece_deadline(0, 0)
            worker.handle.reset_piece_deadline(31)
        elif mode.startswith('native-'):
            result['partialAtRetry'] = next((p.downloading_progress for p in worker.handle.get_peer_info()
                if p.ip[1]==a.port and p.downloading_piece_index==0 and p.downloading_block_index==0),0)
            result['nativeReassigned'] = worker.handle.afmc_retry_piece(0, ('127.0.0.1', a.port), 8000)
            result['nativeRepeat'] = worker.handle.afmc_retry_piece(0, ('127.0.0.1', a.port), 8000)
            result['invalidPiece'] = worker.handle.afmc_retry_piece(-1, ('127.0.0.1', a.port), 8000)
            result['unknownPeer'] = worker.handle.afmc_retry_piece(0, ('127.0.0.1', 1), 8000)
            if mode == 'native-fallback':
                b.close()
        elif mode != 'baseline':
            raise ValueError('unknown mode')
        reader.join(timeout=40 if mode == 'native-corrupt' else 26)
        result.update(mode=mode, hashFailures=worker.hash_failures,
            retainedConnections=a.connected and b.connected, errors=errors,
            delayedBlockCancelled=any(e['peer']=='slow' and e['action']=='cancel' and e.get('piece')==0 and e.get('block')==0 for e in events),
            delayedBlockRequestedFromFast=any(e['peer']=='fast' and e['action']=='request' and e.get('piece')==0 and e.get('block')==0 for e in events),
            events=events)
    finally:
        worker.stop.set()
        with worker.changed:
            worker.changed.notify_all()
        if reader:
            reader.join(timeout=2)
        updater.join(timeout=2)
        worker.session.pause()
        a.close(); b.close()
    output = ROOT / 'docs/evidence/piece-reassignment'
    output.mkdir(parents=True, exist_ok=True)
    evidence = output/(mode+'-'+root.name+'.json')
    evidence.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k != 'events'}))
    # Native session may still own files: retain this small isolated fixture.
    assert result.get('bytesMatch') and not errors
    assert worker.hash_failures >= 1 if mode == 'native-corrupt' else worker.hash_failures == 0
    if mode in ('native-reassign','native-dribble'):
        assert result['nativeReassigned'] == 1
        assert result['nativeRepeat'] == result['invalidPiece'] == result['unknownPeer'] == 0
        assert result['retainedConnections']
        assert result['delayedBlockRequestedFromFast']
        requests = [e for e in events if e['peer']=='slow' and e['action']=='request' and e.get('piece')==0 and e.get('block')==0]
        assert len(requests) == 1
        if mode == 'native-dribble':
            assert 0 < result['partialAtRetry'] < BLOCK
    if mode in ('native-fallback','native-partial-fallback'):
        assert result['nativeReassigned'] == 1
        assert result['readSeconds'] < 22 and result['bytesMatch']
        if mode == 'native-partial-fallback':
            assert any(e['action']=='partial-disconnect' for e in events)
    if mode == 'native-only-supplier':
        assert result['nativeReassigned'] == 0 and not result['delayedBlockCancelled']
        assert result['readSeconds'] < 22
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['baseline', 'priority-reset', 'deadline', 'cancel-reassign', 'native-reassign', 'native-fallback', 'native-only-supplier', 'native-dribble', 'native-partial-fallback', 'native-corrupt'])
    run(parser.parse_args().mode)
