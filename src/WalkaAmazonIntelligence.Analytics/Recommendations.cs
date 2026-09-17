using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Analytics;

public sealed record RuleSettings(int MinimumClicks = 20, decimal WasteSpend = 20, int MinimumOrders = 10,
    decimal TargetAcos = .3m, int AttributionDays = 7);
public sealed record Recommendation(string Type, string Severity, string Entity, string Explanation,
    string ProposedAction, string Confidence, DateOnly Start, DateOnly End, Guid ArtifactId, decimal Value);
public static class RecommendationRules
{
    public static IReadOnlyList<Recommendation> Evaluate(AdvertisingDailyMetric row, DateOnly today, RuleSettings settings)
    {
        if (settings.MinimumClicks < 1 || settings.WasteSpend <= 0 || settings.TargetAcos <= 0 || settings.AttributionDays < 1)
            throw new ArgumentException("Recommendation thresholds must be positive.");
        if (today.DayNumber - row.Date.DayNumber <= settings.AttributionDays) return [];
        var results = new List<Recommendation>();
        if (row.Clicks >= settings.MinimumClicks && row.Spend >= settings.WasteSpend && row.Orders7d == 0)
            results.Add(new("PPC_WASTE", "HIGH", row.AdId, $"{row.Clicks} clicks and {row.Spend} {row.Currency} spend without attributed orders.",
                "Review targeting and search terms before adjusting bids.", "MEDIUM", row.Date, row.Date, row.ArtifactId, row.Spend));
        if (row.Orders7d >= settings.MinimumOrders && Metrics.Acos(row.Spend, row.Sales7d) > settings.TargetAcos * 1.25m)
            results.Add(new("BID_REDUCTION_CANDIDATE", "MEDIUM", row.AdId, "Mature ACOS exceeds the configured target with sufficient orders.",
                "Review a lower bid against contribution margin.", "MEDIUM", row.Date, row.Date, row.ArtifactId, Metrics.Acos(row.Spend, row.Sales7d)!.Value));
        return results;
    }
}
