using Microsoft.EntityFrameworkCore;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
using WalkaAmazonIntelligence.Infrastructure;
using WalkaAmazonIntelligence.Persistence;
namespace WalkaAmazonIntelligence.Worker;

public sealed class SyncCoordinator(DatabaseFactory factory, EvidenceArchive archive, IReportImporter importer)
{
    private const string ReportRequestStarted = "REPORT_REQUEST_STARTED";
    private const string ReportCreationUncertain = "REPORT_CREATION_UNCERTAIN";
    private const string ReportCreationConfirmedAbsent = "REPORT_CREATION_CONFIRMED_ABSENT";
    private const string ReportTerminalPrefix = "REPORT_TERMINAL_";

    // Desktop enforces a single process per storage root; this gate serializes requests within it.
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<string> StepAsync(IReportConnector connector, IReportParser parser, DataScope scope, DateOnly date, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        SyncRun? run = null;
        var reportCreationStarted = false;
        try
        {
            scope.Validate();
            if (date >= DateOnly.FromDateTime(DateTime.UtcNow)) throw new ArgumentException("Select a completed reporting day.");
            var key = ScopeKey(scope);
            await using var db = factory.Create();
            run = await db.SyncRuns
                .Where(r => r.Source == connector.Source && r.Scope == key && r.Date == date)
                .OrderByDescending(r => r.StartedUtc)
                .FirstOrDefaultAsync(ct);

            if (run is not null && IsClosedAttempt(run)) run = null;
            if (run is not null && run.RemoteReportId is null && run.Error == ReportCreationUncertain)
                throw new ReportCreationUncertainException(run.Id);

            if (run is null)
            {
                run = new() { Source = connector.Source, Scope = key, Date = date, Status = JobStatus.Pending };
                db.SyncRuns.Add(run);
                await db.SaveChangesAsync(ct);
            }

            await connector.ValidateConfigurationAsync(scope, ct);
            ReportTicket ticket;
            if (run.RemoteReportId is null)
            {
                // Persist the creation attempt before the non-idempotent POST. Any ambiguous outcome
                // is held for explicit reconciliation instead of automatically creating another report.
                run.Status = JobStatus.Running;
                run.CompletedUtc = null;
                run.Error = ReportRequestStarted;
                db.Audit.Add(new()
                {
                    Action = "SYNC_REPORT_REQUEST_STARTED",
                    CorrelationId = run.Id,
                    Detail = AuditDetail(connector.Source, ReportRequestStarted)
                });
                await db.SaveChangesAsync(ct);
                reportCreationStarted = true;

                ticket = await connector.RequestAsync(scope, date, ct);
                if (string.IsNullOrWhiteSpace(ticket.Id))
                    throw new InvalidDataException("Amazon returned an empty report identifier after report creation.");

                run.RemoteReportId = ticket.Id.Trim();
                run.Error = null;
                run.Status = JobStatus.Pending;
                reportCreationStarted = false;
                db.Audit.Add(new()
                {
                    Action = "SYNC_REPORT_ID_SAVED",
                    CorrelationId = run.Id,
                    Detail = AuditDetail(connector.Source, "REMOTE_REPORT_ID_PERSISTED")
                });
                await db.SaveChangesAsync(ct);
                return "Report requested. Use Check reports to retrieve it when Amazon finishes generation.";
            }

            run.Status = JobStatus.Running;
            run.CompletedUtc = null;
            run.Error = null;
            await db.SaveChangesAsync(ct);

            ticket = await connector.PollAsync(run.RemoteReportId, ct);
            if (IsTerminalRemoteStatus(ticket.Status))
                throw new RemoteReportTerminalException(ticket.Status);
            if (!string.Equals(ticket.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                run.Status = JobStatus.Pending;
                run.Error = null;
                await db.SaveChangesAsync(ct);
                return "Amazon report is processing. Its report ID is saved for restart.";
            }
            if (ticket.DownloadUri is null) throw new InvalidDataException("Completed report has no download location.");

            var artifact = await archive.DownloadAsync(ticket.DownloadUri, connector.Source, connector.ReportType, scope, date, run.Id, ticket.GeneratedUtc, ct);
            // Save evidence metadata before parsing so failed source files remain inspectable.
            var existing = await db.Artifacts.SingleOrDefaultAsync(a => a.Source == artifact.Source && a.Account == artifact.Account && a.Marketplace == artifact.Marketplace &&
                a.Profile == artifact.Profile && a.ReportType == artifact.ReportType && a.Start == date && a.End == date && a.Sha256 == artifact.Sha256, ct);
            if (existing is null)
            {
                db.Artifacts.Add(artifact);
                await db.SaveChangesAsync(ct);
            }
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
                    artifact.Status = ImportStatus.Failed;
                    artifact.Error = SafeErrorCode(ex);
                    artifact.RetryCount++;
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                throw;
            }

            run.Status = JobStatus.Completed;
            run.CompletedUtc = DateTime.UtcNow;
            run.Error = null;
            db.Audit.Add(new()
            {
                Action = "SYNC_COMPLETED",
                CorrelationId = run.Id,
                Detail = AuditDetail(connector.Source, $"RECORDS_{run.Records}")
            });
            await db.SaveChangesAsync(ct);
            return $"Imported and reconciled {run.Records} records. Raw evidence retained.";
        }
        catch (ReportCreationUncertainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (run is not null)
            {
                await using var db = factory.Create();
                var saved = await db.SyncRuns.FindAsync([run.Id], CancellationToken.None);
                if (saved is not null)
                {
                    if (reportCreationStarted && saved.RemoteReportId is null && IsCreationOutcomeAmbiguous(ex))
                    {
                        saved.Status = JobStatus.Failed;
                        saved.Error = ReportCreationUncertain;
                        saved.CompletedUtc = null;
                        db.Audit.Add(new()
                        {
                            Action = "SYNC_REPORT_CREATION_UNCERTAIN",
                            CorrelationId = saved.Id,
                            Detail = AuditDetail(connector.Source, ReportCreationUncertain)
                        });
                        await db.SaveChangesAsync(CancellationToken.None);
                        throw new ReportCreationUncertainException(saved.Id, ex);
                    }

                    var errorCode = SafeErrorCode(ex);
                    saved.Status = ex is AuthenticationRequiredException ? JobStatus.AuthRequired :
                        ex is OperationCanceledException ? JobStatus.Cancelled : JobStatus.Failed;
                    saved.Error = errorCode;
                    saved.CompletedUtc = DateTime.UtcNow;
                    db.Audit.Add(new()
                    {
                        Action = "SYNC_ATTEMPT_FAILED",
                        CorrelationId = saved.Id,
                        Detail = AuditDetail(connector.Source, errorCode)
                    });
                    await db.SaveChangesAsync(CancellationToken.None);
                }
            }
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ResolveUncertainRequestAsync(Guid syncRunId, string remoteReportId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(remoteReportId) || remoteReportId.Length > 256 || remoteReportId.Any(char.IsControl) || remoteReportId.Any(char.IsWhiteSpace))
            throw new ArgumentException("A valid Amazon report identifier is required.", nameof(remoteReportId));

        await gate.WaitAsync(ct);
        try
        {
            await using var db = factory.Create();
            var run = await db.SyncRuns.FindAsync([syncRunId], ct) ?? throw new KeyNotFoundException("Sync run not found.");
            if (run.RemoteReportId is not null || run.Error != ReportCreationUncertain)
                throw new InvalidOperationException("Only an unresolved report-creation attempt can be reconciled with a remote report ID.");

            run.RemoteReportId = remoteReportId.Trim();
            run.Status = JobStatus.Pending;
            run.Error = null;
            run.CompletedUtc = null;
            db.Audit.Add(new()
            {
                Action = "SYNC_REPORT_ID_RECOVERED",
                CorrelationId = run.Id,
                Detail = "MANUAL_REMOTE_REPORT_RECONCILIATION"
            });
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ConfirmNoRemoteReportAsync(Guid syncRunId, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var db = factory.Create();
            var run = await db.SyncRuns.FindAsync([syncRunId], ct) ?? throw new KeyNotFoundException("Sync run not found.");
            if (run.RemoteReportId is not null || run.Error != ReportCreationUncertain)
                throw new InvalidOperationException("Only an unresolved report-creation attempt can be confirmed absent.");

            run.Status = JobStatus.Failed;
            run.Error = ReportCreationConfirmedAbsent;
            run.CompletedUtc = DateTime.UtcNow;
            db.Audit.Add(new()
            {
                Action = "SYNC_REPORT_CREATION_CONFIRMED_ABSENT",
                CorrelationId = run.Id,
                Detail = "MANUAL_REMOTE_REPORT_RECONCILIATION"
            });
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string ScopeKey(DataScope scope) => $"{scope.Account}|{scope.Marketplace}|{scope.Profile}|{scope.Currency}";

    private static bool IsClosedAttempt(SyncRun run) =>
        run.Status == JobStatus.Completed ||
        run.Error == ReportCreationConfirmedAbsent ||
        (run.Error?.StartsWith(ReportTerminalPrefix, StringComparison.Ordinal) ?? false);

    private static bool IsCreationOutcomeAmbiguous(Exception ex) => ex switch
    {
        AuthenticationRequiredException => false,
        RemoteApiException remote when remote.StatusCode is >= 400 and < 500 => false,
        _ => true
    };

    private static bool IsTerminalRemoteStatus(string status)
    {
        var normalized = NormalizeCode(status);
        return normalized is "FATAL" or "FAILED" or "FAILURE" or "CANCELLED";
    }

    private static string SafeErrorCode(Exception ex) => ex switch
    {
        AuthenticationRequiredException => "AUTH_REQUIRED",
        OperationCanceledException => "CANCELLED",
        RemoteReportTerminalException terminal => ReportTerminalPrefix + NormalizeCode(terminal.Status),
        RemoteApiException remote => $"REMOTE_HTTP_{remote.StatusCode}",
        HttpRequestException => "NETWORK_ERROR",
        InvalidDataException => "INVALID_REMOTE_DATA",
        _ => NormalizeCode(ex.GetType().Name)
    };

    private static string AuditDetail(string source, string code) => $"{NormalizeCode(source)};{NormalizeCode(code)}";

    private static string NormalizeCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "UNKNOWN";
        var chars = value.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_').Take(64).ToArray();
        return chars.Length == 0 ? "UNKNOWN" : new string(chars);
    }

    private sealed class RemoteReportTerminalException(string status) : Exception("Amazon report reached a terminal remote state.")
    {
        public string Status { get; } = status;
    }
}
