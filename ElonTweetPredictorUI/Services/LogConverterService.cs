using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Services;

/// <summary>
/// Background service that watches <c>tweet_predictor.log</c> and converts it
/// into <c>status.json</c> + <c>logs.json</c> for the UI services to consume.
/// </summary>
public sealed partial class LogConverterService : BackgroundService
{
    private readonly string _dataPath;
    private readonly string _logFilePath;

    private readonly string _statusJsonPath;
    private readonly string _logsJsonPath;
    private readonly IDataChangeNotifier _notifier;
    private readonly IProbabilityHistoryService _probabilityHistory;
    private readonly ILogger<LogConverterService> _logger;
    private readonly SemaphoreSlim _convertLock = new(1, 1);

    private const int MaxLogEntries = 2000;

    private static readonly JsonSerializerOptions s_statusJsonOptions = new() { WriteIndented = true };

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}) \[(\w+)\] (\S+) — (.*)$")]
    private static partial Regex LogLineRegex();

    // 🔮 PREDICTION  |  Wed: 38 tweets  →  Bayesian: 144  |  EF=+0.0000 (+0.0%)  →  Adjusted: 144  [95% CI: 131–157]  (obs: 3/7, so far: 104)
    [GeneratedRegex(@"PREDICTION\s+\|\s+\w+:\s+\d+\s+tweets?\s+.*?Bayesian:\s+(\d+)\s+\|\s+EF=([+-]?\d+\.?\d*)\s+\(([+-]?\d+\.?\d*)%\)\s+.*?Adjusted:\s+(\d+)\s+\[95%\s*CI:\s*(\d+).(\d+)\]\s+\(obs:\s+(\d+)/7,\s+so\s+far:\s+(\d+)\)")]
    private static partial Regex PredictionRegex();

    // PREDICTION | +3 tweets | Cumulative: 6,693 | This week: 45 | Predicted: 286 [CI: 120–452] | Bayesian: 286 | EF: +0.0000 (+0.0%) | Pace: 315 | Days: 2 obs / 5 rem
    [GeneratedRegex(@"PREDICTION\s+\|\s+[^|]+\|\s+Cumulative:\s+([\d,]+)\s+\|\s+This week:\s+(\d+)\s+\|\s+Predicted:\s+(\d+)\s+\[CI:\s*(\d+)[^\d](\d+)\]\s+\|\s+Bayesian:\s+(\d+)\s+\|\s+EF:\s+([+-]?\d+\.?\d*)\s+\(([+-]?\d+\.?\d*)%\)\s+\|\s+Pace:\s+(\d+)\s+\|\s+Days:\s+(\d+)\s+obs\s*/\s*(\d+)\s+rem")]
    private static partial Regex PredictionV2Regex();

    // MODEL RETRAINED — actual=378  new prior: mean=288.3  std=239.2  training_weeks=924
    [GeneratedRegex(@"MODEL RETRAINED.*mean=(\d+\.?\d*)\s+std=(\d+\.?\d*)\s+training_weeks=(\d+)")]
    private static partial Regex ModelRetrainedRegex();

    // Cumulative: 6690  or  Cumulative tweets in CSV: 6583
    [GeneratedRegex(@"Cumulative(?:\s+tweets\s+in\s+CSV)?:\s+(\d+)")]
    private static partial Regex CumulativeRegex();

    // Model loaded ← /app/data/bayesian_model.pkl  (EventFactor=0.0000)
    [GeneratedRegex(@"Model loaded.*EventFactor=(\d+\.?\d*)")]
    private static partial Regex ModelLoadedRegex();

    // Day-of-week: Monday    │ ████   12.4%  (851 tweets)
    [GeneratedRegex(@"(\w+day)\s+│.*?(\d+\.?\d*)%\s+\((\d+)\s+tweets?\)")]
    private static partial Regex DayOfWeekRegex();

    // Hourly: 00:00 │ ▪▪▪ 499
    [GeneratedRegex(@"(\d{2}:\d{2})\s+│.*\s(\d+)\s*$")]
    private static partial Regex HourlyRegex();

    // ► Peak hour (EST): 00:00
    [GeneratedRegex(@"Peak hour.*?:\s+(\d{2}:\d{2})")]
    private static partial Regex PeakHourRegex();

    // Gap statistics
    [GeneratedRegex(@"Mean gap\s+:\s+(\d+\.?\d*)\s+hours")]
    private static partial Regex MeanGapRegex();

    [GeneratedRegex(@"Median gap\s+:\s+(\d+\.?\d*)\s+hours")]
    private static partial Regex MedianGapRegex();

    [GeneratedRegex(@"Max gap.*?:\s+(\d+\.?\d*)\s+hours")]
    private static partial Regex MaxGapRegex();

    [GeneratedRegex(@"Std dev.*?:\s+(\d+\.?\d*)\s+hours")]
    private static partial Regex StdDevGapRegex();

    [GeneratedRegex(@"Unusually long.*?:\s+(\d+)\s+occurrences")]
    private static partial Regex UnusuallyLongRegex();

    // Weekly: 2025-W01 │ 291 tweets
    [GeneratedRegex(@"(\d{4}-W\d{2})\s+│\s+(\d+)\s+tweets")]
    private static partial Regex WeeklySummaryRegex();

    // Weekly aggregate: Mean: 311.3  |  Std: 114.9  |  Min: 30  |  Max: 597
    [GeneratedRegex(@"Mean:\s+(\d+\.?\d*)\s+\|\s+Std:\s+(\d+\.?\d*)\s+\|\s+Min:\s+(\d+)\s+\|\s+Max:\s+(\d+)")]
    private static partial Regex WeeklyAggregateRegex();

    // Bet title: 📌 [1/4]  Elon Musk # tweets March 24 - March 31, 2026?
    [GeneratedRegex(@"📌\s+\[\d+/\d+\]\s+(.+)")]
    private static partial Regex BetTitleRegex();

    // Time remaining: ⏳ Time Remaining  : 10d 9h 58m
    [GeneratedRegex(@"Time Remaining\s+:\s+(.+?)(?:\s*$)")]
    private static partial Regex TimeRemainingRegex();

    // Tweets in window: ✅ Tweets in Window: 0
    [GeneratedRegex(@"Tweets in Window:\s+(\d+)")]
    private static partial Regex BetTweetsInWindowRegex();

    // Predicted total: 🔮 Predicted Total : 27  [95% CI: 0 – 116]  σ≈29.6
    [GeneratedRegex(@"Predicted Total\s+:\s+(\d+)\s+\[95%\s*CI:\s*(\d+)\s*[–—-]\s*(\d+)\]\s+σ[≈=](\d+\.?\d*)")]
    private static partial Regex PredictedTotalRegex();

    // Interval row: <20  40.0%  or  20-39  26.4%
    [GeneratedRegex(@"(<?\d+(?:-\d+)?)\s+(\d+\.?\d*)%")]
    private static partial Regex IntervalRowRegex();

    public LogConverterService(
        IConfiguration configuration,
        IDataChangeNotifier notifier,
        IProbabilityHistoryService probabilityHistory,
        ILogger<LogConverterService> logger)
    {
        _dataPath = configuration["DataPath"] ?? ".";
        var cachePath = configuration["CachePath"]
            ?? Path.Combine(Path.GetTempPath(), "predictor-cache");
        Directory.CreateDirectory(cachePath);
        _logFilePath = Path.Combine(_dataPath, "tweet_predictor.log");
        _statusJsonPath = Path.Combine(cachePath, "status.json");
        _logsJsonPath = Path.Combine(cachePath, "logs.json");
        _notifier = notifier;
        _probabilityHistory = probabilityHistory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!Directory.Exists(_dataPath) && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Data path '{DataPath}' does not exist. Retrying in 5 s.", _dataPath);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        if (stoppingToken.IsCancellationRequested)
            return;

        // Initial conversion
        await ConvertAsync();

        using var watcher = new FileSystemWatcher(_dataPath)
        {
            Filter = "tweet_predictor.log",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        using var debounceTimer = new Timer(_ => _ = ConvertAsync(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        void ScheduleConversion(object? s, FileSystemEventArgs e) =>
            debounceTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);

        watcher.Changed += ScheduleConversion;
        watcher.Created += ScheduleConversion;

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ConvertAsync()
    {
        if (!await _convertLock.WaitAsync(0))
            return;

        try
        {
            if (!File.Exists(_logFilePath))
                return;

            string content;
            using (var stream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                content = await reader.ReadToEndAsync();
            }

            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return;

            var logEntries = ParseLogEntries(lines);
            if (logEntries.Count > MaxLogEntries)
                logEntries = logEntries[^MaxLogEntries..];

            var status = BuildStatus(lines);

            // Reuse the already-parsed log entries (which have clean timestamps)
            // to extract both current bet-interval forecasts and the historical
            // snapshots for the probability-delta badges.
            var (currentBets, historicalSnapshots) = ExtractAllBetData(logEntries);
            if (currentBets.Count > 0)
                status.BetIntervalForecasts = currentBets;

            _probabilityHistory.SeedFromLog(historicalSnapshots);

            if (historicalSnapshots.Count > 0)
            {
                _logger.LogDebug(
                    "Probability history: {Count} snapshots spanning {Oldest} → {Newest}",
                    historicalSnapshots.Count,
                    historicalSnapshots[0].Timestamp.ToString("HH:mm:ss"),
                    historicalSnapshots[^1].Timestamp.ToString("HH:mm:ss"));
            }

            await WriteAtomicAsync(_logsJsonPath, () =>
            {
                var sb = new StringBuilder();
                foreach (var entry in logEntries)
                    sb.AppendLine(JsonSerializer.Serialize(entry));
                return sb.ToString();
            });

            await WriteAtomicAsync(_statusJsonPath, () =>
                JsonSerializer.Serialize(status, s_statusJsonOptions));

            _notifier.NotifyChanged();

            _logger.LogDebug(
                "Converted tweet_predictor.log → status.json + logs.json ({Lines} lines, {Entries} entries)",
                lines.Length, logEntries.Count);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read tweet_predictor.log — will retry on next change.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting tweet_predictor.log.");
        }
        finally
        {
            _convertLock.Release();
        }
    }

    private static List<LogEntry> ParseLogEntries(string[] lines)
    {
        var entries = new List<LogEntry>(lines.Length);
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Handle structured JSON log lines (new format from Python predictor)
            if (line.StartsWith('{'))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("message", out var msgProp))
                    {
                        entries.Add(new LogEntry
                        {
                            Timestamp = root.TryGetProperty("timestamp", out var ts) ? ts.GetString() ?? "" : "",
                            Level = root.TryGetProperty("level", out var lvl) ? lvl.GetString() ?? "" : "",
                            Logger = root.TryGetProperty("logger", out var lgr) ? lgr.GetString() ?? "" : "",
                            Message = msgProp.GetString() ?? ""
                        });
                    }
                }
                catch (JsonException) { }
                continue;
            }

            var match = LogLineRegex().Match(line);
            if (!match.Success)
                continue;

            var timestampStr = match.Groups[1].Value;
            if (DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss,fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                timestampStr = dt.ToString("o");
            }

            entries.Add(new LogEntry
            {
                Timestamp = timestampStr,
                Level = match.Groups[2].Value,
                Logger = match.Groups[3].Value,
                Message = match.Groups[4].Value
            });
        }
        return entries;
    }

    private PredictorStatus BuildStatus(string[] lines)
    {
        var status = new PredictorStatus
        {
            State = "running",
            Prediction = new PredictionInfo(),
            Model = new ModelInfo(),
            Files = new FilesInfo
            {
                Log = _logFilePath,
                Model = Path.Combine(_dataPath, "bayesian_model.pkl")
            }
        };

        bool foundPrediction = false, foundModel = false, foundCumulative = false, foundTimestamp = false;

        // Try parsing structured JSON poll_snapshot lines first (newest log format)
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].TrimEnd('\r');
            if (!line.StartsWith('{') || !line.Contains("\"poll_snapshot\""))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "poll_snapshot")
                    continue;

                if (root.TryGetProperty("timestamp", out var ts))
                {
                    status.UpdatedAt = ts.GetString() ?? "";
                    foundTimestamp = true;
                }

                if (root.TryGetProperty("cumulative_count", out var cc))
                {
                    status.CumulativeCount = cc.GetInt32();
                    foundCumulative = true;
                }

                if (root.TryGetProperty("tweets_this_week", out var tw))
                    status.TweetsThisWeek = tw.GetInt32();

                if (root.TryGetProperty("pace_projected", out var pace))
                    status.Prediction.Pace = pace.GetInt32();

                if (root.TryGetProperty("prediction", out var pred))
                {
                    if (pred.TryGetProperty("predicted_weekly_total", out var pwt))
                        status.Prediction.WeeklyTotal = pwt.GetInt32();
                    if (pred.TryGetProperty("bayesian_weekly_total", out var bwt))
                        status.Prediction.BayesianTotal = bwt.GetInt32();
                    if (pred.TryGetProperty("adjusted_ci_95_lower", out var cil))
                        status.Prediction.CiLower = cil.GetInt32();
                    if (pred.TryGetProperty("adjusted_ci_95_upper", out var ciu))
                        status.Prediction.CiUpper = ciu.GetInt32();
                    if (pred.TryGetProperty("event_factor", out var ef))
                        status.Prediction.EventFactor = ef.GetDouble();
                    if (pred.TryGetProperty("event_adjustment_pct", out var eap))
                        status.Prediction.EventAdjustmentPct = eap.GetDouble();
                    if (pred.TryGetProperty("days_observed", out var dobs))
                        status.Prediction.DaysObserved = dobs.GetInt32();
                    if (pred.TryGetProperty("days_remaining", out var drem))
                        status.Prediction.DaysRemaining = drem.GetInt32();
                    if (pred.TryGetProperty("posterior_mean", out var pm))
                        status.Prediction.PosteriorMean = pm.GetDouble();
                    if (pred.TryGetProperty("posterior_std", out var ps))
                        status.Prediction.PosteriorStd = ps.GetDouble();

                    foundPrediction = true;
                }

                if (root.TryGetProperty("active_trackings", out var trackings) && trackings.ValueKind == JsonValueKind.Array)
                {
                    status.ActiveTrackings = [];
                    foreach (var t in trackings.EnumerateArray())
                    {
                        status.ActiveTrackings.Add(new ActiveTracking
                        {
                            Id = t.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            Title = t.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                            Target = t.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.Number ? target.GetInt32() : null,
                            TweetsInWindow = t.TryGetProperty("tweets_in_window", out var tiw) ? tiw.GetInt32() : 0
                        });
                    }
                }

                break;
            }
            catch (JsonException) { }
        }

        // Fall back to text-based parsing for fields not found in JSON
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].TrimEnd('\r');

            if (!foundTimestamp)
            {
                var bracketIdx = line.IndexOf('[');
                if (bracketIdx > 0 && DateTime.TryParseExact(line[..(bracketIdx - 1)].Trim(),
                    "yyyy-MM-dd HH:mm:ss,fff", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var lastDt))
                {
                    status.UpdatedAt = lastDt.ToString("o");
                    foundTimestamp = true;
                }
            }

            if (!foundPrediction && line.Contains("PREDICTION"))
            {
                var matchV2 = PredictionV2Regex().Match(line);
                if (matchV2.Success)
                {
                    status.CumulativeCount = int.Parse(matchV2.Groups[1].Value.Replace(",", ""));
                    status.TweetsThisWeek = int.Parse(matchV2.Groups[2].Value);
                    status.Prediction.WeeklyTotal = int.Parse(matchV2.Groups[3].Value);
                    status.Prediction.CiLower = int.Parse(matchV2.Groups[4].Value);
                    status.Prediction.CiUpper = int.Parse(matchV2.Groups[5].Value);
                    status.Prediction.BayesianTotal = int.Parse(matchV2.Groups[6].Value);
                    status.Prediction.EventFactor = double.Parse(matchV2.Groups[7].Value, CultureInfo.InvariantCulture);
                    status.Prediction.EventAdjustmentPct = double.Parse(matchV2.Groups[8].Value, CultureInfo.InvariantCulture);
                    status.Prediction.Pace = int.Parse(matchV2.Groups[9].Value);
                    status.Prediction.DaysObserved = int.Parse(matchV2.Groups[10].Value);
                    status.Prediction.DaysRemaining = int.Parse(matchV2.Groups[11].Value);
                    foundPrediction = true;
                    foundCumulative = true;
                }
                else
                {
                    var match = PredictionRegex().Match(line);
                    if (match.Success)
                    {
                        status.Prediction.BayesianTotal = int.Parse(match.Groups[1].Value);
                        status.Prediction.EventFactor = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                        status.Prediction.EventAdjustmentPct = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                        status.Prediction.WeeklyTotal = int.Parse(match.Groups[4].Value);
                        status.Prediction.CiLower = int.Parse(match.Groups[5].Value);
                        status.Prediction.CiUpper = int.Parse(match.Groups[6].Value);
                        status.Prediction.DaysObserved = int.Parse(match.Groups[7].Value);
                        status.Prediction.DaysRemaining = 7 - status.Prediction.DaysObserved;
                        status.TweetsThisWeek = int.Parse(match.Groups[8].Value);
                        foundPrediction = true;
                    }
                }
            }

            if (!foundModel && line.Contains("MODEL RETRAINED"))
            {
                var match = ModelRetrainedRegex().Match(line);
                if (match.Success)
                {
                    status.Model.PriorMean = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    status.Model.PriorStd = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    status.Model.TrainingWeeks = int.Parse(match.Groups[3].Value);
                    status.Prediction.PosteriorMean = status.Model.PriorMean;
                    status.Prediction.PosteriorStd = status.Model.PriorStd;
                    foundModel = true;
                }
            }

            if (!foundCumulative && line.Contains("Cumulative"))
            {
                var match = CumulativeRegex().Match(line);
                if (match.Success)
                {
                    status.CumulativeCount = int.Parse(match.Groups[1].Value);
                    foundCumulative = true;
                }
            }

            if (foundPrediction && foundModel && foundCumulative && foundTimestamp)
                break;
        }

        if (!foundModel)
        {
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                if (!lines[i].Contains("Model loaded"))
                    continue;
                var match = ModelLoadedRegex().Match(lines[i]);
                if (match.Success)
                    status.Model.EventFactor = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                break;
            }
        }

        if (string.IsNullOrEmpty(status.Files.Csv) && Directory.Exists(_dataPath))
        {
            var namedCsv = Path.Combine(_dataPath, "elonmusk_tweet_history.csv");
            var csvFile = File.Exists(namedCsv)
                ? namedCsv
                : Directory.EnumerateFiles(_dataPath, "*.csv", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            if (csvFile is not null)
                status.Files.Csv = csvFile;
        }

        status.TemporalPatterns = ParseTemporalPatterns(lines);
        // BetIntervalForecasts is set in ConvertAsync from the parsed log entries

        return status;
    }

    private static TemporalPatterns ParseTemporalPatterns(string[] lines)
    {
        var patterns = new TemporalPatterns();

        // Day-of-Week Distribution — find last occurrence
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!lines[i].Contains("Day-of-Week Distribution"))
                continue;

            for (var j = i + 1; j < lines.Length; j++)
            {
                var line = lines[j].TrimEnd('\r');
                var m = DayOfWeekRegex().Match(line);
                if (m.Success)
                {
                    patterns.DayOfWeek.Add(new DayOfWeekEntry
                    {
                        Day = m.Groups[1].Value,
                        Percentage = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                        Count = int.Parse(m.Groups[3].Value)
                    });
                }
                else if (patterns.DayOfWeek.Count > 0)
                    break;
            }
            break;
        }

        // Hourly Distribution — find last occurrence
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!lines[i].Contains("Hourly Distribution"))
                continue;

            for (var j = i + 1; j < lines.Length; j++)
            {
                var line = lines[j].TrimEnd('\r');

                var peakMatch = PeakHourRegex().Match(line);
                if (peakMatch.Success)
                {
                    patterns.PeakHour = peakMatch.Groups[1].Value;
                    break;
                }

                var m = HourlyRegex().Match(line);
                if (m.Success)
                {
                    patterns.Hourly.Add(new HourlyEntry
                    {
                        Hour = m.Groups[1].Value,
                        Count = int.Parse(m.Groups[2].Value)
                    });
                }
            }
            break;
        }

        // Inactivity Gap Statistics — find last occurrence
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!lines[i].Contains("Inactivity Gap Statistics"))
                continue;

            for (var j = i + 1; j < lines.Length && j < i + 15; j++)
            {
                var line = lines[j].TrimEnd('\r');
                var meanM = MeanGapRegex().Match(line);
                if (meanM.Success) { patterns.InactivityGap.MeanHours = double.Parse(meanM.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
                var medM = MedianGapRegex().Match(line);
                if (medM.Success) { patterns.InactivityGap.MedianHours = double.Parse(medM.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
                var maxM = MaxGapRegex().Match(line);
                if (maxM.Success) { patterns.InactivityGap.MaxHours = double.Parse(maxM.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
                var stdM = StdDevGapRegex().Match(line);
                if (stdM.Success) { patterns.InactivityGap.StdDevHours = double.Parse(stdM.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
                var ulM = UnusuallyLongRegex().Match(line);
                if (ulM.Success) { patterns.InactivityGap.UnusuallyLongCount = int.Parse(ulM.Groups[1].Value); break; }
            }
            break;
        }

        // Weekly Tweet Summary — find last occurrence
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!lines[i].Contains("Weekly Tweet Summary"))
                continue;

            for (var j = i + 1; j < lines.Length; j++)
            {
                var line = lines[j].TrimEnd('\r');

                var aggM = WeeklyAggregateRegex().Match(line);
                if (aggM.Success)
                {
                    patterns.WeeklyMean = double.Parse(aggM.Groups[1].Value, CultureInfo.InvariantCulture);
                    patterns.WeeklyStd = double.Parse(aggM.Groups[2].Value, CultureInfo.InvariantCulture);
                    patterns.WeeklyMin = int.Parse(aggM.Groups[3].Value);
                    patterns.WeeklyMax = int.Parse(aggM.Groups[4].Value);
                    break;
                }

                var wm = WeeklySummaryRegex().Match(line);
                if (wm.Success)
                {
                    patterns.WeeklySummary.Add(new WeeklySummaryEntry
                    {
                        Week = wm.Groups[1].Value,
                        Count = int.Parse(wm.Groups[2].Value)
                    });
                }
            }
            break;
        }

        return patterns;
    }

    /// <summary>
    /// Extract both the current (latest) bet-interval forecasts and all recent
    /// historical snapshots from the already-parsed <see cref="LogEntry"/> list.
    /// Timestamps come straight from the existing parser — no re-parsing needed.
    /// </summary>
    private static (List<BetIntervalForecast> Current, List<HistoricalBetSnapshot> History)
        ExtractAllBetData(List<LogEntry> entries)
    {
        // Find every index where a bet-interval block starts
        var blockStarts = new List<int>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Message.Contains("BET ANSWER INTERVAL PROBABILITIES"))
                blockStarts.Add(i);
        }

        if (blockStarts.Count == 0)
            return ([], []);

        // Build historical snapshots (walk backwards, stop after ~2.5 h)
        var history = new List<HistoricalBetSnapshot>();
        DateTime? latestTs = null;

        for (var b = blockStarts.Count - 1; b >= 0; b--)
        {
            var startIdx = blockStarts[b];
            var endIdx = b < blockStarts.Count - 1 ? blockStarts[b + 1] : entries.Count;

            var ts = ParseEntryTimestamp(entries[startIdx].Timestamp);
            latestTs ??= ts;

            if ((latestTs.Value - ts).TotalHours > 2.5)
                break;

            var forecasts = ParseBetBlockFromEntries(entries, startIdx + 1, endIdx);
            if (forecasts.Count > 0)
                history.Add(new HistoricalBetSnapshot(ts, forecasts));
        }

        history.Reverse(); // chronological order (oldest → newest)

        // The latest block is also the "current" data for the dashboard
        var lastStart = blockStarts[^1];
        var lastEnd = entries.Count;
        var current = ParseBetBlockFromEntries(entries, lastStart + 1, lastEnd);

        return (current, history);
    }

    /// <summary>
    /// Parse a single bet-interval block from log entry messages
    /// between <paramref name="startIdx"/> (inclusive) and
    /// <paramref name="endIdx"/> (exclusive).
    /// </summary>
    private static List<BetIntervalForecast> ParseBetBlockFromEntries(
        List<LogEntry> entries, int startIdx, int endIdx)
    {
        var result = new List<BetIntervalForecast>();
        BetIntervalForecast? current = null;
        var inIntervals = false;

        for (var i = startIdx; i < endIdx; i++)
        {
            var msg = entries[i].Message;

            // End of block — closing ====== or start of another block
            if (msg.Contains("======") && result.Count > 0)
                break;
            if (msg.Contains("BET ANSWER INTERVAL PROBABILITIES"))
                break;

            var titleMatch = BetTitleRegex().Match(msg);
            if (titleMatch.Success)
            {
                current = new BetIntervalForecast { Title = titleMatch.Groups[1].Value.Trim() };
                result.Add(current);
                inIntervals = false;
                continue;
            }

            if (current is null)
                continue;

            var trMatch = TimeRemainingRegex().Match(msg);
            if (trMatch.Success) { current.TimeRemaining = trMatch.Groups[1].Value.Trim(); continue; }

            var twMatch = BetTweetsInWindowRegex().Match(msg);
            if (twMatch.Success) { current.TweetsInWindow = int.Parse(twMatch.Groups[1].Value); continue; }

            var ptMatch = PredictedTotalRegex().Match(msg);
            if (ptMatch.Success)
            {
                current.PredictedTotal = int.Parse(ptMatch.Groups[1].Value);
                current.CiLower = int.Parse(ptMatch.Groups[2].Value);
                current.CiUpper = int.Parse(ptMatch.Groups[3].Value);
                current.Sigma = double.Parse(ptMatch.Groups[4].Value, CultureInfo.InvariantCulture);
                continue;
            }

            if (msg.Contains("───"))
            {
                inIntervals = !inIntervals;
                continue;
            }

            if (inIntervals)
            {
                var intMatch = IntervalRowRegex().Match(msg);
                if (intMatch.Success)
                {
                    current.Intervals.Add(new IntervalProbability
                    {
                        Label = intMatch.Groups[1].Value,
                        Probability = double.Parse(intMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                        IsPredicted = msg.Contains('◄')
                    });
                }
            }
        }

        return result;
    }

    private static DateTime ParseEntryTimestamp(string timestamp)
    {
        if (DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;

        return DateTime.UtcNow;
    }

    private static async Task WriteAtomicAsync(string targetPath, Func<string> generateContent)
    {
        var tempPath = targetPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, generateContent());
        File.Move(tempPath, targetPath, overwrite: true);
    }
}
