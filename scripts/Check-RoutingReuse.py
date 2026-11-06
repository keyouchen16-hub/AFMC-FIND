"""No network: only bounded public DHT endpoints may cross the owner pipe."""
import importlib.util
import pathlib
import socket

p = pathlib.Path(__file__).resolve().parents[1] / 'src/Afmc.Find.App/Streaming/stream_worker.py'
s = importlib.util.spec_from_file_location('worker', p)
m = importlib.util.module_from_spec(s)
s.loader.exec_module(m)
def node(address, port=6881):
    return (socket.inet_pton(socket.AF_INET6 if ':' in address else socket.AF_INET, address) + port.to_bytes(2, 'big')).hex()
valid = node('8.8.8.8')
assert m.routing_nodes([valid, valid]) == [bytes.fromhex(valid)]
assert len(m.routing_nodes([node('2001:4860:4860::8888')])) == 1
assert m.routing_nodes([node(x) for x in ['127.0.0.1', '10.0.0.1', '192.168.1.1', '169.254.1.1', '224.0.0.1', '::1', 'fe80::1']]) == []
assert m.routing_nodes([node('8.8.8.8', 0), 'zz'*6, None, {}, 'a'*10000]) == []
assert m.routing_nodes([valid]*257) == []
assert m.routing_nodes({'nodes': [valid], 'node-id': 'not allowed'}) == []
print('PASS deduplication, IPv4/IPv6, public addresses only, zero port, malformed/oversized data, bounded count, no arbitrary state')
from types import SimpleNamespace
public = SimpleNamespace(priv=lambda: False)
private = SimpleNamespace(priv=lambda: True)
peers = [SimpleNamespace(total_download=100, ip=('8.8.8.8', 6881)),
         SimpleNamespace(total_download=0, ip=('1.1.1.1', 6881)),
         SimpleNamespace(total_download=1000, ip=('127.0.0.1', 6881))]
assert m.transferred_peers(None, peers) == []
assert m.transferred_peers(private, peers) == []
assert m.transferred_peers(public, peers) == [valid]
print('PASS private/unknown torrents export no peers; only public peers with actual transferred bytes')
