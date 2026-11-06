"""Offline regression for bounded, sequential video read-ahead scheduling.

Uses the production window/select methods with an in-memory torrent handle;
no network sessions, disk payload, or live application state are created.
"""
import importlib.util
import pathlib
import threading
from types import SimpleNamespace

root = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    'afmc_stream_worker', root / 'src/Afmc.Find.App/Streaming/stream_worker.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

MIB = 1024 * 1024
PIECE = 2801664
passed = 0


class FakeFiles:
    def __init__(self, offsets):
        self.offsets = offsets

    def file_offset(self, index):
        return self.offsets[index]


class FakeInfo:
    def __init__(self, size, pieces, offsets):
        self.size, self.pieces = size, pieces
        self.storage = FakeFiles(offsets)

    def piece_length(self):
        return self.size

    def num_pieces(self):
        return self.pieces

    def files(self):
        return self.storage


class FakeHandle:
    def __init__(self, peers):
        self.peers = peers
        self.priorities = None
        self.updates = []
        self.clears = 0
        self.flags = []

    def status(self):
        return SimpleNamespace(num_peers=self.peers)

    def prioritize_pieces(self, priorities):
        self.priorities = list(priorities)
        self.updates.append(self.priorities)

    def clear_piece_deadlines(self):
        self.clears += 1

    def set_piece_deadline(self, *args):
        raise AssertionError('sequential scheduling must not arm urgent deadlines')

    def set_flags(self, flags):
        self.flags.append(('set', flags))

    def unset_flags(self, flags):
        self.flags.append(('unset', flags))


def worker(size=PIECE, pieces=100, offsets=None, length=None, peers=20):
    w = module.Worker.__new__(module.Worker)
    w.handle = FakeHandle(peers)
    w.info = FakeInfo(size, pieces, offsets or {0: 0})
    w.selected = {'id': 0, 'path': 'fixture.mp4',
                  'length': length if length is not None else size * pieces}
    w.videos = [w.selected]
    w.last_window = None
    w.lock = threading.RLock()
    w.changed = threading.Condition(w.lock)
    w.error = None
    w.generation = 0
    return w


def check(condition, message):
    global passed
    assert condition, message
    passed += 1
    print('PASS', message)


w = worker()
w.select(0)
p = w.handle.priorities
check(p[:3] == [7, 6, 1] and not any(p[3:]),
      'initial window covers all three pieces needed by the large MP4 index')
check(w.handle.clears == 1 and len(w.handle.flags) == 2,
      'selection resets stale deadlines and enables sequential transfer')

# The first nonzero read intentionally grows startup prefetch from 8 to 16 MiB.
w.window(128 * 1024, 128 * 1024)
check(w.handle.priorities[:6] == [7, 6, 1, 1, 1, 1]
      and not any(w.handle.priorities[6:]),
      'normal read-ahead is bounded to a rounded 16 MiB window')
updates = len(w.handle.updates)
for offset in (256 * 1024, 512 * 1024, PIECE - 1):
    w.window(offset, 1)
check(len(w.handle.updates) == updates,
      'repeated reads within the same piece do not reprioritize the queue')

w.window(PIECE, 128 * 1024)
check(w.handle.priorities[0] == 0 and w.handle.priorities[1:4] == [7, 6, 1],
      'sequential advance promotes the next blocking piece and drops old work')
w.window(40 * PIECE, 128 * 1024)
check(not any(w.handle.priorities[:40])
      and w.handle.priorities[40:46] == [7, 6, 1, 1, 1, 1]
      and not any(w.handle.priorities[46:]),
      'nonlocal seek replaces irrelevant download priorities')
check(w.handle.clears == 1,
      'reads and seeks never rearm or churn urgent piece deadlines')

w = worker(size=MIB, pieces=20, offsets={0: 2 * MIB + 99}, length=2 * MIB + 200)
w.window(0, 1)
check([i for i, priority in enumerate(w.handle.priorities) if priority] == [2, 3, 4],
      'unaligned selected file permits only its shared boundary pieces')
w.window(w.selected['length'] - 1, 1)
check([i for i, priority in enumerate(w.handle.priorities) if priority] == [4],
      'end-of-file window stays within the selected file')

for peers in (0, 1, 30):
    w = worker(peers=peers)
    w.select(0)
    w.window(PIECE, 1)
    check(w.handle.priorities[1:4] == [7, 6, 1],
          'peer count %d retains sequential priorities without urgent deadlines' % peers)

print('STREAMING SCHEDULE PASSED', passed)
