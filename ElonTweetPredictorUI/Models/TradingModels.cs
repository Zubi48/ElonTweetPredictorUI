namespace ElonTweetPredictorUI.Models;

public class TradeEntry
{
    public string Type { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Time { get; set; } = "";
    public string Event { get; set; } = "";
    public string Interval { get; set; } = "";
    public int Shares { get; set; }
    public decimal Price { get; set; }
    public decimal Cost { get; set; }
    public double EdgePercent { get; set; }
    public double YourPercent { get; set; }
    public double MarketPercent { get; set; }
    public double KellyRawPercent { get; set; }
    public double KellyAdjPercent { get; set; }
    public string OrderId { get; set; } = "";
}

public class TradeSummary
{
    public int Buys { get; set; }
    public int Sells { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinratePercent { get; set; }
    public decimal TotalPnL { get; set; }
}

public class TradingSession
{
    public string StartedAt { get; set; } = "";
    public List<TradeEntry> Trades { get; set; } = [];
    public TradeSummary LatestSummary { get; set; } = new();
}
