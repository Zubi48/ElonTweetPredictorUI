namespace ElonTweetPredictorUI.Services;

/// <summary>
/// Resolves the per-tweet history CSV (one row per tweet) that feeds the
/// tweet-activity heatmap. The v7 pipeline writes it under the
/// <c>tweetpredictorv7</c> sub-directory; older builds wrote it at the data-dir
/// root under a couple of legacy names.
///
/// We deliberately never fall back to "the newest CSV in the directory": poll
/// logs such as <c>next_tweet_monitor_log_v4.csv</c> have one row per polling
/// sample, so counting their rows per hour yields the polling cadence
/// (~constant around the clock) instead of real tweet volume.
/// </summary>
public static class TweetHistoryCsv
{
    // Highest-priority first.
    private static readonly string[] RelativeCandidates =
    [
        Path.Combine("tweetpredictorv7", "elonmusk_tweet_history_v7.csv"),
        "elonmusk_tweet_history.csv",
        "elonmusk_tweet_history_improved.csv",
    ];

    /// <summary>
    /// Returns the first existing per-tweet history CSV under <paramref name="dataPath"/>,
    /// or null when none of the known files are present.
    /// </summary>
    public static string? Resolve(string dataPath)
    {
        foreach (var relative in RelativeCandidates)
        {
            var candidate = Path.Combine(dataPath, relative);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
