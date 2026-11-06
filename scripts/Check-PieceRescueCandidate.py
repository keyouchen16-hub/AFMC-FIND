import ast
import ipaddress
import pathlib
from types import SimpleNamespace as N

tree = ast.parse(pathlib.Path(__file__).with_name('PieceRescueCandidate.py').read_text(encoding='utf-8'))
function = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'choose_rescue')
scope = {'ipaddress': ipaddress, 'lt': N(peer_info=N(remote_choked=2))}
exec(compile(ast.Module(body=[function], type_ignores=[]), '<rescue policy>', 'exec'), scope)
choose = scope['choose_rescue']
slow = N(ip=('1.1.1.1', 1234), payload_down_speed=5, flags=0, pieces=[True])
fast = N(ip=('8.8.8.8', 1234), payload_down_speed=100000, flags=0, pieces=[True])
block = {'state': 1, 'peer': slow.ip, 'block_size': 16384, 'bytes_progress': 0}
queue = {'blocks': [block]}
candidate, observations = choose(0, 0, queue, [slow, fast], {})
assert candidate is None
assert choose(13, 0, queue, [slow, fast], observations)[0] == slow.ip[0]
assert choose(13, 0, queue, [slow], observations)[0] is None
fast.flags = 2
assert choose(13, 0, queue, [slow, fast], observations)[0] is None
fast.flags = 0
fast.pieces = [False]
assert choose(13, 0, queue, [slow, fast], observations)[0] is None
fast.pieces = [True]
slow.payload_down_speed = 5000
assert choose(13, 0, queue, [slow, fast], observations)[0] is None
slow.payload_down_speed = 5
fast.payload_down_speed = 20000
assert choose(13, 0, queue, [slow, fast], observations)[0] == slow.ip[0]
fast.payload_down_speed = 8000
assert choose(13, 0, queue, [slow, fast], observations)[0] is None
fast.payload_down_speed = 100000
block['bytes_progress'] = 10000
assert choose(13, 0, queue, [slow, fast], observations)[0] is None
block['bytes_progress'] = 0
block['state'] = 3
assert choose(13, 0, queue, [slow, fast], observations) == (None, {})
block['state'] = 1
slow.ip = ('10.0.0.1', 1234)
block['peer'] = slow.ip
_, private = choose(0, 0, queue, [slow, fast], {})
assert choose(13, 0, queue, [slow, fast], private)[0] is None
print('PIECE RESCUE POLICY PASSED 11')
