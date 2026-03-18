namespace ElonTweetPredictorUI.Services;

public record BetResult(
    double OurProbYesPct,
    double MarketProbYesPct,
    double EdgePct,
    double FracKellyPct,
    double BetDollars,
    string Side,
    string Tier,
    string TierEmoji
);

public static class KellyCalculator
{
    /// <summary>
    /// Fractional Kelly Criterion bet sizing (port of Python calculate_optimal_bet).
    /// </summary>
    public static BetResult? Calculate(
        int    ciLower,
        int    ciUpper,
        int    target,
        double marketYesPrice,
        double kellyFraction = 0.25,
        double bankroll      = 100.0)
    {
        if (ciUpper <= ciLower)
            return null;

        var meanPred = (ciLower + ciUpper) / 2.0;
        var sigma    = Math.Max((ciUpper - ciLower) / 3.92, 1.0);

        // P(tweets > target) using normal approximation
        var ourProbYes = 1.0 - NormalCdf(target, meanPred, sigma);
        ourProbYes = Math.Clamp(ourProbYes, 0.02, 0.98);

        var marketProb = Math.Clamp(marketYesPrice, 0.01, 0.99);
        var edge       = ourProbYes - marketProb;

        var b         = (1.0 - marketProb) / marketProb;
        var fullKelly = (b * ourProbYes - (1.0 - ourProbYes)) / b;
        var fracKelly = Math.Clamp(kellyFraction * fullKelly, 0.0, 0.30);
        var betDollars = Math.Round(fracKelly * bankroll, 2);

        string side;
        if (edge >= 0.04)
        {
            side = "BUY YES";
        }
        else if (edge <= -0.04)
        {
            side      = "BUY NO";
            fracKelly  = Math.Clamp(kellyFraction * Math.Abs(fullKelly), 0.0, 0.30);
            betDollars = Math.Round(fracKelly * bankroll, 2);
        }
        else
        {
            side = "SKIP";
        }

        var absEdge = Math.Abs(edge);
        string tier, emoji;
        if      (absEdge >= 0.20) { tier = "STRONG edge";    emoji = "✅"; }
        else if (absEdge >= 0.10) { tier = "MODERATE edge";  emoji = "⚠️"; }
        else if (absEdge >= 0.04) { tier = "WEAK edge";      emoji = "🔸"; }
        else                      { tier = "NO edge — skip"; emoji = "❌"; }

        return new BetResult(
            OurProbYesPct   : Math.Round(ourProbYes  * 100, 1),
            MarketProbYesPct: Math.Round(marketProb   * 100, 1),
            EdgePct         : Math.Round(edge         * 100, 1),
            FracKellyPct    : Math.Round(fracKelly    * 100, 1),
            BetDollars      : betDollars,
            Side            : side,
            Tier            : tier,
            TierEmoji       : emoji
        );
    }

    // P(X ≤ x) where X ~ N(mu, sigma)
    private static double NormalCdf(double x, double mu, double sigma)
    {
        var z = (x - mu) / (sigma * Math.Sqrt(2.0));
        return 0.5 * (1.0 + Erf(z));
    }

    // Abramowitz & Stegun approximation (max error ≈ 1.5e-7)
    private static double Erf(double x)
    {
        const double p  = 0.3275911;
        var t  = 1.0 / (1.0 + p * Math.Abs(x));
        var t2 = t * t; var t3 = t2 * t; var t4 = t3 * t; var t5 = t4 * t;
        var poly = t  *  0.254829592
                 + t2 * -0.284496736
                 + t3 *  1.421413741
                 + t4 * -1.453152027
                 + t5 *  1.061405429;
        var result = 1.0 - poly * Math.Exp(-(x * x));
        return x >= 0 ? result : -result;
    }
}
