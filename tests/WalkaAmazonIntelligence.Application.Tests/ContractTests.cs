using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Application.Tests;
public class ContractTests
{
    [Fact] public void BackfillIncludesBothBoundaries()
    {
        var range = new ReportRange(new(2026, 1, 30), new(2026, 2, 2));
        Assert.Equal(4, range.Days().Count()); Assert.Equal(range.End, range.Days().Last());
    }
    [Fact] public void InvertedRangeAndMissingScopeFail()
    {
        Assert.Throws<ArgumentException>(() => new ReportRange(new(2026, 2, 1), new(2026, 1, 1)).Validate());
        Assert.Throws<ArgumentException>(() => new DataScope("", "market", "USD").Validate());
        Assert.Throws<ArgumentException>(() => new DataScope("account", "market", "usd").Validate());
    }
}
