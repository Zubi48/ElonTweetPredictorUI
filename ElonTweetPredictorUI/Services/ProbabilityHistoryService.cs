using System.Collections.Concurrent;
using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Services;

public interface IProbabilityHistoryService
{
    void RecordSnapshot(List<BetIntervalForecast> forecasts);
    ProbabilityDeltas GetDeltas(string betTitle, string intervalLabel);
}

/// <summary>
/// Tracks probability values over time so the UI can show how much each
/// interval probability has changed in the last 5 min, 30 min, and 1 hour.
/// </summary>
public sealed class ProbabilityHistoryService : IProbabilityHistoryService
{
    private static readonly TimeSpan MaxRetention = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, List<ProbabilitySnapshot>> _history = new();
    private readonly object _lock = new();

    public void RecordSnapshot(List<BetIntervalForecast> forecasts)
    {
        var now = DateTime.UtcNow;

        foreach (var bet in forecasts)
        {
            foreach (var interval in bet.Intervals)
            {
                var key = BuildKey(bet.Title, interval.Label);
                var snapshot = new ProbabilitySnapshot(now, interval.Probability);

                lock (_lock)
                {
                    var list = _history.GetOrAdd(key, _ => new List<ProbabilitySnapshot>());

                    // Only add if the timestamp differs from the last entry by ≥1 s
                    // (avoids duplicates from rapid re-renders).
                    if (list.Count == 0 || (now - list[^1].Timestamp).TotalSeconds >= 1)
                    {
                        list.Add(snapshot);
                    }
                    else
                    {
                        // Update the latest entry's value
                        list[^1] = snapshot;
                    }

                    // Prune entries older than MaxRetention
                    var cutoff = now - MaxRetention;
                    list.RemoveAll(s => s.Timestamp < cutoff);
                }
            }
        }
    }

    public ProbabilityDeltas GetDeltas(string betTitle, string intervalLabel)
    {
        var key = BuildKey(betTitle, intervalLabel);

        lock (_lock)
        {
            if (!_history.TryGetValue(key, out var list) || list.Count == 0)
            {
                return ProbabilityDeltas.Empty;
            }

            var current = list[^1].Probability;
            var now = list[^1].Timestamp;

            return new ProbabilityDeltas
            {
                Delta5Min = ComputeDelta(list, current, now, TimeSpan.FromMinutes(5)),
                Delta30Min = ComputeDelta(list, current, now, TimeSpan.FromMinutes(30)),
                Delta1Hr = ComputeDelta(list, current, now, TimeSpan.FromHours(1))
            };
        }
    }

    private static double? ComputeDelta(List<ProbabilitySnapshot> list, double current, DateTime now, TimeSpan lookback)
    {
        var targetTime = now - lookback;

        // Find the entry closest to (but not after) the target time.
        ProbabilitySnapshot? best = null;
        foreach (var s in list)
        {
            if (s.Timestamp <= targetTime)
            {
                best = s;
            }
        }

        // If we don't have data going back far enough, return null.
        if (best is null)
        {
            return null;
        }

        return Math.Round(current - best.Probability, 2);
    }

    private static string BuildKey(string betTitle, string intervalLabel) =>
        $"{betTitle}||{intervalLabel}";

    private sealed record ProbabilitySnapshot(DateTime Timestamp, double Probability);
}

public class ProbabilityDeltas
{
    public double? Delta5Min { get; init; }
    public double? Delta30Min { get; init; }
    public double? Delta1Hr { get; init; }

    public static ProbabilityDeltas Empty { get; } = new();
}

