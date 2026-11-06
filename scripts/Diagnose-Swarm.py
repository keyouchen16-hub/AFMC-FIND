"""Bounded diagnostic using the production worker; never records peer addresses."""
import importlib.util, pathlib, sys, time, json, uuid, urllib.parse, collections
import libtorrent as lt
import socket, struct

root = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('worker', root/'src/Afmc.Find.App/Streaming/stream_worker.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
mode = sys.argv[1] if len(sys.argv)>1 else 'baseline'
cache = root/'.tools/swarm-diagnostic'/uuid.uuid4().hex
cache.mkdir(parents=True)
w = module.Worker({'token':'0'*64,'cache':str(cache),'input':('magnet:?xt=urn:btih:2EE672984434179AA8A7C5D2EBD0A94B38EB2960' if 'bare' in mode else str(root/'.tools/comparison.torrent')),'rate':0})
if 'bootstrap' in mode:
    w.session.apply_settings({'enable_dht':False})
    w.session.apply_settings({'dht_bootstrap_nodes':lt.default_settings()['dht_bootstrap_nodes']+',router.bittorrent.com:6881,dht.transmissionbt.com:6881','enable_dht':True})
if mode.startswith('dual'):
    w.session.apply_settings({'listen_interfaces':'0.0.0.0:0,[::]:0'})
state_path=root/'.tools/swarm-diagnostic/dht-state.bin'
if 'warm' in mode and state_path.exists():
    w.session.load_state(lt.bdecode(state_path.read_bytes()))
if 'knownpeers' in mode:
    resume=lt.bdecode(next((root/'.tools/qbit-profile/qBittorrent/data/BT_backup').glob('*.fastresume')).read_bytes())
    assert str(w.handle.info_hashes().v1)=='2ee672984434179aa8a7c5d2ebd0a94b38eb2960'
    compact=resume.get(b'peers',b'')
    for i in range(0,min(len(compact),6*40),6):
        w.handle.connect_peer((socket.inet_ntoa(compact[i:i+4]),struct.unpack('!H',compact[i+4:i+6])[0]))
native = w.session
counts = collections.Counter()
events = []
class Observed:
    def __getattr__(self,k): return getattr(native,k)
    def pop_alerts(self):
        alerts=native.pop_alerts()
        for a in alerts:
            kind=type(a).__name__
            counts[kind]+=1
            if kind in ('tracker_error_alert','tracker_reply_alert','dht_reply_alert','peer_error_alert','peer_disconnected_alert','listen_failed_alert') and len(events)<200:
                e={'kind':kind}
                if hasattr(a,'url'): e['host']=urllib.parse.urlsplit(a.url).hostname
                if hasattr(a,'num_peers'): e['peers']=a.num_peers
                if hasattr(a,'error'):
                    e['error']=a.error.value()
                events.append(e)
        return alerts
w.session=Observed()
start=time.monotonic(); samples=[]; retry_at=20
try:
    while time.monotonic()-start<120:
        w.update()
        if 'retry' in mode and time.monotonic()-start>=retry_at and not w.videos:
            w.handle.force_dht_announce()
            retry_at+=20
        if w.videos and w.selected is None: w.select(max(w.videos,key=lambda x:x['length'])['id'])
        if len(samples)<=int((time.monotonic()-start)/5):
            s={k:w.status[k] for k in ('metadata','nodes','peers','downloaded','verified','rate')}
            s['seconds']=round(time.monotonic()-start,2)
            peers=w.handle.get_peer_info()
            s['ipv6_peers']=sum(':' in p.ip[0] for p in peers)
            samples.append(s)
            report={'mode':mode,'version':lt.version,'samples':samples,'counts':dict(counts),'events':events}
            (root/f'docs/evidence/qbit-comparison/afmc-{mode}.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
        time.sleep(.05)
finally:
    state=native.save_state()
    if b'dht state' in state:
        state_path.write_bytes(lt.bencode({b'dht state':state[b'dht state']}))
    native.pause(); native.remove_torrent(w.handle)
    report={'mode':mode,'version':lt.version,'samples':samples,'counts':dict(counts),'events':events}
    (root/f'docs/evidence/qbit-comparison/afmc-{mode}.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
