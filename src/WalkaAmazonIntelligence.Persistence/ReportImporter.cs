using Microsoft.EntityFrameworkCore;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Persistence;

public sealed class ReportImporter(DatabaseFactory factory) : IReportImporter
{
    private const string SellerSource = "Seller";
    private const string AdsSource = "Ads";

    public async Task<int> ImportAsync(ReportArtifact artifact, ParsedReport rows, CancellationToken ct)
    {
        Validate(artifact, rows);
        await using var db = factory.Create();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.Artifacts.SingleOrDefaultAsync(a => a.Source == artifact.Source && a.Account == artifact.Account &&
            a.Marketplace == artifact.Marketplace && a.Profile == artifact.Profile && a.ReportType == artifact.ReportType &&
            a.Start == artifact.Start && a.End == artifact.End && a.Sha256 == artifact.Sha256, ct);
        if (existing?.Status == ImportStatus.Imported)
        {
            // Replaying old evidence must not roll back a newer revision.
            return existing.RecordCount;
        }
        if (existing is not null) { artifact = existing; artifact.RetryCount++; }
        else db.Artifacts.Add(artifact);
        if (artifact.Source == SellerSource)
            await db.Sales.Where(r => r.Account == artifact.Account && r.Marketplace == artifact.Marketplace && r.Date == artifact.Start).ExecuteDeleteAsync(ct);
        else
            await db.Ads.Where(r => r.Account == artifact.Account && r.Marketplace == artifact.Marketplace && r.Profile == artifact.Profile && r.Date == artifact.Start).ExecuteDeleteAsync(ct);
        foreach (var row in rows.Sales) { row.Id = 0; row.ArtifactId = artifact.Id; db.Sales.Add(row); }
        foreach (var row in rows.Ads) { row.Id = 0; row.ArtifactId = artifact.Id; db.Ads.Add(row); }
        artifact.RecordCount = rows.Count;
        artifact.Status = ImportStatus.Imported;
        artifact.Error = null;
        await db.SaveChangesAsync(ct);
        var sales = await db.Sales.Where(r => r.ArtifactId == artifact.Id).ToListAsync(ct);
        var ads = await db.Ads.Where(r => r.ArtifactId == artifact.Id).ToListAsync(ct);
        if (sales.Count + ads.Count != rows.Count || sales.Sum(r => r.Sales) != rows.Sales.Sum(r => r.Sales) ||
            ads.Sum(r => r.Spend) != rows.Ads.Sum(r => r.Spend) || ads.Sum(r => r.Sales7d) != rows.Ads.Sum(r => r.Sales7d))
            throw new InvalidDataException("RECONCILIATION_FAILED: imported counts or totals differ.");
        db.Audit.Add(new() { Action = "REPORT_IMPORTED", Detail = $"{artifact.Source}; artifact={artifact.Id}; rows={rows.Count}; reconciled", CorrelationId = artifact.SyncRunId });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return rows.Count;
    }

    private static void Validate(ReportArtifact artifact, ParsedReport rows)
    {
        if (artifact.Start != artifact.End) throw new InvalidDataException("Daily imports require a single date.");
        if (artifact.Source is not (SellerSource or AdsSource)) throw new InvalidDataException("Unsupported import source.");
        if (string.IsNullOrWhiteSpace(artifact.Account) || string.IsNullOrWhiteSpace(artifact.Marketplace) ||
            string.IsNullOrWhiteSpace(artifact.ReportType) || string.IsNullOrWhiteSpace(artifact.Sha256))
            throw new InvalidDataException("Artifact identity is incomplete.");
        if (artifact.Source == AdsSource && string.IsNullOrWhiteSpace(artifact.Profile))
            throw new InvalidDataException("Ads imports require a profile.");
        if (rows.Sales.Count > 0 && rows.Ads.Count > 0) throw new InvalidDataException("Mixed report sources.");
        if (artifact.Source == SellerSource && rows.Ads.Count > 0)
            throw new InvalidDataException("Seller evidence cannot import advertising rows.");
        if (artifact.Source == AdsSource && rows.Sales.Count > 0)
            throw new InvalidDataException("Advertising evidence cannot import seller rows.");
        if (rows.Sales.Any(r => r.Account != artifact.Account || r.Marketplace != artifact.Marketplace || r.Date != artifact.Start) ||
            rows.Ads.Any(r => r.Account != artifact.Account || r.Marketplace != artifact.Marketplace || r.Profile != artifact.Profile || r.Date != artifact.Start))
            throw new InvalidDataException("Report scope does not match artifact.");
        if (rows.Sales.Any(r => string.IsNullOrWhiteSpace(r.Asin)) || rows.Ads.Any(r => string.IsNullOrWhiteSpace(r.AdId)))
            throw new InvalidDataException("Report natural key is incomplete.");
        if (rows.Sales.Select(r => r.Asin).Distinct(StringComparer.Ordinal).Count() != rows.Sales.Count ||
            rows.Ads.Select(r => r.AdId).Distinct(StringComparer.Ordinal).Count() != rows.Ads.Count)
            throw new InvalidDataException("Duplicate natural keys in report.");
    }
}
