using Microsoft.EntityFrameworkCore;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Persistence;

public sealed class DashboardService(DatabaseFactory factory)
{
    public async Task<IReadOnlyList<DashboardRow>> ReadAsync(DataScope scope, ReportRange range, CancellationToken ct = default)
    {
        scope.Validate(); range.Validate();
        await using var db = factory.Create();
        var sales = await db.Sales.AsNoTracking().Where(r => r.Account == scope.Account && r.Marketplace == scope.Marketplace && r.Currency == scope.Currency && r.Date >= range.Start && r.Date <= range.End).ToListAsync(ct);
        var ads = await db.Ads.AsNoTracking().Where(r => r.Account == scope.Account && r.Marketplace == scope.Marketplace && r.Profile == scope.Profile && r.Currency == scope.Currency && r.Date >= range.Start && r.Date <= range.End).ToListAsync(ct);
        return sales.Select(r => r.Asin).Union(ads.Select(r => r.Asin)).Order().Select(asin =>
        {
            var s = sales.Where(r => r.Asin == asin).ToList(); var a = ads.Where(r => r.Asin == asin).ToList();
            decimal? revenue = s.Count > 0 ? s.Sum(r => r.Sales) : null;
            decimal? spend = a.Count > 0 ? a.Sum(r => r.Spend) : null;
            decimal? adSales = a.Count > 0 ? a.Sum(r => r.Sales7d) : null;
            // TACOS is withheld unless both source sets cover the same observed dates.
            var aligned = s.Select(r => r.Date).Distinct().Order().SequenceEqual(a.Select(r => r.Date).Distinct().Order());
            return new DashboardRow(asin, scope.Currency, revenue, s.Count > 0 ? s.Sum(r => r.Units) : null,
                s.Count > 0 ? s.Sum(r => r.OrderItems) : null,
                s.Count > 0 && s.All(r => r.Sessions.HasValue) ? s.Sum(r => r.Sessions!.Value) : null,
                spend, adSales, Metrics.Acos(spend, adSales), Metrics.Roas(adSales, spend), aligned ? Metrics.Tacos(spend, revenue) : null);
        }).ToList();
    }
    public async Task SaveSettingAsync(string key, string value, CancellationToken ct = default)
    {
        if (key is not ("account" or "marketplace" or "profile" or "currency" or "region" or "theme" or "language" or "clientId"))
            throw new ArgumentException("Setting is not allowlisted; secrets must use protected storage.");
        await using var db = factory.Create();
        var row = await db.Settings.FindAsync([key], ct);
        if (row is null) db.Settings.Add(new() { Key = key, Value = value }); else row.Value = value;
        db.Audit.Add(new() { Action = "SETTING_CHANGED", Detail = key });
        await db.SaveChangesAsync(ct);
    }
    public async Task<Dictionary<string, string>> SettingsAsync(CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Settings.AsNoTracking().ToDictionaryAsync(r => r.Key, r => r.Value, ct);
    }
}
