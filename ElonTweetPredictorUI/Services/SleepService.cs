using System.Globalization;
using System.Text.RegularExpressions;
using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Services;

public interface ISleepService
{
    Task<SleepData?> GetSleepDataAsync();
}

public partial class SleepService : ISleepService
{
    private readonly string _filePath;

    public SleepService(IConfiguration configuration)
    {
        var dataPath = configuration["DataPath"] ?? "/app/data";
        _filePath = Path.Combine(dataPath, "final_output_v2.txt");
    }

    // Strips an optional log line prefix: "2026-05-02 17:14:09,818 [INFO]   " -> rest.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2},\d+\s+\[\w+\]\s*(.*)$", RegexOptions.Compiled)]
    private static partial Regex LogPrefixRegex();

    // Per-weekday block header: "  += MONDAY (5 nights) ===..."
    [GeneratedRegex(@"\+=\s*([A-Z]+)\s*\((\d+)\s+nights?\)", RegexOptions.Compiled)]
    private static partial Regex WeekdayHeaderRegex();

    // "  | Avg bedtime : 02:28 AM EST | Avg wake-up : 11:31 AM EST"
    [GeneratedRegex(@"Avg bedtime\s*:\s*(\d{1,2}:\d{2}\s*[AP]M).*Avg wake-up\s*:\s*(\d{1,2}:\d{2}\s*[AP]M)", RegexOptions.Compiled)]
    private static partial Regex WeekdayAvgRegex();

    // "  | Sleep duration: avg=6.5h min=3.8h max=8.0h"
    [GeneratedRegex(@"Sleep duration:\s*avg=([\d.]+)h\s+min=([\d.]+)h\s+max=([\d.]+)h", RegexOptions.Compiled)]
    private static partial Regex SleepDurationRegex();

    // Confirmation-threshold weekday header: "  Monday:" (alone on its line)
    [GeneratedRegex(@"^\s*([A-Z][a-z]+):\s*$", RegexOptions.Compiled)]
    private static partial Regex ThresholdWeekdayRegex();

    // Threshold data row: "  11:00 PM         3h45m     4h00m     4h30m"
    [GeneratedRegex(@"^\s*(\d{1,2}:\d{2}\s*[AP]M)\s+(\S+)\s+(\S+)\s+(\S+)\s*$", RegexOptions.Compiled)]
    private static partial Regex ThresholdRowRegex();

    // ── Current sleep-state estimate fields ──
    [GeneratedRegex(@"Now \(EST\)\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex NowRegex();

    [GeneratedRegex(@"Last tweet \(EST\)\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex LastTweetRegex();

    [GeneratedRegex(@"Silence so far\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex SilenceRegex();

    [GeneratedRegex(@"Clock regime\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex RegimeRegex();

    // Fixes "01:00 PM" -> "01:00 AM" for post-midnight hours (12 AM – 5 AM) that the
    // Python script sometimes stamps as PM due to a 12-hour clock rollover bug.
    [GeneratedRegex(@"\b(0?[1-5])(:(\d{2})) PM\b", RegexOptions.Compiled)]
    private static partial Regex PostMidnightPmFixRegex();

    [GeneratedRegex(@"Activity \(24h, dur\.\)\s*:\s*.*tier\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex ActivityTierRegex();

    // "P(ASLEEP - in a night-rest gap) = 76.4%   (95% band: 59.1% - 90.1%)"
    [GeneratedRegex(@"P\(ASLEEP[^)]*\)\s*=\s*([\d.]+)%.*?band:\s*([\d.]+)%\s*-\s*([\d.]+)%", RegexOptions.Compiled)]
    private static partial Regex AsleepRegex();

    // "P(no more tweets until 05:00 AM) = 56.0%   (95% band: 37.4% - 73.6%)"
    [GeneratedRegex(@"P\(no more tweets until[^)]*\)\s*=\s*([\d.]+)%.*?band:\s*([\d.]+)%\s*-\s*([\d.]+)%", RegexOptions.Compiled)]
    private static partial Regex NoTweets5Regex();

    [GeneratedRegex(@"^\s*Median\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex MedianRegex();

    [GeneratedRegex(@"50% interval\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex Interval50Regex();

    [GeneratedRegex(@"90th percentile\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex Pct90Regex();

    [GeneratedRegex(@"If he tweets again before the morning\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex BranchTweetsRegex();

    [GeneratedRegex(@"If the silence lasts until the morning\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex BranchSilentRegex();

    [GeneratedRegex(@"Probability-weighted mean\s*:\s*(.+)", RegexOptions.Compiled)]
    private static partial Regex BranchWeightedRegex();

    public async Task<SleepData?> GetSleepDataAsync()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var text = await File.ReadAllTextAsync(_filePath);
            return Parse(text);
        }
        catch
        {
            return null;
        }
    }

    private enum Section { None, WeekdayLaunch, WeekdayNonLaunch, Thresholds, Estimate }

    private static SleepData Parse(string text)
    {
        var data = new SleepData { ParsedAt = DateTime.UtcNow };
        var lines = text.Split('\n');

        var section = Section.None;
        WeekdaySleepSummary? curWeekday = null;
        var estimate = new CurrentSleepEstimate();
        var hasEstimate = false;
        string thresholdWeekday = "";

        foreach (var raw in lines)
        {
            var rawLine = raw.TrimEnd('\r');
            var prefixMatch = LogPrefixRegex().Match(rawLine);
            var line = prefixMatch.Success ? prefixMatch.Groups[1].Value : rawLine;

            // ── Section switches (banner lines) ──
            if (line.Contains("PER-WEEKDAY SLEEP SUMMARY [LAUNCH DAYS]"))
            {
                FlushWeekday(data, ref curWeekday);
                section = Section.WeekdayLaunch;
                continue;
            }
            if (line.Contains("PER-WEEKDAY SLEEP SUMMARY [NON-LAUNCH DAYS]"))
            {
                FlushWeekday(data, ref curWeekday);
                section = Section.WeekdayNonLaunch;
                continue;
            }
            if (line.Contains("OPTIMAL DOWN-FOR-THE-NIGHT CONFIRMATION THRESHOLDS"))
            {
                FlushWeekday(data, ref curWeekday);
                section = Section.Thresholds;
                continue;
            }
            if (line.Contains("CURRENT SLEEP-STATE ESTIMATE"))
            {
                FlushWeekday(data, ref curWeekday);
                section = Section.Estimate;
                continue;
            }
            // Sections we intentionally drop from the UI.
            if (line.Contains("SLEEP-STATE INFERENCE") || line.Contains("ACTIVITY COVARIATE"))
            {
                FlushWeekday(data, ref curWeekday);
                section = Section.None;
                continue;
            }

            switch (section)
            {
                case Section.WeekdayLaunch:
                case Section.WeekdayNonLaunch:
                    ParseWeekdayLine(line, section == Section.WeekdayLaunch, data, ref curWeekday);
                    break;

                case Section.Thresholds:
                    ParseThresholdLine(line, ref thresholdWeekday, data);
                    break;

                case Section.Estimate:
                    hasEstimate |= ParseEstimateLine(line, estimate);
                    break;
            }
        }

        FlushWeekday(data, ref curWeekday);
        if (hasEstimate)
            data.CurrentEstimate = estimate;

        return data;
    }

    private static void FlushWeekday(SleepData data, ref WeekdaySleepSummary? cur)
    {
        if (cur is not null)
        {
            data.WeekdaySummaries.Add(cur);
            cur = null;
        }
    }

    private static void ParseWeekdayLine(string line, bool isLaunch, SleepData data, ref WeekdaySleepSummary? cur)
    {
        var header = WeekdayHeaderRegex().Match(line);
        if (header.Success)
        {
            FlushWeekday(data, ref cur);
            cur = new WeekdaySleepSummary
            {
                Weekday = Capitalize(header.Groups[1].Value),
                IsLaunch = isLaunch,
                Nights = int.Parse(header.Groups[2].Value)
            };
            return;
        }

        if (cur is null) return;

        var avg = WeekdayAvgRegex().Match(line);
        if (avg.Success)
        {
            cur.AvgBedtime = avg.Groups[1].Value.Trim();
            cur.AvgWakeUp = avg.Groups[2].Value.Trim();
            return;
        }

        var dur = SleepDurationRegex().Match(line);
        if (dur.Success)
        {
            cur.AvgSleepHours = ParseD(dur.Groups[1].Value);
            cur.MinSleepHours = ParseD(dur.Groups[2].Value);
            cur.MaxSleepHours = ParseD(dur.Groups[3].Value);
        }
    }

    private static void ParseThresholdLine(string line, ref string weekday, SleepData data)
    {
        var wd = ThresholdWeekdayRegex().Match(line);
        if (wd.Success)
        {
            weekday = wd.Groups[1].Value;
            return;
        }

        if (string.IsNullOrEmpty(weekday)) return;
        if (line.Contains(">=80%")) return; // column header row

        var row = ThresholdRowRegex().Match(line);
        if (row.Success)
        {
            data.Thresholds.Add(new ConfirmationThreshold
            {
                Weekday = weekday,
                LastTweet = row.Groups[1].Value.Trim(),
                Target80 = row.Groups[2].Value.Trim(),
                Target90 = row.Groups[3].Value.Trim(),
                Target95 = row.Groups[4].Value.Trim()
            });
        }
    }

    private static bool ParseEstimateLine(string line, CurrentSleepEstimate e)
    {
        if (TryMatch(NowRegex(), line, out var v))           { e.NowEst = v; return true; }
        if (TryMatch(LastTweetRegex(), line, out v))          { e.LastTweetEst = v; return true; }
        if (TryMatch(SilenceRegex(), line, out v))            { e.SilenceSoFar = v; return true; }
        if (TryMatch(RegimeRegex(), line, out v))             { e.ClockRegime = FixPostMidnightPm(v); return true; }
        if (TryMatch(ActivityTierRegex(), line, out v))       { e.ActivityTier = v; return true; }
        if (TryMatch(MedianRegex(), line, out v))             { e.NextTweetMedian = v; return true; }
        if (TryMatch(Interval50Regex(), line, out v))         { e.NextTweet50Interval = v; return true; }
        if (TryMatch(Pct90Regex(), line, out v))              { e.NextTweet90Pct = v; return true; }
        if (TryMatch(BranchTweetsRegex(), line, out v))       { e.BranchIfTweetsAgain = v; return true; }
        if (TryMatch(BranchSilentRegex(), line, out v))       { e.BranchIfSilentTillMorning = v; return true; }
        if (TryMatch(BranchWeightedRegex(), line, out v))     { e.BranchWeightedMean = v; return true; }

        var asleep = AsleepRegex().Match(line);
        if (asleep.Success)
        {
            e.AsleepProbability = ParseD(asleep.Groups[1].Value);
            e.AsleepLow = ParseD(asleep.Groups[2].Value);
            e.AsleepHigh = ParseD(asleep.Groups[3].Value);
            return true;
        }

        var no5 = NoTweets5Regex().Match(line);
        if (no5.Success)
        {
            e.NoTweetsUntil5Probability = ParseD(no5.Groups[1].Value);
            e.NoTweets5Low = ParseD(no5.Groups[2].Value);
            e.NoTweets5High = ParseD(no5.Groups[3].Value);
            return true;
        }

        return false;
    }

    private static bool TryMatch(Regex regex, string line, out string value)
    {
        var m = regex.Match(line);
        value = m.Success ? m.Groups[1].Value.Trim() : "";
        return m.Success;
    }

    // Corrects "01:00 PM" -> "01:00 AM" for post-midnight hours (1–5 AM) that the
    // Python script sometimes labels as PM due to a 12-hour clock rollover bug.
    private static string FixPostMidnightPm(string s) =>
        PostMidnightPmFixRegex().Replace(s, m => $"{m.Groups[1].Value}{m.Groups[2].Value} AM");

    private static double ParseD(string s) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLowerInvariant();
}
