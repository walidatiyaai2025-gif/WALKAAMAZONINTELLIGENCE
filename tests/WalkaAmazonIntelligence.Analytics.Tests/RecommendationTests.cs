using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Analytics.Tests;
public class RecommendationTests
{
    [Fact] public void ImmatureAttributionCannotProduceWasteAdvice()
    {
        var row = new AdvertisingDailyMetric { Date = new(2026, 1, 1), Spend = 50, Clicks = 50 };
        Assert.Empty(RecommendationRules.Evaluate(row, new(2026, 1, 7), new()));
        Assert.Single(RecommendationRules.Evaluate(row, new(2026, 1, 10), new()));
    }
    [Fact] public void OrdersDisqualifyZeroConversionRule()
    {
        var row = new AdvertisingDailyMetric { Date = new(2026, 1, 1), Spend = 50, Clicks = 50, Orders7d = 1 };
        Assert.Empty(RecommendationRules.Evaluate(row, new(2026, 2, 1), new()));
    }
}
