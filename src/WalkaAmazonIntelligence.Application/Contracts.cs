using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Application;

public interface ISecretStore
{
    Task SaveAsync(string name, string value, CancellationToken ct = default);
    Task<string?> ReadAsync(string name, CancellationToken ct = default);
}
public interface IAccessTokenProvider { Task<string> GetAsync(CancellationToken ct); }
public interface IRefreshableAccessTokenProvider : IAccessTokenProvider
{
    Task InvalidateAsync(CancellationToken ct);
}
public interface ICapabilityProbe
{
    Task ProbeCapabilitiesAsync(DataScope scope, CancellationToken ct);
}
public sealed class AuthenticationRequiredException(int? statusCode = null)
    : Exception("AUTH_REQUIRED: configure or renew Amazon authorization.")
{
    public int? StatusCode { get; } = statusCode;
}
public sealed class CapabilityUnavailableException : Exception
{
    public string Code { get; }

    public CapabilityUnavailableException(string code)
        : base($"CAPABILITY_UNAVAILABLE: {ValidateCode(code)}.")
    {
        Code = ValidateCode(code);
    }

    private static string ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 80 ||
            code.Any(c => !(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')))
            throw new ArgumentException("Capability code must be an uppercase machine-readable identifier.", nameof(code));
        return code;
    }
}
public sealed class RemoteApiException(int status) : Exception($"Amazon request failed (HTTP {status}).")
{ public int StatusCode { get; } = status; }
public sealed class ReportCreationUncertainException(Guid syncRunId, Exception? inner = null)
    : Exception($"Amazon report creation outcome is uncertain for sync run {syncRunId}. Reconcile the remote report before retrying.", inner)
{
    public Guid SyncRunId { get; } = syncRunId;
}
public sealed record ReportTicket(string Id, string Status, Uri? DownloadUri = null, string? Compression = null, DateTime? GeneratedUtc = null);
public interface IReportConnector
{
    string Source { get; }
    string ReportType { get; }
    Task ValidateConfigurationAsync(DataScope scope, CancellationToken ct);
    Task<ReportTicket> RequestAsync(DataScope scope, DateOnly date, CancellationToken ct);
    Task<ReportTicket> PollAsync(string id, CancellationToken ct);
}
public sealed record ParsedReport(IReadOnlyList<DailySales> Sales, IReadOnlyList<AdvertisingDailyMetric> Ads)
{
    public int Count => Sales.Count + Ads.Count;
}
public interface IReportParser
{
    ParsedReport Parse(Stream json, DataScope scope, DateOnly date);
}
public interface IReportImporter
{
    Task<int> ImportAsync(ReportArtifact artifact, ParsedReport rows, CancellationToken ct);
}
public sealed record DashboardRow(string Asin, string Currency, decimal? Sales, int? Units, int? OrderItems,
    int? Sessions, decimal? AdSpend, decimal? AdSales, decimal? Acos, decimal? Roas, decimal? Tacos);
