using Microsoft.EntityFrameworkCore;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
using WalkaAmazonIntelligence.Infrastructure;
using WalkaAmazonIntelligence.Persistence;
namespace WalkaAmazonIntelligence.Worker;

public sealed class SyncCoordinator(DatabaseFactory factory, EvidenceArchive archive, IReportImporter importer)
{
    // Desktop enforces a single process per storage root; this gate serializes requests within it.
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<string> StepAsync(IReportConnector connector, IReportParser parser, DataScope scope, DateOnly date, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        SyncRun? run = null;
        try
        {
            scope.Validate();
            if (date >= DateOnly.FromDateTime(DateTime.UtcNow)) throw new ArgumentException("Select a completed reporting day.");
            var key = $"{scope.Account}|{scope.Marketplace}|{scope.Profile}|{scope.Currency}";
            await using var db = factory.Create();
            run = await db.SyncRuns.Where(r => r.Source == connector.Source && r.Scope == key && r.Date == date &&
                (r.Status == JobStatus.Pending || r.Status == JobStatus.Running || r.Status == JobStatus.AuthRequired || r.Status == JobStatus.Cancelled))
                .OrderByDescending(r => r.StartedUtc).FirstOrDefaultAsync(ct);
            if (run is null)
            {
                run = new() { Source = connector.Source, Scope = key, Date = date, Status = JobStatus.Pending };
                db.SyncRuns.Add(run); await db.SaveChangesAsync(ct);
            }
            await connector.ValidateConfigurationAsync(scope, ct);
            ReportTicket ticket;
            if (run.RemoteReportId is null)
            {
                // Persist uncertainty before a non-idempotent request; never automatically duplicate an ambiguous POST.
                run.Status = JobStatus.Running; run.Error = "REPORT_REQUEST_STARTED"; await db.SaveChangesAsync(ct);
                ticket = await connector.RequestAsync(scope, date, ct);
                run.RemoteReportId = ticket.Id; run.Error = null; run.Status = JobStatus.Pending;
                await db.SaveChangesAsync(ct);
                return "Report requested. Use Check reports to retrieve it when Amazon finishes generation.";
            }
            ticket = await connector.PollAsync(run.RemoteReportId, ct);
            if (ticket.Status is "FATAL" or "FAILED" or "CANCELLED") throw new InvalidDataException("Amazon report ended with " + ticket.Status);
            if (ticket.Status != "COMPLETED")
            { run.Status = JobStatus.Pending; await db.SaveChangesAsync(ct); return "Amazon report is processing. Its report ID is saved for restart."; }
            if (ticket.DownloadUri is null) throw new InvalidDataException("Completed report has no download location.");
            var artifact = await archive.DownloadAsync(ticket.DownloadUri, connector.Source, connector.ReportType, scope, date, run.Id, ticket.GeneratedUtc, ct);
            // Save evidence metadata before parsing so failed source files remain inspectable.
            var existing = await db.Artifacts.SingleOrDefaultAsync(a => a.Source == artifact.Source && a.Account == artifact.Account && a.Marketplace == artifact.Marketplace &&
                a.Profile == artifact.Profile && a.ReportType == artifact.ReportType && a.Start == date && a.End == date && a.Sha256 == artifact.Sha256, ct);
            if (existing is null) { db.Artifacts.Add(artifact); await db.SaveChangesAsync(ct); }
            else artifact = existing;
            try
            {
                using var json = EvidenceArchive.OpenJson(artifact.Path, ticket.Compression);
                var rows = parser.Parse(json, scope, date);
                run.Records = await importer.ImportAsync(artifact, rows, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Existing successfully imported evidence must never be relabeled as failed.
                if (artifact.Status != ImportStatus.Imported)
                {
                    artifact.Status = ImportStatus.Failed; artifact.Error = ex.GetType().Name;
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                throw;
            }
            run.Status = JobStatus.Completed; run.CompletedUtc = DateTime.UtcNow; run.Error = null;
            db.Audit.Add(new() { Action = "SYNC_COMPLETED", CorrelationId = run.Id, Detail = connector.Source + "; rows=" + run.Records });
            await db.SaveChangesAsync(ct);
            return $"Imported and reconciled {run.Records} records. Raw evidence retained.";
        }
        catch (Exception ex)
        {
            if (run is not null)
            {
                await using var db = factory.Create();
                var saved = await db.SyncRuns.FindAsync([run.Id], CancellationToken.None);
                if (saved is not null)
                {
                    saved.Status = ex is AuthenticationRequiredException ? JobStatus.AuthRequired :
                        ex is OperationCanceledException && saved.RemoteReportId is not null ? JobStatus.Cancelled : JobStatus.Failed;
                    saved.Error = ex is AuthenticationRequiredException ? "AUTH_REQUIRED" : ex.GetType().Name;
                    saved.CompletedUtc = DateTime.UtcNow;
                    db.Audit.Add(new() { Action = "SYNC_FAILED", CorrelationId = saved.Id, Detail = saved.Error });
                    await db.SaveChangesAsync(CancellationToken.None);
                }
            }
            throw;
        }
        finally { gate.Release(); }
    }
}
