using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Services;

public interface IProbabilityHistoryService
{
    /// <summary>
    /// Replace all history with timestamped snapshots parsed from the log file.
    /// Called by <see cref="LogConverterService"/> on every conversion.
    /// </summary>
    void SeedFromLog(List<HistoricalBetSnapshot> snapshots);

    /// <summary>
    /// Replace Hawkes history with timestamped snapshots from tweet_predictor_v7.log.
    /// Stored under a separate namespace so it never collides with main-log data.
    /// </summary>
    void SeedHawkesFromLog(List<HistoricalBetSnapshot> snapshots);

    ProbabilityDeltas GetDeltas(string betTitle, string intervalLabel);
    ProbabilityDeltas GetHawkesDeltas(string betTitle, string intervalLabel);
}

/// <summary>A timestamped set of bet interval forecasts extracted from the log.</summary>
public sealed record HistoricalBetSnapshot(DateTime Timestamp, List<BetIntervalForecast> Forecasts);

/// <summary>
/// Tracks probability values over time so the UI can show how much each
/// interval probability has changed in the last 5 min, 30 min, and 1 hour.
///
/// Data is seeded entirely from the <c>tweet_predictor.log</c> file by
/// <see cref="LogConverterService"/>, so it survives container restarts and
/// is available immediately on startup.
/// </summary>
public sealed class ProbabilityHistoryService : IProbabilityHistoryService
{
    private static readonly TimeSpan MaxRetention = TimeSpan.FromHours(2);

    // key = "betTitle||intervalLabel"  →  chronologically sorted snapshots
    private readonly Dictionary<string, List<ProbabilitySnapshot>> _history = new();
    private readonly object _lock = new();

    public void SeedFromLog(List<HistoricalBetSnapshot> snapshots) =>
        SeedInternal(snapshots, prefix: null);

    public void SeedHawkesFromLog(List<HistoricalBetSnapshot> snapshots) =>
        SeedInternal(snapshots, prefix: "hawkes");

    private void SeedInternal(List<HistoricalBetSnapshot> snapshots, string? prefix)
    {
        lock (_lock)
        {
            // Remove only the keys that belong to this namespace
            var keysToRemove = _history.Keys
                .Where(k => prefix is null ? !k.StartsWith("hawkes||") : k.StartsWith($"{prefix}||"))
                .ToList();
            foreach (var k in keysToRemove)
                _history.Remove(k);

            foreach (var snapshot in snapshots)
            {
                foreach (var bet in snapshot.Forecasts)
                {
                    foreach (var interval in bet.Intervals)
                    {
                        var key = BuildKey(bet.Title, interval.Label, prefix);

                        if (!_history.TryGetValue(key, out var list))
                        {
                            list = [];
                            _history[key] = list;
                        }

                        list.Add(new ProbabilitySnapshot(snapshot.Timestamp, interval.Probability));
                    }
                }
            }

            // Ensure chronological order and prune
            foreach (var kvp in _history)
            {
                kvp.Value.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

                if (kvp.Value.Count > 0)
                {
                    var cutoff = kvp.Value[^1].Timestamp - MaxRetention;
                    kvp.Value.RemoveAll(s => s.Timestamp < cutoff);
                }
            }
        }
    }

    public ProbabilityDeltas GetDeltas(string betTitle, string intervalLabel) =>
        GetDeltasInternal(BuildKey(betTitle, intervalLabel, prefix: null));

    public ProbabilityDeltas GetHawkesDeltas(string betTitle, string intervalLabel) =>
        GetDeltasInternal(BuildKey(betTitle, intervalLabel, prefix: "hawkes"));

    private ProbabilityDeltas GetDeltasInternal(string key)
    {
        lock (_lock)
        {
            if (!_history.TryGetValue(key, out var list) || list.Count < 2)
            {
                return ProbabilityDeltas.Empty;
            }

            var current = list[^1].Probability;
            var now = list[^1].Timestamp;

            return new ProbabilityDeltas
            {
                Delta5Min  = ComputeDelta(list, current, now, TimeSpan.FromMinutes(5)),
                Delta30Min = ComputeDelta(list, current, now, TimeSpan.FromMinutes(30)),
                Delta1Hr   = ComputeDelta(list, current, now, TimeSpan.FromHours(1))
            };
        }
    }

    private static double? ComputeDelta(
        List<ProbabilitySnapshot> list, double current, DateTime now, TimeSpan lookback)
    {
        var targetTime = now - lookback;

        // Find the entry closest to (but not after) the target time.
        ProbabilitySnapshot? best = null;
        foreach (var s in list)
        {
            if (s.Timestamp <= targetTime)
                best = s;
        }

        if (best is null)
            return null;

        return Math.Round(current - best.Probability, 2);
    }

    private static string BuildKey(string betTitle, string intervalLabel, string? prefix) =>
        prefix is null ? $"{betTitle}||{intervalLabel}" : $"{prefix}||{betTitle}||{intervalLabel}";

    private sealed record ProbabilitySnapshot(DateTime Timestamp, double Probability);
}

public class ProbabilityDeltas
{
    public double? Delta5Min  { get; init; }
    public double? Delta30Min { get; init; }
    public double? Delta1Hr   { get; init; }

    public static ProbabilityDeltas Empty { get; } = new();
}
