# Isolated benchmark injection. Requires AFMC native experiment, never installed implicitly.
_retry_original_init = Worker.__init__
_retry_original_update = Worker.update

def choose_piece_retry(now, target, queue, peers, observations, recent_rates=None):
    current, choices = {}, []
    by_endpoint = {p.ip: p for p in peers}
    for index, block in enumerate(queue.get('blocks', [])):
        endpoint = block.get('peer')
        if int(block['state']) != 1 or endpoint not in by_endpoint:
            continue
        peer = by_endpoint[endpoint]
        key = (target, index, endpoint)
        progress = int(block['bytes_progress'])
        previous = observations.get(key)
        since, initial = previous if previous and previous[1] <= progress else (now, progress)
        current[key] = (since, initial)
        remaining = block['block_size'] - progress
        block_rate = (progress-initial)/max(1, now-since)
        if now-since < 8 or remaining < 512 or block_rate >= 4096 or remaining/max(1,block_rate) <= 4:
            continue
        recent_rates = recent_rates or {}
        faster = any(p.ip != endpoint and max(p.payload_down_speed,recent_rates.get(p.ip,0) if p.download_queue_length == 0 else 0) > max(16384, block_rate*3)
            and not (p.flags & lt.peer_info.remote_choked)
            and target < len(p.pieces) and p.pieces[target] for p in peers)
        if faster:
            choices.append((now-since, endpoint))
    return (max(choices)[1] if choices else None), current

def _retry_init(self, config):
    _retry_original_init(self, config)
    if not hasattr(self.handle, 'afmc_retry_piece'):
        raise RuntimeError('candidate native component missing')
    self._native_retry_next = 0
    self._native_retry_observations = {}
    self._native_retry_context = None
    self._native_retry_history = []
    self._native_retry_count = 0
    self._native_retry_events = 0
    self._native_retry_rates = {}
    self._native_retry_totals = {}

def _retry_update(self):
    _retry_original_update(self)
    now = time.monotonic()
    if self.info is None or self.info.priv() or self.awaiting_piece < 0 or now < self._native_retry_next:
        return
    self._native_retry_next = now + 1
    peers = self.handle.get_peer_info()
    alive = {p.ip for p in peers}
    self._native_retry_rates = {k:[s for s in v if now-s[0] <= 30] for k,v in self._native_retry_rates.items() if k in alive}
    for p in peers:
        previous_total = self._native_retry_totals.get(p.ip,p.total_download)
        if p.total_download < previous_total:
            self._native_retry_rates.pop(p.ip,None)
            self._native_retry_observations = {k:v for k,v in self._native_retry_observations.items() if k[2] != p.ip}
        if p.total_download > self._native_retry_totals.get(p.ip,p.total_download) and p.payload_down_speed > 16384:
            self._native_retry_rates.setdefault(p.ip,[]).append((now,p.payload_down_speed))
    self._native_retry_totals = {p.ip:p.total_download for p in peers}
    rates = {k:max((s[1] for s in v),default=0) for k,v in self._native_retry_rates.items()}
    with self.lock:
        target = self.awaiting_piece
        context = (self.generation, target)
        if context != self._native_retry_context:
            self._native_retry_observations = {}
            self._native_retry_context = context
        if self.handle.have_piece(target):
            self._native_retry_observations = {}
            return
        queue = next((q for q in self.handle.get_download_queue() if q['piece_index'] == target), {})
        endpoint, self._native_retry_observations = choose_piece_retry(now, target, queue,
            peers, self._native_retry_observations, rates)
        self._native_retry_history = [at for at in self._native_retry_history if now-at < 60]
        if endpoint and len(self._native_retry_history) < 8:
            count = self.handle.afmc_retry_piece(target, endpoint, 8000)
            if count:
                self._native_retry_count += count
                self._native_retry_events += 1
                self._native_retry_history.append(now)
                self._native_retry_observations = {}

Worker.__init__ = _retry_init
Worker.update = _retry_update
