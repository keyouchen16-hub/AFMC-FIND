# Injected only into an isolated benchmark worker, never the installed product.
import os as _trace_os

_trace_original_update = Worker.update

def _trace_update(self):
    _trace_original_update(self)
    now = time.monotonic()
    if now < getattr(self, '_trace_next', 0):
        return
    self._trace_next = now + 1
    if not hasattr(self, '_trace_start'):
        self._trace_start = now
        self._trace_ids = {}
        self._trace_requests = {}
        self._trace_bytes = 0
    output = _trace_os.environ.get('AFMC_PIECE_TRACE')
    if not output or self._trace_bytes > 16*1024*1024:
        return
    try:
        def peer_id(endpoint):
            key = str(endpoint)
            if not endpoint or key in ('None', "('0.0.0.0', 0)", "('::', 0)"):
                return 0
            if key not in self._trace_ids:
                self._trace_ids[key] = len(self._trace_ids)+1
            return self._trace_ids[key]
        target = self.awaiting_piece
        native = self.handle.status()
        queues = self.handle.get_download_queue() if self.info else []
        queue = next((p for p in queues if p['piece_index'] == target), None)
        current = {}
        missing = []
        if queue:
            for index, block in enumerate(queue['blocks']):
                state = int(block['state'])
                if state == 3:
                    continue
                pid = peer_id(block.get('peer'))
                key = (target, index, pid, state)
                since = self._trace_requests.get(key, now)
                current[key] = since
                missing.append({'block': index, 'state': state, 'peer': pid,
                    'age': round(now-since, 2), 'copies': block.get('num_peers'),
                    'progress': block.get('bytes_progress'), 'size': block.get('block_size')})
        self._trace_requests = current
        peers = []
        for p in self.handle.get_peer_info():
            has_piece = bool(p.pieces[target]) if 0 <= target < len(p.pieces) else False
            peers.append({'id': peer_id(p.ip), 'has': has_piece,
                'localChoking': bool(p.flags & lt.peer_info.choked),
                'remoteChoking': bool(p.flags & lt.peer_info.remote_choked), 'snubbed': bool(p.flags & lt.peer_info.snubbed),
                'rate': p.payload_down_speed, 'total': p.total_download,
                'queue': p.download_queue_length, 'queueTime': str(p.download_queue_time),
                'queueBytes': p.queue_bytes, 'timeout': p.request_timeout,
                'receivingPiece': p.downloading_piece_index, 'receivingBlock': p.downloading_block_index,
                'receivingProgress': p.downloading_progress, 'hashFails': p.num_hashfails})
        settings = self.session.get_settings()
        row = {'seconds': round(now-self._trace_start, 3), 'reading': self.read_position,
            'required': target, 'have': self.handle.have_piece(target) if target >= 0 else False,
            'window': self.last_window, 'downloaded': native.total_payload_download,
            'verifiedPieces': native.num_pieces, 'rate': native.download_payload_rate,
            'redundantBytes': native.total_redundant_bytes, 'failedBytes': native.total_failed_bytes,
            'hashFailures': self.hash_failures, 'pieces': [
                {'index':p['piece_index'],'blocks':len(p['blocks']),
                 'finished':sum(int(b['state'])==3 for b in p['blocks']),
                 'requested':sum(int(b['state'])==1 for b in p['blocks']),
                 'writing':sum(int(b['state'])==2 for b in p['blocks'])}
                for p in queues], 'missing': missing, 'peers': peers,
            'settings': {k:settings.get(k) for k in ('request_timeout','piece_timeout','request_queue_time',
                'whole_pieces_threshold','max_out_request_queue','strict_end_game_mode','prefer_partial_pieces')}}
        row['retryBlocks'] = getattr(self, '_native_retry_count', 0)
        row['retryEvents'] = getattr(self, '_native_retry_events', 0)
        if self.info and self.selected and self.read_position >= 0:
            size = self.info.piece_length()
            offset = self.info.files().file_offset(self.selected['id'])
            absolute = offset + self.read_position
            end = offset + self.selected['length']
            cursor = absolute
            # Bounded scan: a verified, contiguous supply window, not total bytes.
            for index in range(absolute//size, min(self.info.num_pieces(), absolute//size+128)):
                if not self.handle.have_piece(index):
                    break
                cursor = min(end, (index+1)*size)
                if cursor == end:
                    break
            row['contiguousBytes'] = max(0, cursor-absolute)
            row['suppliersWithRequiredPiece'] = sum(p['has'] for p in peers)
            row['activeSuppliersWithRequiredPiece'] = sum(p['has'] and not p['remoteChoking'] and p['rate'] > 0 for p in peers)
        if queue:
            row['blockFields'] = list(queue['blocks'][0]) if queue['blocks'] else []
        encoded = json.dumps(row, separators=(',', ':'))+'\n'
        with open(output, 'a', encoding='utf-8') as f:
            f.write(encoded)
        self._trace_bytes += len(encoded)
    except Exception as e:
        with open(output, 'a', encoding='utf-8') as f:
            f.write(json.dumps({'seconds':round(now-self._trace_start,3),'traceError':type(e).__name__})+'\n')

Worker.update = _trace_update
