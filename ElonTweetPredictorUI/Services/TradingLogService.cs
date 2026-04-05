using System.Globalization;
using System.Text.RegularExpressions;
using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Services;

public interface ITradingLogService
{
    Task<TradingSession?> GetSessionAsync();
}

public sealed partial class TradingLogService : ITradingLogService
{
    private readonly string _logFilePath;

    [GeneratedRegex(@"NEW SESSION STARTED\s*[—–-]\s*(.+)")]
    private static partial Regex SessionStartRegex();

    // BUY [LIVE] or SELL [LIVE] (WIN) or SELL [LIVE] (LOSS)
    [GeneratedRegex(@"┌── (\w+) \[(\w+)\](?:\s+\((\w+)\))?")]
    private static partial Regex TradeHeaderRegex();

    [GeneratedRegex(@"│\s+Time:\s+(.+)")]
    private static partial Regex TradeTimeRegex();

    [GeneratedRegex(@"│\s+Event:\s+(.+)")]
    private static partial Regex TradeEventRegex();

    [GeneratedRegex(@"│\s+Interval:\s+(.+)")]
    private static partial Regex TradeIntervalRegex();

    [GeneratedRegex(@"│\s+Shares:\s+(\d+)")]
    private static partial Regex TradeSharesRegex();

    // BUY: Price
    [GeneratedRegex(@"│\s+Price:\s+\$?([\d.]+)")]
    private static partial Regex TradePriceRegex();

    // BUY: Cost
    [GeneratedRegex(@"│\s+Cost:\s+\$?([\d.]+)")]
    private static partial Regex TradeCostRegex();

    // BUY: Edge: 18.5 % (yours=22.9 % vs market=4.5 %)
    [GeneratedRegex(@"│\s+Edge:\s+([\d.]+)\s*%\s*\(yours=([\d.]+)\s*%\s*vs\s+market=([\d.]+)\s*%\)")]
    private static partial Regex TradeBuyEdgeRegex();

    // BUY: Kelly
    [GeneratedRegex(@"│\s+Kelly:\s+raw=([\d.]+)\s*%,?\s*adj=([\d.]+)\s*%")]
    private static partial Regex TradeKellyRegex();

    // SELL: Entry price
    [GeneratedRegex(@"│\s+Entry:\s+\$?([\d.]+)")]
    private static partial Regex TradeEntryPriceRegex();

    // SELL: Current price
    [GeneratedRegex(@"│\s+Current:\s+\$?([\d.]+)")]
    private static partial Regex TradeCurrentPriceRegex();

    // SELL: P&L: +$0.02 (+6.3 %)
    [GeneratedRegex(@"│\s+P&L:\s+([+-]?\$?[\d.]+)\s+\(([+-]?[\d.]+)\s*%\)")]
    private static partial Regex TradePnLRegex();

    // SELL: Edge: -1.5 % | Reason: Edge flip
    [GeneratedRegex(@"│\s+Edge:\s+([+-]?[\d.]+\s*%)\s*\|\s*Reason:\s*(.+)")]
    private static partial Regex TradeSellEdgeRegex();

    [GeneratedRegex(@"│\s+OrderId:\s+(0x[0-9a-fA-F]+)")]
    private static partial Regex TradeOrderIdRegex();

    [GeneratedRegex(@"Buys:\s+(\d+)")]
    private static partial Regex SummaryBuysRegex();

    [GeneratedRegex(@"Sells:\s+(\d+)\s+\(Wins:\s+(\d+)\s+\|\s+Losses:\s+(\d+)\)")]
    private static partial Regex SummarySellsRegex();

    [GeneratedRegex(@"Winrate:\s+([\d.]+)\s*%")]
    private static partial Regex SummaryWinrateRegex();

    [GeneratedRegex(@"Total P&L:\s+([+-]?\$?[\d.]+)")]
    private static partial Regex SummaryPnlRegex();

    public TradingLogService(IConfiguration configuration)
    {
        var dataPath = configuration["DataPath"] ?? ".";
        _logFilePath = Path.Combine(dataPath, "trades.log");
    }

    public async Task<TradingSession?> GetSessionAsync()
    {
        if (!File.Exists(_logFilePath))
            return null;

        string content;
        try
        {
            using var stream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            content = await reader.ReadToEndAsync();
        }
        catch (IOException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(content))
            return null;

        var lines = content.Split('\n');

        var session = new TradingSession();
        string? lastSessionStart = null;

        // Find the last session start
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var sessionMatch = SessionStartRegex().Match(line);
            if (sessionMatch.Success)
            {
                lastSessionStart = sessionMatch.Groups[1].Value.Trim();
            }
        }

        session.StartedAt = lastSessionStart ?? "";

        // Parse from the last session start
        var parsingActive = lastSessionStart is null; // if no session header, parse everything
        TradeEntry? currentTrade = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            var sessionMatch = SessionStartRegex().Match(line);
            if (sessionMatch.Success)
            {
                var ts = sessionMatch.Groups[1].Value.Trim();
                if (ts == lastSessionStart || lastSessionStart is null)
                {
                    parsingActive = true;
                    session.Trades.Clear();
                    session.StartedAt = ts;
                }
                else
                {
                    parsingActive = false;
                }
                continue;
            }

            if (!parsingActive)
                continue;

            var headerMatch = TradeHeaderRegex().Match(line);
            if (headerMatch.Success)
            {
                currentTrade = new TradeEntry
                {
                    Type = headerMatch.Groups[1].Value,
                    Mode = headerMatch.Groups[2].Value,
                    Outcome = headerMatch.Groups[3].Value
                };
                continue;
            }

            if (line.Contains('└'))
            {
                if (currentTrade is not null)
                {
                    session.Trades.Add(currentTrade);
                    currentTrade = null;
                }
                continue;
            }

            if (currentTrade is not null)
            {
                ApplyTradeField(currentTrade, line);
                continue;
            }

            // Summary lines
            ApplySummaryField(session.LatestSummary, line);
        }

        return session.Trades.Count > 0 || !string.IsNullOrEmpty(session.StartedAt)
            ? session
            : null;
    }

    private void ApplyTradeField(TradeEntry trade, string line)
    {
        var m = TradeTimeRegex().Match(line);
        if (m.Success) { trade.Time = m.Groups[1].Value.Trim(); return; }

        m = TradeEventRegex().Match(line);
        if (m.Success) { trade.Event = m.Groups[1].Value.Trim(); return; }

        m = TradeIntervalRegex().Match(line);
        if (m.Success) { trade.Interval = m.Groups[1].Value.Trim(); return; }

        m = TradeSharesRegex().Match(line);
        if (m.Success) { trade.Shares = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); return; }

        // BUY-specific fields
        m = TradePriceRegex().Match(line);
        if (m.Success) { trade.Price = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); return; }

        m = TradeCostRegex().Match(line);
        if (m.Success) { trade.Cost = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); return; }

        m = TradeBuyEdgeRegex().Match(line);
        if (m.Success)
        {
            trade.EdgePercent = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            trade.YourPercent = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            trade.MarketPercent = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            return;
        }

        m = TradeKellyRegex().Match(line);
        if (m.Success)
        {
            trade.KellyRawPercent = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            trade.KellyAdjPercent = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return;
        }

        // SELL-specific fields
        m = TradeEntryPriceRegex().Match(line);
        if (m.Success) { trade.EntryPrice = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); return; }

        m = TradeCurrentPriceRegex().Match(line);
        if (m.Success) { trade.CurrentPrice = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); return; }

        m = TradePnLRegex().Match(line);
        if (m.Success)
        {
            var raw = m.Groups[1].Value.Replace("$", "").Trim();
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var pnl))
                trade.PnLAmount = pnl;
            if (double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pnlPct))
                trade.PnLPercent = pnlPct;
            return;
        }

        m = TradeSellEdgeRegex().Match(line);
        if (m.Success)
        {
            trade.SellEdgeRaw = m.Groups[1].Value.Trim();
            trade.SellReason = m.Groups[2].Value.Trim();
            return;
        }

        m = TradeOrderIdRegex().Match(line);
        if (m.Success) { trade.OrderId = m.Groups[1].Value.Trim(); return; }
    }

    private void ApplySummaryField(TradeSummary summary, string line)
    {
        var m = SummaryBuysRegex().Match(line);
        if (m.Success) { summary.Buys = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); return; }

        m = SummarySellsRegex().Match(line);
        if (m.Success)
        {
            summary.Sells = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            summary.Wins = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            summary.Losses = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            return;
        }

        m = SummaryWinrateRegex().Match(line);
        if (m.Success) { summary.WinratePercent = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); return; }

        m = SummaryPnlRegex().Match(line);
        if (m.Success)
        {
            var raw = m.Groups[1].Value.Replace("$", "").Trim();
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var pnl))
                summary.TotalPnL = pnl;
        }
    }
}
