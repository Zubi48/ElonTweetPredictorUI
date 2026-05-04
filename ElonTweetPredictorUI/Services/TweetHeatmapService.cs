using System.Globalization;
using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Services;

public interface ITweetHeatmapService
{
    Task<HeatmapData?> GetHeatmapAsync(int days = 7);
}

public class TweetHeatmapService : ITweetHeatmapService
{
    private readonly string _dataPath;
    private readonly IStatusService _statusService;

    // EST = UTC-5 (no DST — consistent with how the rest of the app labels times)
    private static readonly TimeZoneInfo Est =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public TweetHeatmapService(IConfiguration configuration, IStatusService statusService)
    {
        _dataPath = configuration["DataPath"] ?? "/app/data";
        _statusService = statusService;
    }

    public async Task<HeatmapData?> GetHeatmapAsync(int days = 7)
    {
        var csvPath = await ResolveCsvPathAsync();
        if (csvPath is null || !File.Exists(csvPath))
            return null;

        var estNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, Est);
        var today = DateOnly.FromDateTime(estNow);
        var currentHour = estNow.Hour;

        // Build day columns for the last `days` days (inclusive of today)
        var columns = Enumerable.Range(0, days)
            .Select(i =>
            {
                var d = today.AddDays(-(days - 1 - i));
                return new HeatmapDayColumn
                {
                    Date = d,
                    DayLabel = d.ToString("ddd"),
                    DayNumber = d.Day,
                    IsToday = d == today
                };
            })
            .ToList();

        var dateIndex = columns.ToDictionary(c => c.Date);

        // All-time hourly totals for AVG column
        var allTimeHour = new long[24];
        var allTimeDays = new HashSet<DateOnly>();

        // Parse CSV — we only need the timestamp column
        // Expected format: first column is timestamp in UTC or similar ISO-8601
        // We'll auto-detect the column index by header
        await using var stream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        var header = await reader.ReadLineAsync();
        if (header is null) return null;

        var headers = header.Split(',');
        // Find column named "created_at", "timestamp", "date", "time" (case-insensitive)
        int tsCol = Array.FindIndex(headers, h =>
            h.Trim('"').Trim().ToLowerInvariant() is "created_at" or "timestamp" or "date" or "time");
        if (tsCol < 0) tsCol = 0; // fallback to first column

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = SplitCsvLine(line);
            if (fields.Length <= tsCol) continue;

            var raw = fields[tsCol].Trim('"').Trim();
            if (!TryParseTimestamp(raw, out var utcDt)) continue;

            var estDt = TimeZoneInfo.ConvertTime(utcDt, Est);
            var date = DateOnly.FromDateTime(estDt);
            var hour = estDt.Hour;

            allTimeHour[hour]++;
            allTimeDays.Add(date);

            if (dateIndex.TryGetValue(date, out var col))
                col.HourCounts[hour]++;
        }

        // Compute averages
        var totalDays = Math.Max(1, allTimeDays.Count);
        var hourlyAvg = allTimeHour.Select(c => (double)c / totalDays).ToArray();

        var maxCount = columns.SelectMany(c => c.HourCounts).DefaultIfEmpty(0).Max();

        return new HeatmapData
        {
            Days = columns,
            HourlyAvg = hourlyAvg,
            MaxCount = Math.Max(1, maxCount),
            CurrentHour = currentHour,
            SleepingCells = new HashSet<(DateOnly, int)>() // filled by caller if SleepData available
        };
    }

    private async Task<string?> ResolveCsvPathAsync()
    {
        // Try status.json path first
        var status = await _statusService.GetStatusAsync();
        if (status?.Files?.Csv is { Length: > 0 } csvRelative)
        {
            var candidate = Path.IsPathRooted(csvRelative)
                ? csvRelative
                : Path.Combine(_dataPath, csvRelative);
            if (File.Exists(candidate)) return candidate;
        }

        // Fallback: first CSV in data directory
        if (Directory.Exists(_dataPath))
        {
            return Directory.EnumerateFiles(_dataPath, "*.csv", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        return null;
    }

    private static bool TryParseTimestamp(string raw, out DateTime utcDt)
    {
        utcDt = default;
        if (string.IsNullOrEmpty(raw)) return false;

        // Try standard formats
        string[] formats =
        [
            "yyyy-MM-dd HH:mm:ss+00:00",
            "yyyy-MM-dd HH:mm:sszzz",
            "yyyy-MM-dd HH:mm:ss UTC",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss",
            "ddd MMM dd HH:mm:ss +0000 yyyy", // Twitter API format
        ];

        // Remove trailing UTC label for simple parse
        var clean = raw.Replace(" UTC", "").Trim();

        if (DateTime.TryParseExact(clean, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utcDt))
            return true;

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utcDt))
            return true;

        return false;
    }

    /// <summary>Minimal CSV splitter that handles quoted fields.</summary>
    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var inQuote = false;
        var current = new System.Text.StringBuilder();
        foreach (var ch in line)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (ch == ',' && !inQuote) { result.Add(current.ToString()); current.Clear(); continue; }
            current.Append(ch);
        }
        result.Add(current.ToString());
        return [.. result];
    }
}
