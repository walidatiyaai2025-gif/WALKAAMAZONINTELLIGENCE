using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Domain.Tests;

public class MetricsTests
{
    [Fact]
    public void RatiosUseCorrectDenominators()
    {
        Assert.Equal(.25m, Metrics.Acos(25, 100));
        Assert.Equal(4m, Metrics.Roas(100, 25));
        Assert.Equal(.05m, Metrics.Tacos(25, 500));
        Assert.Equal(.02m, Metrics.Ctr(20, 1000));
        Assert.Equal(1.25m, Metrics.Cpc(25, 20));
        Assert.Equal(.1m, Metrics.Cvr(2, 20));
    }

    [Fact]
    public void MissingZeroAndNegativeRatioInputsAreReasonCoded()
    {
        Assert.Equal(MetricUnavailableReason.ZeroDenominator, Metrics.AcosResult(10, 0).Reason);
        Assert.Equal(MetricUnavailableReason.MissingInput, Metrics.RoasResult(null, 10).Reason);
        Assert.Equal(MetricUnavailableReason.MissingInput, Metrics.CpcResult(0, null).Reason);
        Assert.Equal(MetricUnavailableReason.NegativeInput, Metrics.CtrResult(-1, 100).Reason);
        Assert.Equal(MetricUnavailableReason.NegativeInput, Metrics.TacosResult(10, -1).Reason);
    }

    [Fact]
    public void AggregateRatiosUseSummedNumeratorsAndDenominators()
    {
        var result = Metrics.AggregateRatioResult(
        [
            new RatioObservation(1, 2),
            new RatioObservation(9, 10)
        ]);

        Assert.True(result.IsAvailable);
        Assert.Equal(10m / 12m, result.Value.GetValueOrDefault());
        Assert.NotEqual((.5m + .9m) / 2m, result.Value.GetValueOrDefault());
        Assert.Equal(
            MetricUnavailableReason.MissingInput,
            Metrics.AggregateRatioResult([new RatioObservation(1, null)]).Reason);
    }

    [Fact]
    public void OrganicEstimateRequiresCompatibleScopeWindowAndCurrency()
    {
        var salesWindow = Window("USD", new(2026, 9, 1), new(2026, 9, 7));
        var matchingAds = Window("USD", new(2026, 9, 1), new(2026, 9, 7));
        var wrongCurrency = Window("CAD", new(2026, 9, 1), new(2026, 9, 7));
        var wrongDates = Window("USD", new(2026, 9, 2), new(2026, 9, 8));

        Assert.Equal(70m, Metrics.OrganicEstimateResult(100, 30, salesWindow, matchingAds).Value);
        Assert.Equal(
            MetricUnavailableReason.IncompatibleScope,
            Metrics.OrganicEstimateResult(100, 30, salesWindow, wrongCurrency).Reason);
        Assert.Equal(
            MetricUnavailableReason.IncompatibleScope,
            Metrics.OrganicEstimateResult(100, 30, salesWindow, wrongDates).Reason);
        Assert.Equal(
            MetricUnavailableReason.AttributionExceedsTotal,
            Metrics.OrganicEstimateResult(100, 120, salesWindow, matchingAds).Reason);
    }

    [Fact]
    public void ProfileComparisonIsExplicit()
    {
        var seller = new MetricWindow(
            new DataScope("account", "market", "USD", "profile-a"),
            new ReportRange(new(2026, 9, 1), new(2026, 9, 7)));
        var ads = new MetricWindow(
            new DataScope("account", "market", "USD", "profile-b"),
            new ReportRange(new(2026, 9, 1), new(2026, 9, 7)));

        Assert.True(seller.IsComparableTo(ads));
        Assert.False(seller.IsComparableTo(ads, requireProfile: true));
    }

    [Fact]
    public void ContributionBreakEvenAndInventoryMetricsCalculateFromCompleteInputs()
    {
        Assert.Equal(45m, Metrics.Contribution(100, 20, 30, 5));
        Assert.Equal(.45m, Metrics.BreakEvenAcos(45, 100));
        Assert.Equal(4.5m, Metrics.BreakEvenCpc(45, .1m));
        Assert.Equal(10m, Metrics.DaysOfSupply(50, 5));
        Assert.Equal(new DateOnly(2026, 9, 20), Metrics.StockoutDate(new(2026, 9, 17), 5, 2));
    }

    [Fact]
    public void IncompleteOrInvalidProfitInventoryAndRateInputsStayUnknown()
    {
        Assert.Equal(MetricUnavailableReason.MissingInput, Metrics.ContributionResult(100, 20, null).Reason);
        Assert.Equal(MetricUnavailableReason.NegativeInput, Metrics.ContributionResult(100, -1).Reason);
        Assert.Equal(MetricUnavailableReason.ZeroDenominator, Metrics.BreakEvenAcosResult(20, 0).Reason);
        Assert.Equal(MetricUnavailableReason.OutOfRangeRate, Metrics.BreakEvenCpcResult(20, 1.1m).Reason);
        Assert.Equal(MetricUnavailableReason.NonPositiveVelocity, Metrics.DaysOfSupplyResult(20, 0).Reason);
        Assert.Equal(MetricUnavailableReason.NegativeInput, Metrics.DaysOfSupplyResult(-1, 2).Reason);
        Assert.Equal(
            MetricUnavailableReason.NonPositiveVelocity,
            Metrics.StockoutDateResult(new(2026, 9, 17), 20, 0).Reason);
    }

    [Fact]
    public void ScopeAndRangeValidationFailClosed()
    {
        Assert.Throws<ArgumentException>(() => new DataScope("", "market", "USD").Validate());
        Assert.Throws<ArgumentException>(() => new DataScope("account", "market", "usd").Validate());
        Assert.Throws<ArgumentException>(() => new ReportRange(new(2026, 2, 1), new(2026, 1, 1)).Validate());
    }

    private static MetricWindow Window(string currency, DateOnly start, DateOnly end) =>
        new(new DataScope("account", "market", currency), new ReportRange(start, end));
}
