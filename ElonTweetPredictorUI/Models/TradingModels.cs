namespace ElonTweetPredictorUI.Models;

public class TradeEntry
{
    // Common fields
    public string Type { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Time { get; set; } = "";
    public string Event { get; set; } = "";
    public string Interval { get; set; } = "";
    public int Shares { get; set; }
    public string OrderId { get; set; } = "";

    // BUY fields
    public decimal Price { get; set; }
    public decimal Cost { get; set; }
    public double EdgePercent { get; set; }
    public double YourPercent { get; set; }
    public double MarketPercent { get; set; }
    public double KellyRawPercent { get; set; }
    public double KellyAdjPercent { get; set; }

    // SELL fields
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal PnLAmount { get; set; }
    public double PnLPercent { get; set; }
    public double GrowthPercent { get; set; }
    public string SellEdgeRaw { get; set; } = "";
    public string SellReason { get; set; } = "";
    public string ExitInfo { get; set; } = "";
    public string Note { get; set; } = "";
    public bool IsSync => Mode.Equals("SYNC", StringComparison.OrdinalIgnoreCase);
    public bool HasPnL => CurrentPrice != 0 || PnLAmount != 0;

    public bool IsBuy => Type.Equals("BUY", StringComparison.OrdinalIgnoreCase);
    public bool IsSell => Type.Equals("SELL", StringComparison.OrdinalIgnoreCase);
    public bool IsExternalClose => Type.Equals("EXTERNAL CLOSE", StringComparison.OrdinalIgnoreCase);
    public bool IsAdjustment => Type.Equals("ADJUSTMENT", StringComparison.OrdinalIgnoreCase);
    public bool IsWin => Outcome.Equals("WIN", StringComparison.OrdinalIgnoreCase);
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
