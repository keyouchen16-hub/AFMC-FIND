"""Policy guards, separate from real wire and exact-magnet playback evidence."""
import pathlib, types

class Worker:
    def __init__(self, *args): pass
    def update(self): pass

scope = {'Worker': Worker, 'lt': types.SimpleNamespace(peer_info=types.SimpleNamespace(remote_choked=1))}
source = pathlib.Path(__file__).with_name('NativePieceRetryCandidate.py').read_text(encoding='utf-8')
exec(compile(source, 'NativePieceRetryCandidate.py', 'exec'), scope)
choose = scope['choose_piece_retry']

def peer(endpoint, speed, has=True, choked=False):
    return types.SimpleNamespace(ip=endpoint, payload_down_speed=speed, pieces=[has], flags=int(choked))

slow = peer(('192.0.2.1', 1), 20)
fast = peer(('192.0.2.2', 2), 100000)
def queue(progress=0, state=1):
    return {'blocks': [{'peer': slow.ip, 'state': state, 'bytes_progress': progress, 'block_size': 16384}]}

_, old = choose(0, 0, queue(), [slow, fast], {})
assert choose(7, 0, queue(), [slow, fast], old)[0] is None
assert choose(9, 0, queue(), [slow, fast], old)[0] == slow.ip
assert choose(9, 0, queue(), [slow], old)[0] is None
assert choose(9, 0, queue(), [slow, peer(fast.ip, 100000, has=False)], old)[0] is None
assert choose(9, 0, queue(), [slow, peer(fast.ip, 100000, choked=True)], old)[0] is None
assert choose(9, 0, queue(), [slow, peer(fast.ip, 16000)], old)[0] is None
assert choose(9, 0, queue(1000), [slow, fast], old)[0] == slow.ip
assert choose(9, 0, queue(12000), [slow, fast], old)[0] is None
assert choose(9, 0, queue(state=2), [slow, fast], old)[0] is None
assert choose(9, 0, queue(state=3), [slow, fast], old)[0] is None
assert choose(9, 0, queue(), [peer(slow.ip, 9000), fast], old)[0] == slow.ip
assert choose(9, 1, queue(), [slow, fast], old)[0] is None
assert choose(9, 0, {}, [slow, fast], old) == (None, {})
assert choose(9,0,queue(),[slow,peer(fast.ip,0)],old,{fast.ip:100000})[0] == slow.ip
assert choose(9,0,queue(),[slow,peer(fast.ip,0)],old,{})[0] is None
assert choose(9,0,queue(16000),[slow,fast],old)[0] is None
print('Native retry policy: 16 guards passed (not playback evidence).')
