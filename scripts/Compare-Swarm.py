"""Bounded diagnostic only; not part of the installed player."""
import gc, json, pathlib, shutil, time, uuid, sys
import libtorrent as lt

workspace = pathlib.Path(__file__).resolve().parent.parent
evidence = workspace / 'docs/evidence/v0810'
before = json.loads((evidence / 'spiderman-before.json').read_text(encoding='utf-8-sig'))['Checks']
english = json.loads((evidence / 'spiderman-english.json').read_text(encoding='utf-8-sig'))['Checks']
selected = []
for rows, term in [(before, '崭新'), (before, '2002'), (english, 'No Way Home')]:
    found = next((r for r in rows if term in r['Title'] and r.get('Hash')), None)
    if found and found['Hash'] not in [r['Hash'] for r in selected]: selected.append(found)
cache_base = (workspace / '.tools/swarm-compare-cache').resolve()
cache_base.mkdir(parents=True, exist_ok=True)
cache = (cache_base / uuid.uuid4().hex).resolve()
assert cache.parent == cache_base and cache.is_relative_to(workspace)
cache.mkdir()
tcp_only = '--tcp-only' in sys.argv
session = lt.session({'enable_upnp': False, 'enable_natpmp': False, 'enable_lsd': False,
    'enable_incoming_utp':not tcp_only,'enable_outgoing_utp':not tcp_only,
    'listen_interfaces': '0.0.0.0:0', 'download_rate_limit': 1024*1024, 'upload_rate_limit': 64*1024,
    'dht_bootstrap_nodes': 'router.bittorrent.com:6881,dht.transmissionbt.com:6881'})
handles = []
records = []
with_control = '--with-control' in sys.argv
with_trackers = '--public-trackers' in sys.argv
# Explicit diagnostic opt-in for these three known public index hashes only.
# Snapshot checked against ngosang/trackerslist/trackers_best.txt on 2026-11-05.
public_trackers = ['udp://open.stealth.si:80/announce',
    'udp://tracker.opentrackr.org:1337/announce',
    'udp://tracker.torrent.eu.org:451/announce',
    'udp://open.demonii.com:1337/announce',
    'udp://exodus.desync.com:6969/announce',
    'udp://open.tracker.cl:1337/announce']
try:
    for row in selected:
        infohash = row['Hash'].removeprefix('btih:')
        assert len(infohash) == 40 and all(c in '0123456789abcdefABCDEF' for c in infohash)
        params = lt.parse_magnet_uri('magnet:?xt=urn:btih:' + infohash)
        if with_trackers: params.trackers = public_trackers
        params.save_path = str(cache / infohash)
        params.flags &= ~lt.torrent_flags.paused
        params.flags &= ~lt.torrent_flags.auto_managed
        handles.append(session.add_torrent(params))
    if with_control:
        control = lt.torrent_info(str(workspace / '.tools/public-playback-control/sintel.torrent'))
        params = lt.add_torrent_params()
        params.ti = control
        params.save_path = str(cache / 'public-control')
        params.flags &= ~lt.torrent_flags.paused
        params.flags &= ~lt.torrent_flags.auto_managed
        handles.append(session.add_torrent(params))
        selected.append({'Hash':str(control.info_hashes().v1),'Title':'Official public Sintel control (.torrent with original trackers)'})
    start = time.monotonic()
    while time.monotonic() - start < 120:
        snapshot=[]
        for row, handle in zip(selected, handles):
            status=handle.status()
            snapshot.append({'Hash':row['Hash'],'Title':row['Title'],'Metadata':status.has_metadata,
                'Peers':status.num_peers,'Seeds':status.num_seeds,'Bytes':status.total_done,'Rate':status.download_rate})
        records.append({'Seconds':round(time.monotonic()-start,1),
            'DhtNodes':getattr(session.status(),'dht_nodes',None),'Resources':snapshot})
        print(json.dumps(records[-1],ensure_ascii=False),flush=True)
        if sum(r['Bytes'] for r in snapshot)>16*1024*1024: break
        time.sleep(5)
finally:
    session.pause()
    for handle in handles: session.remove_torrent(handle)
    handles.clear()
    del session
    gc.collect()
    report={'Engine':lt.version,'Mode':'same public infohash, DHT plus six public trackers' if with_trackers else 'same infohash, DHT discovery, no added trackers; not a playback proof','Snapshots':records}
    report['PublicControlIncluded'] = with_control
    report['TcpOnly'] = tcp_only
    filename = 'independent-control.json' if with_control else 'independent-engine.json'
    output = evidence / 'followup' if with_trackers else evidence
    output.mkdir(parents=True,exist_ok=True)
    if with_trackers: filename = 'public-tracker-comparison.json'
    if tcp_only: filename = 'tcp-only-comparison.json'
    (output/filename).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    for attempt in range(4):
        try:
            assert cache.resolve().parent==cache_base and cache.resolve().is_relative_to(workspace)
            shutil.rmtree(cache)
            break
        except PermissionError:
            time.sleep(1)
    print('CACHE_REMOVED='+str(not cache.exists()),flush=True)
