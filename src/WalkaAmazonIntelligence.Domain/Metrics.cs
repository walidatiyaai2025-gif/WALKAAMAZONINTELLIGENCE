namespace WalkaAmazonIntelligence.Domain;

public enum MetricUnavailableReason
{
    MissingInput,
    ZeroDenominator,
    NegativeInput,
    IncompatibleScope,
    AttributionExceedsTotal,
    NonPositiveVelocity,
    OutOfRangeRate,
    OutOfRangeDate
}

public readonly record struct MetricResult(decimal? Value, MetricUnavailableReason? Reason)
{
    public bool IsAvailable => Value.HasValue && Reason is null;
    public static MetricResult Available(decimal value) => new(value, null);
    public static MetricResult Unavailable(MetricUnavailableReason reason) => new(null, reason);
}

public readonly record struct DateMetricResult(DateOnly? Value, MetricUnavailableReason? Reason)
{
    public bool IsAvailable => Value.HasValue && Reason is null;
    public static DateMetricResult Available(DateOnly value) => new(value, null);
    public static DateMetricResult Unavailable(MetricUnavailableReason reason) => new(null, reason);
}

public sealed record RatioObservation(decimal? Numerator, decimal? Denominator);

public static class Metrics
{
    public static MetricResult RatioResult(decimal? numerator, decimal? denominator)
    {
        if (!numerator.HasValue || !denominator.HasValue)
            return MetricResult.Unavailable(MetricUnavailableReason.MissingInput);
        if (numerator < 0 || denominator < 0)
            return MetricResult.Unavailable(MetricUnavailableReason.NegativeInput);
        if (denominator == 0)
            return MetricResult.Unavailable(MetricUnavailableReason.ZeroDenominator);
        return MetricResult.Available(numerator.Value / denominator.Value);
    }

    public static decimal? Ratio(decimal? numerator, decimal? denominator) => RatioResult(numerator, denominator).Value;
    public static MetricResult CtrResult(long? clicks, long? impressions) => RatioResult(clicks, impressions);
    public static decimal? Ctr(long? clicks, long? impressions) => CtrResult(clicks, impressions).Value;
    public static MetricResult CpcResult(decimal? spend, long? clicks) => RatioResult(spend, clicks);
    public static decimal? Cpc(decimal? spend, long? clicks) => CpcResult(spend, clicks).Value;
    public static MetricResult CvrResult(long? orders, long? clicks) => RatioResult(orders, clicks);
    public static decimal? Cvr(long? orders, long? clicks) => CvrResult(orders, clicks).Value;
    public static MetricResult AcosResult(decimal? spend, decimal? sales) => RatioResult(spend, sales);
    public static decimal? Acos(decimal? spend, decimal? sales) => AcosResult(spend, sales).Value;
    public static MetricResult RoasResult(decimal? sales, decimal? spend) => RatioResult(sales, spend);
    public static decimal? Roas(decimal? sales, decimal? spend) => RoasResult(sales, spend).Value;
    public static MetricResult TacosResult(decimal? spend, decimal? totalSales) => RatioResult(spend, totalSales);
    public static decimal? Tacos(decimal? spend, decimal? totalSales) => TacosResult(spend, totalSales).Value;

    public static MetricResult AggregateRatioResult(IEnumerable<RatioObservation>? observations)
    {
        if (observations is null)
            return MetricResult.Unavailable(MetricUnavailableReason.MissingInput);

        var rows = observations.ToList();
        if (rows.Count == 0 || rows.Any(row => !row.Numerator.HasValue || !row.Denominator.HasValue))
            return MetricResult.Unavailable(MetricUnavailableReason.MissingInput);
        if (rows.Any(row => row.Numerator < 0 || row.Denominator < 0))
            return MetricResult.Unavailable(MetricUnavailableReason.NegativeInput);

        return RatioResult(rows.Sum(row => row.Numerator!.Value), rows.Sum(row => row.Denominator!.Value));
    }

    public static decimal? AggregateRatio(IEnumerable<RatioObservation>? observations) =>
        AggregateRatioResult(observations).Value;

    public static MetricResult OrganicEstimateResult(decimal? sales, decimal? adSales, bool comparable)
    {
        if (!sales.HasValue || !adSales.HasValue)
            return MetricResult.Unavailable(MetricUnavailableReason.MissingInput);
        if (sales < 0 || adSales < 0)
            return MetricResult.Unavailable(MetricUnavailableReason.NegativeInput);
        if (!comparable)
            return MetricResult.Unavailable(MetricUnavailableReason.IncompatibleScope);
        if (adSales > sales)
            return MetricResult.Unavailable(MetricUnavailableReason.AttributionExceedsTotal);
        return MetricResult.Available(sales.Value - adSales.Value);
    }

    public static MetricResult OrganicEstimateResult(
        decimal? sales,
        decimal? adSales,
        MetricWindow? salesWindow,
        MetricWindow? adsWindow,
        bool requireProfile = false)
    {
        if (salesWindow is null || adsWindow is null)
            return MetricResult.Unavailable(MetricUnavailableReason.MissingInput);

        return OrganicEstimateResult(sales, adSales, salesWindow.IsComparableTo(adsWindow, requireProfile));
    }

    public static decimal? OrganicEstimate(decimal? sales, decimal? adSales, bool comparable) =>
        OrganicEstimateResult(sales, adSales, comparable).Value;

    public static MetricResult ContributionResult(decimal? revenue, params decimal?[] costs)
    {
        if (!revenue.HasValue || costs is null || costs.Any(cost => !cost.HasValue))
            return MetricResult.Unavailable(MetricUnavailableReason.MissingInput);
        if (revenue < 0 || costs.Any(cost => cost < 0))
            return MetricResult.Unavailable(MetricUnavailableReason.NegativeInput);
        return MetricResult.Available(revenue.Value - costs.Sum(cost => cost!.Value));
    }

    public static decimal? Contribution(decimal? revenue, params decimal?[] costs) =>
        ContributionResult(revenue, costs).Value;

    public static MetricResult BreakEvenAcosResult(decimal? preAdContribution, decimal? revenue)
    {
        if (!preAdContribution.HasValue || !revenue.HasValue)
            return MetricResult.Unavailable(MetricUnavailableReason.MissingInput);
        if (revenue < 0)
            return MetricResult.Unavailable(MetricUnavailableReason.NegativeInput);
        if (revenue == 0)
            return MetricResult.Unavailable(MetricUnavailableReason.ZeroDenominator);
        return MetricResult.Available(preAdContribution.Value / revenue.Value);
    }

    public static decimal? BreakEvenAcos(decimal? preAdContribution, decimal? revenue) =>
        BreakEvenAcosResult(preAdContribution, revenue).Value;

    public static MetricResult BreakEvenCpcResult(decimal? preAdContributionPerOrder, decimal? adCvr)
    {
        if (!preAdContributionPerOrder.HasValue || !adCvr.HasValue)
            return MetricResult.Unavailable(MetricUnavailableReason.MissingInput);
        if (adCvr < 0 || adCvr > 1)
            return MetricResult.Unavailable(MetricUnavailableReason.OutOfRangeRate);
        return MetricResult.Available(preAdContributionPerOrder.Value * adCvr.Value);
    }

    public static decimal? BreakEvenCpc(decimal? preAdContributionPerOrder, decimal? adCvr) =>
        BreakEvenCpcResult(preAdContributionPerOrder, adCvr).Value;

    public static MetricResult DaysOfSupplyResult(int? available, decimal? dailyUnits)
    {
        if (!available.HasValue || !dailyUnits.HasValue)
            return MetricResult.Unavailable(MetricUnavailableReason.MissingInput);
        if (available < 0 || dailyUnits < 0)
            return MetricResult.Unavailable(MetricUnavailableReason.NegativeInput);
        if (dailyUnits == 0)
            return MetricResult.Unavailable(MetricUnavailableReason.NonPositiveVelocity);
        return MetricResult.Available(available.Value / dailyUnits.Value);
    }

    public static decimal? DaysOfSupply(int? available, decimal? dailyUnits) =>
        DaysOfSupplyResult(available, dailyUnits).Value;

    public static DateMetricResult StockoutDateResult(DateOnly observationDate, int? available, decimal? dailyUnits)
    {
        var supply = DaysOfSupplyResult(available, dailyUnits);
        if (!supply.IsAvailable)
            return DateMetricResult.Unavailable(supply.Reason ?? MetricUnavailableReason.MissingInput);

        var wholeDays = decimal.Ceiling(supply.Value!.Value);
        var remainingDays = DateOnly.MaxValue.DayNumber - observationDate.DayNumber;
        if (wholeDays > remainingDays)
            return DateMetricResult.Unavailable(MetricUnavailableReason.OutOfRangeDate);

        return DateMetricResult.Available(observationDate.AddDays((int)wholeDays));
    }

    public static DateOnly? StockoutDate(DateOnly observationDate, int? available, decimal? dailyUnits) =>
        StockoutDateResult(observationDate, available, dailyUnits).Value;
}

public enum ConnectionStatus { NOT_CONFIGURED, CONNECTING, CONNECTED, AUTH_REQUIRED, DEGRADED, ERROR }
public enum ImportStatus { Downloaded, Imported, Failed }
public enum JobStatus { Pending, Running, Completed, Failed, AuthRequired, Cancelled }
public enum RecommendationStatus { NEW, REVIEWED, ACCEPTED, REJECTED, APPLIED, EXPIRED }

public sealed record DataScope(string Account, string Marketplace, string Currency, string Profile = "")
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Account) || string.IsNullOrWhiteSpace(Marketplace) ||
            string.IsNullOrWhiteSpace(Currency) || Currency.Length != 3 || !Currency.All(char.IsAsciiLetterUpper))
            throw new ArgumentException("Account, marketplace and ISO currency are required.");
    }

    public bool IsComparableTo(DataScope? other, bool requireProfile = false) =>
        other is not null &&
        string.Equals(Account, other.Account, StringComparison.Ordinal) &&
        string.Equals(Marketplace, other.Marketplace, StringComparison.Ordinal) &&
        string.Equals(Currency, other.Currency, StringComparison.Ordinal) &&
        (!requireProfile || string.Equals(Profile, other.Profile, StringComparison.Ordinal));
}

public sealed record ReportRange(DateOnly Start, DateOnly End)
{
    public void Validate()
    {
        if (End < Start) throw new ArgumentException("End date precedes start date.");
        if (End.DayNumber - Start.DayNumber > 365) throw new ArgumentException("Partition backfill into at most one year.");
    }

    public IEnumerable<DateOnly> Days()
    {
        Validate();
        for (var day = Start; day <= End; day = day.AddDays(1)) yield return day;
    }
}

public sealed record MetricWindow(DataScope Scope, ReportRange Range)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Scope);
        ArgumentNullException.ThrowIfNull(Range);
        Scope.Validate();
        Range.Validate();
    }

    public bool IsComparableTo(MetricWindow? other, bool requireProfile = false) =>
        other is not null && Scope.IsComparableTo(other.Scope, requireProfile) && Range == other.Range;
}
