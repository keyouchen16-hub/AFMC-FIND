# Experimental injection only. Owns an otherwise empty, isolated session filter.
# Do not put this into production without real acceptance and filter composition.
_rescue_original_init = Worker.__init__
_rescue_original_update = Worker.update

def choose_rescue(now, target, queue, peers, observations):
    current = {}
    choices = []
    by_endpoint = {p.ip: p for p in peers}
    for index, block in enumerate(queue.get('blocks', [])):
        endpoint = block.get('peer')
        if int(block['state']) != 1 or endpoint not in by_endpoint:
            continue
        peer = by_endpoint[endpoint]
        key = (target, index, endpoint)
        since = observations.get(key, now)
        current[key] = since
        remaining = block['block_size'] - block['bytes_progress']
        speed = max(1, peer.payload_down_speed)
        # Rescue only old, materially incomplete requests with slow suppliers.
        if now-since < 12 or remaining < 8192 or speed >= 4096 or remaining/speed <= 12:
            continue
        try:
            if not ipaddress.ip_address(endpoint[0]).is_global:
                continue
        except ValueError:
            continue
        faster = any(p.ip[0] != endpoint[0]
            and p.payload_down_speed >= 16384
            and not (p.flags & lt.peer_info.remote_choked)
            and target < len(p.pieces) and p.pieces[target] for p in peers)
        if faster:
            choices.append((now-since, endpoint[0]))
    return (max(choices)[1] if choices else None), current

def _rescue_init(self, config):
    _rescue_original_init(self, config)
    self._rescue_next = 0
    self._rescue_observations = {}
    self._rescue_banned = {}
    self._rescue_history = []
    self._rescue_count = 0

def _rescue_filter(self):
    filtered = lt.ip_filter()
    for address in self._rescue_banned:
        filtered.add_rule(address, address, 1)
    self.session.set_ip_filter(filtered)

def _rescue_update(self):
    _rescue_original_update(self)
    now = time.monotonic()
    expired = [ip for ip, until in self._rescue_banned.items() if now >= until]
    for ip in expired:
        del self._rescue_banned[ip]
    if expired:
        _rescue_filter(self)
    if self.info is None or self.info.priv() or self.awaiting_piece < 0 or now < self._rescue_next:
        return
    self._rescue_next = now + 1
    target = self.awaiting_piece
    queue = next((q for q in self.handle.get_download_queue() if q['piece_index'] == target), {})
    candidate, self._rescue_observations = choose_rescue(now, target, queue,
        self.handle.get_peer_info(), self._rescue_observations)
    self._rescue_history = [at for at in self._rescue_history if now-at < 60]
    if (candidate and len(self._rescue_history) < 4
            and (not self._rescue_history or now-self._rescue_history[-1] >= 12)):
        self._rescue_banned[candidate] = now+5
        _rescue_filter(self)
        self._rescue_history.append(now)
        self._rescue_count += 1
    # Anonymous count only, never log endpoint addresses.
    self.status['diagnostic'] += ' rescue=%s' % self._rescue_count

Worker.__init__ = _rescue_init
Worker.update = _rescue_update
