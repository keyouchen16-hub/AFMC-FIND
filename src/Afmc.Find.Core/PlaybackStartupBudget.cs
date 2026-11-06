namespace Afmc.Find.Core;

// Real payload progress can extend startup, but peers/metadata alone cannot.
// Times are monotonic seconds supplied by the race's Stopwatch.
public sealed class PlaybackStartupBudget
{
    private readonly Dictionary<int, (long Bytes, double At)> observations = [];
    public double Deadline { get; private set; } = 60;
    public double Observe(int candidate, double elapsed, long received)
    {
        if (!observations.TryGetValue(candidate, out var previous)) {
            observations[candidate] = (received, elapsed);
            return Deadline;
        }
        if (elapsed - previous.At >= 5 && received - previous.Bytes >= 64 * 1024) {
            observations[candidate] = (received, elapsed);
            Deadline = Math.Max(Deadline, Math.Min(120, elapsed + 30));
        }
        return Deadline;
    }
}
