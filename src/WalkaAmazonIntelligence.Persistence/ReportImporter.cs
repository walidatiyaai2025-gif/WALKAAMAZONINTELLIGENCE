using Microsoft.EntityFrameworkCore;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Persistence;

public sealed class ReportImporter(DatabaseFactory factory) : IReportImporter
{
    public async Task<int> ImportAsync(ReportArtifact artifact, ParsedReport rows, CancellationToken ct)
    {
        if (artifact.Start != artifact.End) throw new InvalidDataException("Daily imports require a single date.");
        if (rows.Sales.Count > 0 && rows.Ads.Count > 0) throw new InvalidDataException("Mixed report sources.");
        if (rows.Sales.Any(r => r.Account != artifact.Account || r.Marketplace != artifact.Marketplace || r.Date != artifact.Start) ||
            rows.Ads.Any(r => r.Account != artifact.Account || r.Marketplace != artifact.Marketplace || r.Profile != artifact.Profile || r.Date != artifact.Start))
            throw new InvalidDataException("Report scope does not match artifact.");
        if (rows.Sales.Select(r => r.Asin).Distinct().Count() != rows.Sales.Count || rows.Ads.Select(r => r.AdId).Distinct().Count() != rows.Ads.Count)
            throw new InvalidDataException("Duplicate natural keys in report.");
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
        if (artifact.Source == "Seller")
            await db.Sales.Where(r => r.Account == artifact.Account && r.Marketplace == artifact.Marketplace && r.Date == artifact.Start).ExecuteDeleteAsync(ct);
        else if (artifact.Source == "Ads")
            await db.Ads.Where(r => r.Account == artifact.Account && r.Marketplace == artifact.Marketplace && r.Profile == artifact.Profile && r.Date == artifact.Start).ExecuteDeleteAsync(ct);
        else throw new InvalidDataException("Unsupported import source.");
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
}
