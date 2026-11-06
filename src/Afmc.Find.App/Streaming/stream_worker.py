"""AFMC's authenticated, loopback-only libtorrent byte-range worker.

The owner supplies one JSON configuration on stdin. EOF ends the process.
Only verified read_piece alerts may supply bytes to the decoder.
"""
import collections
import hmac
import http.server
import json
import ipaddress
import pathlib
import re
import socket
import sys
import threading
import time
import urllib.parse
import libtorrent as lt

VIDEO = {'.mp4', '.mkv', '.webm', '.avi', '.mov', '.m4v', '.ts', '.m2ts', '.mpg', '.mpeg', '.wmv', '.flv', '.ogv'}
MAX_READ = 1024 * 1024


def routing_nodes(values):
    """Accept only bounded public compact DHT endpoints, never arbitrary state."""
    if not isinstance(values, list) or len(values) > 256:
        return []
    result = []
    for value in values:
        if not isinstance(value, str) or len(value) not in (12, 36):
            continue
        try:
            node = bytes.fromhex(value)
            address = ipaddress.ip_address(node[:-2])
            if address.is_global and not address.is_multicast and int.from_bytes(node[-2:], 'big') and node not in result:
                result.append(node)
        except ValueError:
            pass
    return result


def transferred_peers(info, peers):
    if info is None or info.priv():
        return []
    candidates = []
    for peer in sorted(peers, key=lambda p: p.total_download, reverse=True):
        if peer.total_download <= 0:
            continue
        try:
            address, port = peer.ip
            candidates.append((ipaddress.ip_address(address).packed + int(port).to_bytes(2, 'big')).hex())
        except (ValueError, OverflowError):
            continue
    return [n.hex() for n in routing_nodes(candidates[:64])]


class Worker:
    def __init__(self, config):
        self.token = config['token']
        if not isinstance(self.token, str) or len(self.token) != 64:
            raise ValueError('invalid owner token')
        self.root = pathlib.Path(config['cache']).resolve()
        if not self.root.is_dir() or not re.fullmatch('[0-9a-f]{32}', self.root.name):
            raise ValueError('invalid session cache')
        self.content = self.root / 'content'
        self.content.mkdir(exist_ok=True)
        self.stop = threading.Event()
        self.lock = threading.RLock()
        self.changed = threading.Condition(self.lock)
        self.selected = None
        self.generation = 0
        self.videos = []
        self.info = None
        self.error = None
        self.pieces = collections.OrderedDict()
        self.requested = set()
        self.read_slots = threading.BoundedSemaphore(4)
        self.read_position = -1
        self.awaiting_piece = -1
        self.piece_error = 0
        self.hash_failures = 0
        self.web_seed_errors = 0
        self.last_window = None
        self.received_total = 0
        self.last_payload = 0
        self.session = lt.session({
            'enable_upnp': False, 'enable_natpmp': False, 'enable_lsd': False,
            'listen_interfaces': '0.0.0.0:0',
            'download_rate_limit': max(0, min(int(config.get('rate', 0)), 100*1024*1024)),
            'upload_rate_limit': 128*1024, 'connections_limit': 160,
            # A completed read-ahead window is not a completed viewing session.
            # Retain seeds so the next seek/window can request bytes immediately.
            'close_redundant_connections': False,
            'dht_bootstrap_nodes': 'router.bittorrent.com:6881,dht.transmissionbt.com:6881',
            'alert_mask': int(lt.alert.category_t.all_categories)})
        nodes = routing_nodes(config.get('routing', []))
        # Reuse bootstrap endpoints only, not node identity, peers, metadata,
        # credentials or downloaded bytes. The native library verifies replies.
        if nodes:
            self.session.load_state({b'dht state': {b'nodes': [n for n in nodes if len(n) == 6],
                                                   b'nodes6': [n for n in nodes if len(n) == 18]}})
        self.routing_reused = len(nodes)
        self.routing_snapshot = []
        self.routing_next = 0
        value = config['input']
        if not isinstance(value, str) or len(value) > 16384:
            raise ValueError('invalid input')
        if value.lower().startswith('magnet:?'):
            params = lt.parse_magnet_uri(value)
        else:
            path = pathlib.Path(value)
            if path.suffix.lower() != '.torrent' or path.stat().st_size > 8*1024*1024:
                raise ValueError('invalid torrent file')
            params = lt.add_torrent_params()
            params.ti = lt.torrent_info(str(path))
        params.save_path = str(self.content)
        params.flags &= ~lt.torrent_flags.paused
        params.flags &= ~lt.torrent_flags.auto_managed
        # Metadata may arrive, but payload remains disabled until all paths are
        # validated and the owner explicitly chooses a video.
        params.flags |= lt.torrent_flags.upload_mode
        self.handle = self.session.add_torrent(params)
        # The owner only retains these for the exact same magnet after a
        # previous validated public torrent transferred payload. Native BT
        # handshakes still check the infohash; no payload before selection.
        peers = config.get('peers', [])
        peers = routing_nodes(peers) if isinstance(peers, list) and len(peers) <= 64 and value.lower().startswith('magnet:?') else []
        self.peers_reused = len(peers)
        self.peer_snapshot = []
        for peer in peers:
            self.handle.connect_peer((str(ipaddress.ip_address(peer[:-2])), int.from_bytes(peer[-2:], 'big')))
        self.status = {'state': 'Metadata', 'metadata': False, 'nodes': 0, 'peers': 0,
                       'downloaded': 0, 'verified': 0, 'progress': 0, 'rate': 0, 'files': []}
        self.last_status_at = 0
        # Each isolated worker starts with a cold DHT. Its initial lookup may
        # precede bootstrap; do not wait the normal long announce interval.
        # Bounded retries preserve the private-torrent/DHT flags in libtorrent.
        self.discovery_started = time.monotonic()
        self.discovery_retries = 0
        self.discovery_next = self.discovery_started + 5

    def validate_metadata(self):
        info = self.handle.torrent_file()
        files = info.files()
        if files.num_files() > 20000 or info.piece_length() > 32*1024*1024:
            raise ValueError('unsupported torrent layout')
        videos = []
        for index in range(files.num_files()):
            name = files.file_path(index)
            path = pathlib.PureWindowsPath(name)
            target = (self.content / name).resolve()
            if path.is_absolute() or path.drive or '..' in path.parts or ':' in name or not target.is_relative_to(self.content):
                raise ValueError('unsafe torrent path')
            if files.file_flags(index) & lt.file_storage.flag_symlink:
                raise ValueError('torrent symlinks are not supported')
            if pathlib.Path(name).suffix.lower() in VIDEO and files.file_size(index) > 0:
                videos.append({'id': index, 'path': name, 'length': files.file_size(index)})
        if not videos:
            raise ValueError('torrent has no directly playable video')
        self.info, self.videos = info, videos

    def select(self, index):
        with self.lock:
            file = next((v for v in self.videos if v['id'] == index), None)
            if file is None or self.error:
                raise ValueError('unknown video')
            self.generation += 1
            self.awaiting_piece = -1
            self.last_window = None
            self.handle.clear_piece_deadlines()
            # Keep unrelated payload out of the peer queues. Piece priorities
            # override file priorities; upload_mode prevents all payload before
            # validation and this first bounded selection.
            self.handle.set_flags(lt.torrent_flags.sequential_download)
            self.selected = file
            self.window(0, min(file['length'], 128*1024))
            self.handle.unset_flags(lt.torrent_flags.upload_mode)
            self.changed.notify_all()
            return {'generation': self.generation}

    def window(self, start, count):
        size = self.info.piece_length()
        offset = self.info.files().file_offset(self.selected['id'])
        first = (offset + start) // size
        # A container index can span several pieces before the first video
        # sample (large MP4 moov atoms, for example). Pipeline those reads.
        ahead = 8*1024*1024 if start == 0 else 16*1024*1024
        file_last = (offset + self.selected['length'] - 1) // size
        last = min(file_last, first + max(1, (ahead + size - 1)//size) - 1)
        # Sequential priorities keep the blocking piece first without mixing
        # the library's separate time-critical picker into the rolling queue.
        if self.last_window == (first, last):
            return
        self.last_window = (first, last)
        priorities = [0] * self.info.num_pieces()
        for index in range(first, last+1):
            # The blocked decoder read must win over speculative prefetch.
            # Equal priority lets a large peer queue fill later pieces first.
            priorities[index] = 7 if index == first else (6 if index == first+1 else 1)
        self.handle.prioritize_pieces(priorities)

    def piece(self, index, generation):
        deadline = time.monotonic() + 90
        with self.changed:
            self.awaiting_piece = index
            while not self.stop.is_set() and not self.error and generation == self.generation:
                if index in self.pieces:
                    data = self.pieces[index]
                    self.pieces.move_to_end(index)
                    return data
                if self.handle.have_piece(index) and index not in self.requested:
                    self.requested.add(index)
                    self.handle.read_piece(index)
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise TimeoutError('piece unavailable')
                self.changed.wait(min(remaining, 0.1))
        raise InterruptedError('read cancelled')

    def read(self, index, start, end, generation):
        with self.lock:
            if self.selected is None or self.selected['id'] != index or generation != self.generation:
                raise InterruptedError('video changed')
            if start < 0 or end < start or end >= self.selected['length'] or end-start+1 > MAX_READ:
                raise ValueError('invalid range')
            size = self.info.piece_length()
            self.read_position = start
            offset = self.info.files().file_offset(index) + start
            self.window(start, end-start+1)
        # A Stream read may return fewer bytes than requested. Return the first
        # verified piece immediately instead of blocking on the next boundary.
        remaining = min(end-start+1, size-offset % size)
        chunks = []
        while remaining:
            data = self.piece(offset//size, generation)
            part = data[offset % size:offset % size + min(remaining, size-offset % size)]
            if not part:
                raise IOError('short verified piece')
            chunks.append(part)
            offset += len(part)
            remaining -= len(part)
        return b''.join(chunks)

    def refresh_discovery(self, has_metadata, now, nodes=0):
        if (not has_metadata and self.discovery_retries < 4
                and now >= self.discovery_next
                and (nodes >= 8 or now-self.discovery_started >= 20)):
            self.handle.force_dht_announce()
            self.discovery_retries += 1
            self.discovery_next = now + 20

    def update(self):
        for alert in self.session.pop_alerts():
            if isinstance(alert, lt.url_seed_alert):
                self.web_seed_errors += 1
            if isinstance(alert, lt.hash_failed_alert):
                self.hash_failures += 1
            if isinstance(alert, lt.read_piece_alert):
                with self.changed:
                    index = int(alert.piece)
                    self.piece_error = alert.error.value()
                    self.requested.discard(index)
                    if alert.error.value() == 0:
                        self.pieces[index] = bytes(alert.buffer)
                        self.pieces.move_to_end(index)
                        while len(self.pieces) > 1 and sum(map(len, self.pieces.values())) > 32*1024*1024:
                            self.pieces.popitem(last=False)
                    self.changed.notify_all()
        now = time.monotonic()
        if now - self.last_status_at < .2:
            return
        self.last_status_at = now
        if now >= self.routing_next:
            self.routing_next = now + 10
            state = self.session.save_state().get(b'dht state', {})
            nodes = list(state.get(b'nodes', [])) + list(state.get(b'nodes6', []))
            self.routing_snapshot = [n.hex() for n in routing_nodes(
                [n.hex() for n in nodes[:256] if isinstance(n, bytes)])]
            self.peer_snapshot = transferred_peers(self.info, self.handle.get_peer_info())
        status = self.handle.status()
        self.refresh_discovery(status.has_metadata, now, self.session.status().dht_nodes)
        with self.lock:
            payload = status.total_payload_download
            self.received_total += payload-self.last_payload if payload >= self.last_payload else payload
            self.last_payload = payload
            received = self.received_total
            if status.has_metadata and self.info is None and not self.error:
                try:
                    self.validate_metadata()
                except Exception:
                    self.error = '种子文件结构不受支持或没有可直接播放的视频。'
                    self.handle.pause()
            if status.errc.value() and not self.error:
                self.error = '下载引擎报告磁盘或资源错误，请停止后重试。'
            verified = status.num_pieces * self.info.piece_length() if self.info else 0
            if self.info and self.handle.have_piece(self.info.num_pieces()-1):
                verified -= self.info.piece_length() - self.info.piece_size(self.info.num_pieces()-1)
            self.status = {'state': 'Downloading' if status.has_metadata else 'Metadata',
                'metadata': self.info is not None, 'nodes': self.session.status().dht_nodes,
                'peers': status.num_peers, 'downloaded': received,
                'verified': verified, 'progress': (verified*100/self.info.total_size()) if self.info and self.selected else 0,
                'rate': status.download_payload_rate, 'files': self.videos, 'error': self.error,
                'reading': self.read_position,
                'routing': self.routing_snapshot, 'routingReused': self.routing_reused,
                'peersReusable': self.peer_snapshot, 'peersReused': self.peers_reused,
                'diagnostic': 'piece=%s have=%s readError=%s state=%s pieceSize=%s hashFailures=%s webSeedErrors=%s queued=%s' % (
                    self.awaiting_piece, self.handle.have_piece(self.awaiting_piece) if self.awaiting_piece >= 0 else False,
                    self.piece_error, status.state, self.info.piece_length() if self.info else 0, self.hash_failures,
                    self.web_seed_errors, sum(p.download_queue_length for p in self.handle.get_peer_info()))}


class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'
    server_version = 'AFMC'

    def log_message(self, *_):
        pass  # Never log magnet inputs, authentication tokens or request URLs.

    def setup(self):
        super().setup()
        self.connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.connection.settimeout(100)

    def reply(self, code, data, content='application/json', byte_range=None):
        self.send_response(code)
        self.send_header('Content-Type', content)
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Cache-Control', 'no-store')
        if byte_range is not None:
            self.send_header('Content-Range', byte_range)
            self.send_header('Accept-Ranges', 'bytes')
        self.end_headers()
        try:
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError, TimeoutError):
            self.close_connection = True

    def authorized(self):
        if not hmac.compare_digest(self.headers.get('X-AFMC-Token', ''), self.server.worker.token):
            self.reply(403, b'{}')
            self.close_connection = True
            return False
        return True

    def do_GET(self):
        if not self.authorized():
            return
        worker = self.server.worker
        parsed = urllib.parse.urlsplit(self.path)
        try:
            if parsed.path == '/status':
                with worker.lock:
                    result = json.dumps(worker.status).encode()
                self.reply(200, result)
                return
            match = re.fullmatch(r'/file/(\d+)', parsed.path)
            byte_range = re.fullmatch(r'bytes=(\d+)-(\d+)', self.headers.get('Range', ''))
            query = urllib.parse.parse_qs(parsed.query)
            if not match or not byte_range or 'generation' not in query:
                self.reply(400, b'{}')
                return
            if not worker.read_slots.acquire(blocking=False):
                self.reply(429, b'{}')
                return
            try:
                data = worker.read(int(match[1]), int(byte_range[1]), int(byte_range[2]), int(query['generation'][0]))
            finally:
                worker.read_slots.release()
            length = worker.info.files().file_size(int(match[1]))
            self.reply(206, data, 'application/octet-stream', f'bytes {byte_range[1]}-{int(byte_range[1])+len(data)-1}/{length}')
        except (ValueError, KeyError):
            self.reply(416, b'{}')
        except (TimeoutError, InterruptedError, IOError):
            self.reply(503, b'{}')

    def do_POST(self):
        if not self.authorized():
            return
        try:
            length = int(self.headers.get('Content-Length', '0'))
            if not 0 < length <= 1024:
                self.reply(400, b'{}')
                self.close_connection = True
                return
            body = json.loads(self.rfile.read(length))
            if self.path == '/select':
                self.reply(200, json.dumps(self.server.worker.select(int(body['id']))).encode())
            elif self.path == '/rate':
                rate = int(body['rate'])
                if not 0 <= rate <= 100*1024*1024:
                    raise ValueError('invalid download limit')
                self.server.worker.session.apply_settings({'download_rate_limit': rate})
                self.reply(200, b'{}')
            elif self.path == '/stop':
                self.reply(200, b'{}')
                self.server.worker.stop.set()
            else:
                self.reply(404, b'{}')
        except (ValueError, KeyError, TypeError):
            self.reply(400, b'{}')


def main():
    raw = sys.stdin.buffer.readline(65537)
    if len(raw) > 65536:
        raise ValueError('configuration too large')
    worker = Worker(json.loads(raw))
    server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    server.daemon_threads = True
    server.worker = worker
    threading.Thread(target=server.serve_forever, daemon=True).start()
    def owner_pipe():
        while sys.stdin.buffer.read(1):
            pass
        worker.stop.set()
    threading.Thread(target=owner_pipe, daemon=True).start()
    print(json.dumps({'port': server.server_port, 'version': lt.version}), flush=True)
    try:
        while not worker.stop.wait(0.05):
            worker.update()
    finally:
        worker.stop.set()
        with worker.changed:
            worker.changed.notify_all()
        worker.session.pause()
        server.shutdown()
        server.server_close()
        worker.session.remove_torrent(worker.handle)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        # No credentials or native-library error details leave this process.
        print('AFMC download worker failed', file=sys.stderr, flush=True)
        sys.exit(1)
