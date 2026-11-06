# Diagnostic injection for an isolated test worker only. No endpoints or tokens.
import os as _discovery_os

_discovery_init = Worker.__init__
_discovery_update = Worker.update

class _DiscoverySession:
    def __init__(self, native):
        self.native = native
        self.counts = collections.Counter()
        self.errors = collections.Counter()
        self.metrics = {}

    def __getattr__(self, name):
        return getattr(self.native, name)

    def pop_alerts(self):
        alerts = self.native.pop_alerts()
        for alert in alerts:
            kind = type(alert).__name__
            self.counts[kind] += 1
            if kind in ('listen_failed_alert', 'dht_error_alert', 'udp_error_alert'):
                try:
                    self.errors[kind + ':' + str(alert.error.value())] += 1
                except AttributeError:
                    self.errors[kind] += 1
            if kind == 'session_stats_alert':
                self.metrics = {k: v for k, v in alert.values.items()
                    if k.startswith('dht.') or k.startswith('utp.') or k.startswith('net.')}
        return alerts

def _discovery_constructor(self, config):
    _discovery_init(self, config)
    self.session = _DiscoverySession(self.session)
    self._discovery_trace_started = time.monotonic()
    self._discovery_trace_next = 0

def _discovery_tick(self):
    _discovery_update(self)
    now = time.monotonic()
    output = _discovery_os.environ.get('AFMC_DISCOVERY_TRACE')
    if not output or now < self._discovery_trace_next:
        return
    self._discovery_trace_next = now + 2
    self.session.post_session_stats()
    elapsed = now - self._discovery_trace_started
    if elapsed > 600:
        return
    row = {'seconds': elapsed, 'nodes': self.status.get('nodes'),
        'metadata': self.status.get('metadata'), 'peers': self.status.get('peers'),
        'alerts': dict(self.session.counts), 'errors': dict(self.session.errors),
        'metrics': self.session.metrics}
    with open(output, 'a', encoding='utf-8') as file:
        file.write(json.dumps(row, separators=(',', ':')) + '\n')

Worker.__init__ = _discovery_constructor
Worker.update = _discovery_tick
