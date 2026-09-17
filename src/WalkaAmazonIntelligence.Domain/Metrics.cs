namespace WalkaAmazonIntelligence.Domain;

public static class Metrics
{
    public static decimal? Ratio(decimal? numerator, decimal? denominator) =>
        numerator.HasValue && denominator > 0 ? numerator / denominator : null;
    public static decimal? Ctr(long? clicks, long? impressions) => Ratio(clicks, impressions);
    public static decimal? Cpc(decimal? spend, long? clicks) => Ratio(spend, clicks);
    public static decimal? Cvr(long? orders, long? clicks) => Ratio(orders, clicks);
    public static decimal? Acos(decimal? spend, decimal? sales) => Ratio(spend, sales);
    public static decimal? Roas(decimal? sales, decimal? spend) => Ratio(sales, spend);
    public static decimal? Tacos(decimal? spend, decimal? totalSales) => Ratio(spend, totalSales);
    public static decimal? OrganicEstimate(decimal? sales, decimal? adSales, bool comparable) =>
        comparable && sales >= adSales ? sales - adSales : null;
    public static decimal? Contribution(decimal? revenue, params decimal?[] costs) =>
        revenue.HasValue && costs.All(c => c.HasValue) ? revenue - costs.Sum(c => c!.Value) : null;
    public static decimal? DaysOfSupply(int? available, decimal? dailyUnits) =>
        available >= 0 ? Ratio(available, dailyUnits) : null;
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
            Currency.Length != 3 || !Currency.All(char.IsAsciiLetterUpper))
            throw new ArgumentException("Account, marketplace and ISO currency are required.");
    }
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
