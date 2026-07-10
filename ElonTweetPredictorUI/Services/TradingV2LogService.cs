using System.Globalization;
using System.Text.RegularExpressions;
using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Services;

public interface ITradingV2LogService
{
    Task<TradingSession?> GetSessionAsync();
}

/// <summary>
/// Parses unified-trades.log written by the v2 (unified strategy) Polymarket bot.
/// Same box-drawing layout as the v1 log, but buys carry a richer Edge line
/// (model → shaded vs market; bar) plus Model/Regime/CountRisk snapshot lines.
/// </summary>
public sealed partial class TradingV2LogService : ITradingV2LogService
{
    private readonly string _logFilePath;

    [GeneratedRegex(@"SESSION STARTED\s*[—–-]\s*(.+)")]
    private static partial Regex SessionStartRegex();

    // Initial header: First run: 2026-07-10 12:07:14 AM ET
    [GeneratedRegex(@"^\s+First run:\s+(.+)")]
    private static partial Regex FirstRunRegex();

    [GeneratedRegex(@"┌── ([^\[]+?)\s*\[([^\]]+)\](?:\s+\((\w+)\))?")]
    private static partial Regex TradeHeaderRegex();

    [GeneratedRegex(@"│\s+Time:\s+(.+)")]
    private static partial Regex TradeTimeRegex();

    [GeneratedRegex(@"│\s+Event:\s+(.+)")]
    private static partial Regex TradeEventRegex();

    [GeneratedRegex(@"│\s+Interval:\s+(.+)")]
    private static partial Regex TradeIntervalRegex();

    [GeneratedRegex(@"│\s+Shares:\s+(\d+)")]
    private static partial Regex TradeSharesRegex();

    [GeneratedRegex(@"│\s+Price:\s+\$?([\d.]+)")]
    private static partial Regex TradePriceRegex();

    [GeneratedRegex(@"│\s+Cost:\s+\$?([\d.]+)")]
    private static partial Regex TradeCostRegex();

    // BUY: Edge: +12.8%  (model=55.2 % → shaded=54.8 % vs market=42.0 %; bar=9.0%)
    // "shaded" and "bar" parts are optional so v1-style lines also parse.
    [GeneratedRegex(@"│\s+Edge:\s+([+-]?[\d.]+)\s*%\s+\(model=([\d.]+)\s*%(?:\s*→\s*shaded=([\d.]+)\s*%)?\s*vs\s+market=([\d.]+)\s*%(?:;\s*bar=([\d.]+)\s*%)?\)")]
    private static partial Regex TradeBuyEdgeRegex();

    // Model:     μ=58 σ=15.3
    [GeneratedRegex(@"│\s+Model:\s+(.+)")]
    private static partial Regex TradeModelRegex();

    // Regime:    pHold=97%
    [GeneratedRegex(@"│\s+Regime:\s+(.+)")]
    private static partial Regex TradeRegimeRegex();

    // CountRisk: pBust=34% | pReach=89%
    [GeneratedRegex(@"│\s+CountRisk:\s+(.+)")]
    private static partial Regex TradeCountRiskRegex();

    [GeneratedRegex(@"│\s+Entry:\s+\$?([\d.]+)")]
    private static partial Regex TradeEntryPriceRegex();

    [GeneratedRegex(@"│\s+Current:\s+\$?([\d.]+)")]
    private static partial Regex TradeCurrentPriceRegex();

    [GeneratedRegex(@"│\s+P&L:\s+\$([+-]?[\d.]+)")]
    private static partial Regex TradePnLRegex();

    [GeneratedRegex(@"│\s+Growth:\s+([+-]?[\d.]+)\s*%")]
    private static partial Regex TradeGrowthRegex();

    // SELL: Edge: -1.5 % | Reason: Edge flip
    [GeneratedRegex(@"│\s+Edge:\s+([+-]?[\d.]+\s*%)\s*\|\s*Reason:\s*(.+)")]
    private static partial Regex TradeSellEdgeRegex();

    [GeneratedRegex(@"│\s+Exit:\s+(.+)")]
    private static partial Regex TradeExitRegex();

    [GeneratedRegex(@"^\$?([\d.]+)")]
    private static partial Regex ExitPriceRegex();

    [GeneratedRegex(@"│\s+Reason:\s+(.+)")]
    private static partial Regex TradeReasonRegex();

    [GeneratedRegex(@"│\s+Note:\s+(.+)")]
    private static partial Regex TradeNoteRegex();

    [GeneratedRegex(@"│\s+OrderId:\s+(0x[0-9a-fA-F]+)")]
    private static partial Regex TradeOrderIdRegex();

    public TradingV2LogService(IConfiguration configuration)
    {
        var dataPath = configuration["DataPathV2"] ?? configuration["DataPath"] ?? ".";
        _logFilePath = Path.Combine(dataPath, "unified-trades.log");
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
        TradeEntry? currentTrade = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Capture the very first "First run:" header
            if (string.IsNullOrEmpty(session.StartedAt))
            {
                var initMatch = FirstRunRegex().Match(line);
                if (initMatch.Success)
                {
                    session.StartedAt = initMatch.Groups[1].Value.Trim();
                    continue;
                }
            }

            // Track session starts (update timestamp but keep accumulating trades)
            var sessionMatch = SessionStartRegex().Match(line);
            if (sessionMatch.Success)
            {
                session.StartedAt = sessionMatch.Groups[1].Value.Trim();
                continue;
            }

            var headerMatch = TradeHeaderRegex().Match(line);
            if (headerMatch.Success)
            {
                currentTrade = new TradeEntry
                {
                    Type = headerMatch.Groups[1].Value.Trim(),
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
                ApplyTradeField(currentTrade, line);
        }

        // Compute summary from trades (the log has no summary lines)
        var closedTrades = session.Trades.Where(t => t.IsSell || t.IsExternalClose).ToList();
        session.LatestSummary.Buys = session.Trades.Count(t => t.IsBuy);
        session.LatestSummary.Sells = closedTrades.Count;
        session.LatestSummary.Wins = closedTrades.Count(t => t.PnLAmount > 0);
        session.LatestSummary.Losses = closedTrades.Count(t => t.PnLAmount < 0);
        var totalClosed = session.LatestSummary.Wins + session.LatestSummary.Losses;
        session.LatestSummary.WinratePercent = totalClosed > 0
            ? (double)session.LatestSummary.Wins / totalClosed * 100
            : 0;
        session.LatestSummary.TotalPnL = closedTrades.Sum(t => t.PnLAmount);

        return session.Trades.Count > 0 || !string.IsNullOrEmpty(session.StartedAt)
            ? session
            : null;
    }

    private static void ApplyTradeField(TradeEntry trade, string line)
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
            if (m.Groups[3].Success)
                trade.ShadedPercent = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            trade.MarketPercent = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (m.Groups[5].Success)
                trade.BarPercent = double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            return;
        }

        m = TradeModelRegex().Match(line);
        if (m.Success) { trade.ModelInfo = m.Groups[1].Value.Trim(); return; }

        m = TradeRegimeRegex().Match(line);
        if (m.Success) { trade.RegimeInfo = m.Groups[1].Value.Trim(); return; }

        m = TradeCountRiskRegex().Match(line);
        if (m.Success) { trade.CountRiskInfo = m.Groups[1].Value.Trim(); return; }

        // SELL-specific fields
        m = TradeEntryPriceRegex().Match(line);
        if (m.Success) { trade.EntryPrice = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); return; }

        m = TradeCurrentPriceRegex().Match(line);
        if (m.Success) { trade.CurrentPrice = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); return; }

        m = TradePnLRegex().Match(line);
        if (m.Success)
        {
            if (decimal.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pnl))
                trade.PnLAmount = pnl;
            return;
        }

        m = TradeGrowthRegex().Match(line);
        if (m.Success)
        {
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var growth))
                trade.GrowthPercent = growth;
            return;
        }

        m = TradeSellEdgeRegex().Match(line);
        if (m.Success)
        {
            trade.SellEdgeRaw = m.Groups[1].Value.Trim();
            trade.SellReason = m.Groups[2].Value.Trim();
            return;
        }

        m = TradeExitRegex().Match(line);
        if (m.Success)
        {
            var exitStr = m.Groups[1].Value.Trim();
            trade.ExitInfo = exitStr;
            var priceMatch = ExitPriceRegex().Match(exitStr);
            if (priceMatch.Success && decimal.TryParse(priceMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var exitPrice))
                trade.CurrentPrice = exitPrice;
            return;
        }

        // Standalone Reason: (must be after TradeSellEdgeRegex)
        m = TradeReasonRegex().Match(line);
        if (m.Success) { trade.SellReason = m.Groups[1].Value.Trim(); return; }

        m = TradeNoteRegex().Match(line);
        if (m.Success) { trade.Note = m.Groups[1].Value.Trim(); return; }

        m = TradeOrderIdRegex().Match(line);
        if (m.Success) { trade.OrderId = m.Groups[1].Value.Trim(); return; }
    }
}
