"""Archived rejected experiment only: clock-controlled single-piece rescue checks."""
import collections, importlib.util, pathlib
from unittest.mock import Mock, patch
p = pathlib.Path(__file__).resolve().parents[1] / 'docs/evidence/discovery-0821/candidate3-worker.py'
s = importlib.util.spec_from_file_location('worker', p)
m = importlib.util.module_from_spec(s); s.loader.exec_module(m)

def run(mode):
    w = m.Worker.__new__(m.Worker)
    w.stop = Mock(); w.stop.is_set.return_value = False
    w.error = None; w.generation = 1; w.pieces = collections.OrderedDict(); w.requested = set()
    w.handle = Mock(); w.handle.have_piece.return_value = False; w.rescue_requests = 0
    clock = [0]
    class Condition:
        def __enter__(self): return self
        def __exit__(self, *_): pass
        def wait(self, seconds):
            clock[0] += 1
            if clock[0] < 8: w.handle.set_piece_deadline.assert_not_called()
            if clock[0] == 12:
                if mode == 'complete': w.pieces[2] = b'verified-only'
                elif mode == 'cancel': w.generation = 2
    w.changed = Condition()
    if mode == 'cached': w.pieces[2] = b'verified-only'
    with patch.object(m.time, 'monotonic', side_effect=lambda: clock[0]):
        try:
            value = w.piece(2, 1)
            assert mode in ('complete', 'cached') and value == b'verified-only'
        except InterruptedError: assert mode == 'cancel'
        except TimeoutError: assert mode == 'timeout' and clock[0] == 90
    if mode == 'cached':
        w.handle.set_piece_deadline.assert_not_called(); w.handle.reset_piece_deadline.assert_not_called()
    else:
        w.handle.set_piece_deadline.assert_called_once_with(2, 0)
        w.handle.reset_piece_deadline.assert_called_once_with(2)
        assert w.rescue_requests == 1
for mode in ('complete', 'cancel', 'timeout', 'cached'): run(mode)
print('PASS delayed single-piece rescue, no repeated rearming, cleanup on complete/cancel/timeout, cached bytes stay immediate')
