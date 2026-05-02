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
        _filePath = Path.Combine(dataPath, "sleep_wake.log");
    }

    // Matches a FULL SLEEP PERIOD LOG data row:
    // e.g.  "  2025-11-02   Sunday      02:39 AM EST 08:46 AM EST     6.1        2         1"
    [GeneratedRegex(
        @"^\s*(\d{4}-\d{2}-\d{2})\s+(\w+)\s+(\d{2}:\d{2}\s+[AP]M\s+EST)\s+(\d{2}:\d{2}\s+[AP]M\s+EST)\s+([\d.]+)\s+(\d+)\s+(\d+)",
        RegexOptions.Compiled)]
    private static partial Regex SleepRowRegex();

    // Matches week summary header line:
    // e.g.  "  Week 2026-W17  (6 nights: Monday, Tuesday, ...)"
    [GeneratedRegex(@"Week\s+(\d{4}-W\d+)\s+\((\d+) nights?: ([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex WeekHeaderRegex();

    // Matches avg lines like "    Avg bedtime  : 02:41 AM EST"
    [GeneratedRegex(@"Avg\s+(bedtime|wake-up|sleep)\s*:\s+(.+)", RegexOptions.Compiled)]
    private static partial Regex WeekAvgRegex();

    // Matches total count
    [GeneratedRegex(@"Total sleep periods detected\s*:\s*(\d+)", RegexOptions.Compiled)]
    private static partial Regex TotalRegex();

    // Matches extreme lines
    [GeneratedRegex(@"(Latest bedtime|Earliest bedtime|Earliest wake-up|Latest wake-up|Longest sleep|Shortest sleep)\s*:\s+(.+)", RegexOptions.Compiled)]
    private static partial Regex ExtremeRegex();

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

    private static SleepData Parse(string text)
    {
        var data = new SleepData { ParsedAt = DateTime.UtcNow };
        var lines = text.Split('\n');

        WeekSummary? currentWeek = null;
        int avgLineIndex = 0; // 0=bedtime,1=wakeup,2=sleep within a week block

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');

            // Total sleep periods
            var totalMatch = TotalRegex().Match(line);
            if (totalMatch.Success)
            {
                data.TotalSleepPeriods = int.Parse(totalMatch.Groups[1].Value);
                continue;
            }

            // Sleep period row
            var rowMatch = SleepRowRegex().Match(line);
            if (rowMatch.Success)
            {
                var period = new SleepPeriod
                {
                    Date = DateOnly.Parse(rowMatch.Groups[1].Value),
                    Weekday = rowMatch.Groups[2].Value,
                    BedtimeStr = rowMatch.Groups[3].Value.Trim(),
                    WakeTimeStr = rowMatch.Groups[4].Value.Trim(),
                    DurationHours = double.Parse(rowMatch.Groups[5].Value, CultureInfo.InvariantCulture),
                    BedTweets = int.Parse(rowMatch.Groups[6].Value),
                    MornTweets = int.Parse(rowMatch.Groups[7].Value),
                    Bedtime = ParseTime12(rowMatch.Groups[3].Value.Trim()),
                    WakeTime = ParseTime12(rowMatch.Groups[4].Value.Trim()),
                };
                data.Periods.Add(period);
                continue;
            }

            // Week header
            var weekMatch = WeekHeaderRegex().Match(line);
            if (weekMatch.Success)
            {
                if (currentWeek is not null)
                    data.WeekSummaries.Add(currentWeek);

                currentWeek = new WeekSummary
                {
                    WeekLabel = weekMatch.Groups[1].Value,
                    Nights = int.Parse(weekMatch.Groups[2].Value),
                    Weekdays = weekMatch.Groups[3].Value.Trim()
                };
                avgLineIndex = 0;
                continue;
            }

            // Week avg lines (bedtime / wake-up / sleep)
            if (currentWeek is not null)
            {
                var avgMatch = WeekAvgRegex().Match(line);
                if (avgMatch.Success)
                {
                    var kind = avgMatch.Groups[1].Value;
                    var val = avgMatch.Groups[2].Value.Trim();
                    switch (kind)
                    {
                        case "bedtime": currentWeek.AvgBedtime = val; break;
                        case "wake-up": currentWeek.AvgWakeUp = val; break;
                        case "sleep":
                            if (double.TryParse(val.Split(' ')[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var hrs))
                                currentWeek.AvgSleepHours = hrs;
                            break;
                    }
                }
            }

            // Extremes
            var extMatch = ExtremeRegex().Match(line);
            if (extMatch.Success)
            {
                var kind = extMatch.Groups[1].Value;
                var val = extMatch.Groups[2].Value.Trim();
                switch (kind)
                {
                    case "Latest bedtime": data.Extremes.LatestBedtime = val; break;
                    case "Earliest bedtime": data.Extremes.EarliestBedtime = val; break;
                    case "Earliest wake-up": data.Extremes.EarliestWakeUp = val; break;
                    case "Latest wake-up": data.Extremes.LatestWakeUp = val; break;
                    case "Longest sleep": data.Extremes.LongestSleep = val; break;
                    case "Shortest sleep": data.Extremes.ShortestSleep = val; break;
                }
            }
        }

        // Flush last week
        if (currentWeek is not null)
            data.WeekSummaries.Add(currentWeek);

        return data;
    }

    /// <summary>Parse "02:39 AM EST" or "11:59 PM EST" into TimeOnly.</summary>
    private static TimeOnly ParseTime12(string s)
    {
        // Remove EST suffix
        var clean = s.Replace("EST", "").Trim();
        if (TimeOnly.TryParseExact(clean, "hh:mm tt", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return t;
        return TimeOnly.MinValue;
    }
}
