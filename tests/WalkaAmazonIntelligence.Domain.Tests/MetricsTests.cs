using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Domain.Tests;
public class MetricsTests
{
    [Fact] public void RatiosUseCorrectDenominators()
    {
        Assert.Equal(.25m, Metrics.Acos(25, 100)); Assert.Equal(4m, Metrics.Roas(100, 25));
        Assert.Equal(.05m, Metrics.Tacos(25, 500)); Assert.Equal(.02m, Metrics.Ctr(20, 1000));
        Assert.Equal(1.25m, Metrics.Cpc(25, 20)); Assert.Equal(.1m, Metrics.Cvr(2, 20));
    }
    [Fact] public void MissingOrZeroDenominatorsAreUnknown()
    {
        Assert.Null(Metrics.Acos(10, 0)); Assert.Null(Metrics.Roas(null, 10)); Assert.Null(Metrics.Cpc(0, null));
        Assert.Null(Metrics.Contribution(100, 20, null)); Assert.Null(Metrics.DaysOfSupply(20, 0));
        Assert.Null(Metrics.OrganicEstimate(100, 120, true)); Assert.Null(Metrics.OrganicEstimate(100, 20, false));
    }
    [Fact] public void ProfitAndInventoryCalculateFromCompleteInputs()
    { Assert.Equal(45, Metrics.Contribution(100, 20, 30, 5)); Assert.Equal(10, Metrics.DaysOfSupply(50, 5)); }
}
