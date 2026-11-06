"""Apply the isolated AFMC experiment to pinned libtorrent 2.0.15 sources.

Not installed or used by the product until exact-magnet acceptance passes.
"""
from pathlib import Path
import difflib

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / '.tools/native-stream-build/libtorrent-2.0.15'
BACKUP = ROOT / '.tools/native-stream-build/upstream-files'
texts = {}

def edit(name, old, new):
    path = SRC / name
    original = BACKUP / name
    text = texts.setdefault(name, (original if original.exists() else path).read_text(encoding='utf-8'))
    if new in text:
        return
    assert text.count(old) == 1, (name, text.count(old))
    original.parent.mkdir(parents=True, exist_ok=True)
    if not original.exists():
        original.write_text(text, encoding='utf-8', newline='\n')
    texts[name] = text.replace(old, new)

signature = 'int afmc_retry_piece(piece_index_t piece, tcp::endpoint const& peer, int cooldown_ms)'
edit('include/libtorrent/torrent_handle.hpp',
     '\t\tvoid reset_piece_deadline(piece_index_t index) const;',
     '\t\t// AFMC experimental: reassign <=4 outstanding blocks, retain connections.\n'
     '\t\t'+signature+' const;\n\t\tvoid reset_piece_deadline(piece_index_t index) const;')
edit('include/libtorrent/torrent.hpp', '\t\tvoid reset_piece_deadline(piece_index_t piece);',
     '\t\t'+signature+';\n\t\tvoid reset_piece_deadline(piece_index_t piece);')
edit('src/torrent_handle.cpp', '\tvoid torrent_handle::reset_piece_deadline(piece_index_t index) const',
     '''\tint torrent_handle::afmc_retry_piece(piece_index_t piece, tcp::endpoint const& peer, int cooldown_ms) const
\t{
\t\treturn sync_call_ret<int>(0, &torrent::afmc_retry_piece, piece, peer, cooldown_ms);
\t}

\tvoid torrent_handle::reset_piece_deadline(piece_index_t index) const''')
edit('bindings/python/src/torrent_handle.cpp',
     '        .def("reset_piece_deadline", _(&torrent_handle::reset_piece_deadline), (arg("index")))',
     '        .def("afmc_retry_piece", _(&torrent_handle::afmc_retry_piece), (arg("index"), arg("endpoint"), arg("cooldown_ms")=8000))\n'
     '        .def("reset_piece_deadline", _(&torrent_handle::reset_piece_deadline), (arg("index")))')
edit('bindings/python/CMakeLists.txt',
     '\ttarget_compile_options(python-libtorrent PRIVATE /bigobj)',
     '\ttarget_compile_options(python-libtorrent PRIVATE /bigobj /MP4)\n'
     '\ttarget_compile_definitions(python-libtorrent PRIVATE BOOST_PYTHON_STATIC_LIB BOOST_ALL_NO_LIB)')
edit('include/libtorrent/peer_connection.hpp', '\t\tvoid cancel_request(piece_block const& b, bool force = false);',
     '''\t\tvoid cancel_request(piece_block const& b, bool force = false);
\t\t// One bounded, per-connection/piece cooldown. No IP ban or disconnection.
\t\tpiece_index_t afmc_avoided_piece() const
\t\t{ return aux::time_now() < m_afmc_retry_until ? m_afmc_retry_piece : piece_index_t{-1}; }
\t\tvoid afmc_avoid_piece(piece_index_t piece, int ms)
\t\t{ m_afmc_retry_piece = piece; m_afmc_retry_until = aux::time_now() + milliseconds(ms); }
\tprivate:
\t\tpiece_index_t m_afmc_retry_piece{-1};
\t\ttime_point m_afmc_retry_until = min_time();
\tpublic:''')
edit('src/peer_connection.cpp',
     '\tbool peer_connection::add_request(piece_block const& block\n\t\t, request_flags_t const flags)\n\t{',
     '\tbool peer_connection::add_request(piece_block const& block\n\t\t, request_flags_t const flags)\n\t{\n'
     '\t\tif (block.piece_index == afmc_avoided_piece()) return false;')
edit('src/request_blocks.cpp',
     '\t\t// picks the interesting pieces from this peer',
     '''\t\t// Exclude only this peer's temporarily avoided piece before picking,
\t\t// so other wanted pieces can still use the connection.
\t\ttyped_bitfield<piece_index_t> retry_mask;
\t\tauto const avoided = c.afmc_avoided_piece();
\t\tif (avoided >= piece_index_t{0} && avoided < bits->end_index())
\t\t{
\t\t\tretry_mask = *bits;
\t\t\tretry_mask.clear_bit(avoided);
\t\t\tbits = &retry_mask;
\t\t}

\t\t// picks the interesting pieces from this peer''')
edit('src/torrent.cpp', '\tvoid torrent::cancel_non_critical()', '''\tint torrent::afmc_retry_piece(piece_index_t const piece, tcp::endpoint const& endpoint, int cooldown_ms)
\t{
\t\tif (!valid_metadata() || !has_picker() || upload_mode() || is_paused()
\t\t\t|| piece < piece_index_t{0} || piece >= torrent_file().end_piece()
\t\t\t|| have_piece(piece)) return 0;
\t\tpeer_connection* slow = nullptr;
\t\tfor (auto* p : m_connections)
\t\t\tif (p->remote() == endpoint && !p->is_disconnecting()) { slow = p; break; }
\t\tif (!slow || slow->afmc_avoided_piece() == piece) return 0;
\t\tpeer_connection* fast = nullptr;
\t\tint rate = std::max(16 * 1024, slow->statistics().download_payload_rate() * 3);
\t\tfor (auto* p : m_connections)
\t\t{
\t\t\tif (p == slow || !p->can_request_time_critical() || !p->has_piece(piece)
\t\t\t\t|| p->afmc_avoided_piece() == piece) continue;
\t\t\tint const r = p->statistics().download_payload_rate();
\t\t\tif (r > rate) { fast = p; rate = r; }
\t\t}
\t\t// Never withdraw from the only observed supplier.
\t\tif (!fast) return 0;
\t\tauto pending = slow->download_queue();
\t\tint count = 0;
\t\tauto const incoming = slow->downloading_piece_progress();
\t\tfor (auto const& item : pending)
\t\t{
\t\t\tauto const block = item.block;
\t\t\tif (count == 4) break;
\t\t\tif (block.piece_index != piece || item.not_wanted || item.timed_out
\t\t\t\t|| picker().is_downloaded(block) || picker().is_finished(block)
\t\t\t\t|| (incoming.piece_index == piece && incoming.block_index == block.block_index)) continue;
\t\t\tauto const& dq = fast->download_queue();
\t\t\tauto const& rq = fast->request_queue();
\t\t\tif (std::find_if(dq.begin(), dq.end(), aux::has_block(block)) != dq.end()
\t\t\t\t|| std::find_if(rq.begin(), rq.end(), aux::has_block(block)) != rq.end()) continue;
\t\t\tslow->cancel_request(block, true);
\t\t\tif (fast->add_request(block, peer_connection::time_critical)) ++count;
\t\t}
\t\tif (count > 0)
\t\t{
\t\t\tslow->afmc_avoid_piece(piece, std::max(1000, std::min(cooldown_ms, 10000)));
\t\t\tfast->send_block_requests();
\t\t}
\t\treturn count;
\t}

\tvoid torrent::cancel_non_critical()''')

# Keep the original request alive until a valid copy arrives. A vanished backup
# peer must not discard the only remaining supplier's outstanding work.
edit('src/torrent.cpp',
     '\t\t\tslow->cancel_request(block, true);\n\t\t\tif (fast->add_request(block, peer_connection::time_critical)) ++count;',
     '\t\t\tif (fast->add_request(block, peer_connection::time_critical | peer_connection::busy)) ++count;')
edit('include/libtorrent/peer_connection.hpp',
     '\t\tvoid afmc_avoid_piece(piece_index_t piece, int ms)',
     '''\t\tint afmc_retry_rate() const
\t\t{
\t\t\tint const current = m_statistics.download_payload_rate();
\t\t\tif (m_download_queue.empty() && m_request_queue.empty()
\t\t\t\t&& aux::time_now() - m_last_piece.get(m_connect) < seconds(30)
\t\t\t\t&& m_statistics.total_payload_download() >= 64 * 1024)
\t\t\t\treturn std::max(current, m_download_rate_peak);
\t\t\treturn current;
\t\t}
\t\tvoid afmc_avoid_piece(piece_index_t piece, int ms)''')
edit('src/torrent.cpp', '\t\t\tint const r = p->statistics().download_payload_rate();',
     '\t\t\tint const r = p->afmc_retry_rate();')
edit('src/torrent.cpp', '\t\tauto const incoming = slow->downloading_piece_progress();\n', '')
edit('src/torrent.cpp',
     '\t\t\t\t|| picker().is_downloaded(block) || picker().is_finished(block)\n'
     '\t\t\t\t|| (incoming.piece_index == piece && incoming.block_index == block.block_index)) continue;',
     '\t\t\t\t|| picker().is_downloaded(block) || picker().is_finished(block)\n'
     '\t\t\t\t|| picker().num_peers(block) != 1) continue;')
edit('CMakeLists.txt', 'target_compile_options(torrent-rasterbar PUBLIC /EHsc)',
     'target_compile_options(torrent-rasterbar PUBLIC /EHsc /MP4)')

patch = []
for original in sorted(BACKUP.rglob('*')):
    if original.is_file():
        name = original.relative_to(BACKUP).as_posix()
        if name in texts and (SRC / name).read_text(encoding='utf-8') != texts[name]:
            (SRC / name).write_text(texts[name], encoding='utf-8', newline='\n')
        patch.extend(difflib.unified_diff(original.read_text(encoding='utf-8').splitlines(True),
            (SRC / name).read_text(encoding='utf-8').splitlines(True), fromfile='a/'+name, tofile='b/'+name))
output = ROOT/'scripts/native/libtorrent-2.0.15-afmc-piece-retry.patch'
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(''.join(patch), encoding='utf-8')
print('Isolated native experiment patched; product/runtime unchanged.')
