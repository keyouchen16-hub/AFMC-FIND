using MonoTorrent;
using MonoTorrent.PiecePicking;

namespace Afmc.Find.App;

// Use the regular request queue with a moving availability window. Do not send bursts of
// cancel/re-request messages when the decoder probes the tail or switches episodes: some
// peers leave those streaming requests pending until the stale-request timeout.
internal sealed class PlaybackPieceRequester : IStreamingPieceRequester
{
    private readonly StandardPieceRequester inner = new(new PieceRequesterSettings(true, false, false, 1));
    private IPieceRequesterData? data;
    private sealed record ReadWindow(int Start, int UrgentEnd, int End);
    private ReadWindow? window;
    public bool InEndgameMode => inner.InEndgameMode;
    public int HighPriorityPieceIndex => Volatile.Read(ref window)?.Start ?? 0;
    public void Initialise(IPieceRequesterData data, IMessageEnqueuer enqueuer, ReadOnlySpan<ReadOnlyBitField> ignored) { this.data = data; inner.Initialise(data, enqueuer, ignored); }
    public void SeekToPosition(ITorrentManagerFile file, long position) => ReadToPosition(file, position);
    public void ReadToPosition(ITorrentManagerFile file, long position)
    {
        if (data is null) return;
        var start = position >= file.Length ? file.EndPieceIndex : data.ByteOffsetToPieceIndex(file.OffsetInTorrent + position);
        var lead = Math.Max(2, (4 * 1024 * 1024 + data.PieceLength - 1) / data.PieceLength);
        Volatile.Write(ref window, new ReadWindow(start, Math.Min(file.EndPieceIndex, start + lead), Math.Min(file.EndPieceIndex, start + lead * 8)));
    }
    private ReadOnlyBitField Available(ReadOnlyBitField original, bool urgent)
    {
        var current = Volatile.Read(ref window);
        return current is null ? original : new BitField(original.Length).SetTrue((current.Start, urgent ? current.UrgentEnd : current.End)).And(original);
    }
    public void AddRequests(ReadOnlySpan<(IRequester Peer, ReadOnlyBitField Available)> peers)
    {
        for (var i = 0; i < peers.Length; i++)
            AddRequests(peers[i].Peer, peers[i].Available, ReadOnlySpan<ReadOnlyBitField>.Empty);
    }
    public void AddRequests(IRequester peer, ReadOnlyBitField available, ReadOnlySpan<ReadOnlyBitField> peers)
    {
        inner.AddRequests(peer, Available(available, true), peers);
        if (peer.CanRequestMorePieces)
            inner.AddRequests(peer, Available(available, false), peers);
    }
    public bool ValidatePiece(IRequester peer, PieceSegment segment, out bool complete, HashSet<IRequester> involved) => inner.ValidatePiece(peer, segment, out complete, involved);
    public bool IsInteresting(IRequester peer, ReadOnlyBitField bitfield) => inner.IsInteresting(peer, bitfield);
    public void CancelRequests(IRequester peer, int start, int end) => inner.CancelRequests(peer, start, end);
    public void RequestRejected(IRequester peer, PieceSegment segment) => inner.RequestRejected(peer, segment);
    public int CurrentRequestCount() => inner.CurrentRequestCount();
}
