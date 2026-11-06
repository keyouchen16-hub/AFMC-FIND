"""No-network regression: cold-start recovery is delayed, bounded and stops on metadata."""
import importlib.util, pathlib
from unittest.mock import Mock
p=pathlib.Path(__file__).resolve().parents[1]/'src/Afmc.Find.App/Streaming/stream_worker.py'
s=importlib.util.spec_from_file_location('worker',p); m=importlib.util.module_from_spec(s); s.loader.exec_module(m)
def worker():
    w=m.Worker.__new__(m.Worker); w.handle=Mock(); w.discovery_started=100; w.discovery_retries=0; w.discovery_next=105
    return w
w=worker()
for t in (100,105,119.9): w.refresh_discovery(False,t)
assert w.handle.force_dht_announce.call_count==0
for t in (120,120.1,121,139): w.refresh_discovery(False,t)
assert w.handle.force_dht_announce.call_count==1
for t in (140,160,180,200,500): w.refresh_discovery(False,t)
assert w.handle.force_dht_announce.call_count==4
w=worker(); w.refresh_discovery(False,120)
for t in (140,160,180): w.refresh_discovery(True,t)
assert w.handle.force_dht_announce.call_count==1
print('PASS delayed, spaced, maximum-four recovery; stops after metadata')
w=worker()
w.refresh_discovery(False,104,100)
assert w.handle.force_dht_announce.call_count==0
w.refresh_discovery(False,105,8)
assert w.handle.force_dht_announce.call_count==1
w.refresh_discovery(False,200,8)
for t in (200.1,201,210,219.9): w.refresh_discovery(False,t,8)
assert w.handle.force_dht_announce.call_count==2
w.refresh_discovery(False,220,8)
assert w.handle.force_dht_announce.call_count==3
print('PASS ready routing allows early retry; long scheduler gap never causes catch-up burst')
