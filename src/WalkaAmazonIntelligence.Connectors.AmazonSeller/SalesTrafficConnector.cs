using System.Net.Http.Json;
using System.Text.Json;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
using WalkaAmazonIntelligence.Infrastructure;
namespace WalkaAmazonIntelligence.Connectors.AmazonSeller;

public sealed class SalesTrafficConnector(AmazonHttp http, IAccessTokenProvider tokens, string region) : IReportConnector, ICapabilityProbe
{
    private readonly Uri endpoint = AmazonRegions.Seller(region);
    public string Source => "Seller";
    public string ReportType => "GET_SALES_AND_TRAFFIC_REPORT";

    public async Task ValidateConfigurationAsync(DataScope scope, CancellationToken ct)
    {
        scope.Validate();
        _ = await tokens.GetAsync(ct);
    }

    public async Task ProbeCapabilitiesAsync(DataScope scope, CancellationToken ct)
    {
        await ValidateConfigurationAsync(scope, ct);
        var path = "/reports/2021-06-30/reports?reportTypes=" + Uri.EscapeDataString(ReportType) +
                   "&marketplaceIds=" + Uri.EscapeDataString(scope.Marketplace) + "&pageSize=1";
        using var doc = await AmazonAuthorization.SendAsync(
            http, tokens, token => Request(HttpMethod.Get, path, token), true, ct);

        if (!doc.RootElement.TryGetProperty("reports", out var reports) || reports.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Seller Reports capability response schema is invalid.");
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, new Uri(endpoint, path));
        req.Headers.Add("x-amz-access-token", token);
        req.Headers.UserAgent.ParseAdd("WalkaAmazonIntelligence/0.1.0 (Language=CSharp)");
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    public async Task<ReportTicket> RequestAsync(DataScope scope, DateOnly date, CancellationToken ct)
    {
        scope.Validate();
        await ProbeCapabilitiesAsync(scope, ct);
        var body = new
        {
            reportType = ReportType,
            marketplaceIds = new[] { scope.Marketplace },
            dataStartTime = $"{date:yyyy-MM-dd}T00:00:00Z",
            dataEndTime = $"{date:yyyy-MM-dd}T23:59:59Z",
            reportOptions = new { dateGranularity = "DAY", asinGranularity = "CHILD" }
        };
        using var doc = await AmazonAuthorization.SendAsync(
            http, tokens, token => Request(HttpMethod.Post, "/reports/2021-06-30/reports", token, body), false, ct);
        if (!doc.RootElement.TryGetProperty("reportId", out var reportId) ||
            reportId.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(reportId.GetString()))
            throw new InvalidDataException("Amazon returned an empty report identifier after report creation.");
        return new(reportId.GetString()!, "IN_QUEUE");
    }

    public async Task<ReportTicket> PollAsync(string id, CancellationToken ct)
    {
        using var doc = await AmazonAuthorization.SendAsync(
            http, tokens,
            token => Request(HttpMethod.Get, "/reports/2021-06-30/reports/" + Uri.EscapeDataString(id), token),
            true, ct);
        var root = doc.RootElement;
        var status = root.GetProperty("processingStatus").GetString()!;
        if (status != "DONE") return new(id, status);

        var documentId = root.GetProperty("reportDocumentId").GetString()!;
        using var document = await AmazonAuthorization.SendAsync(
            http, tokens,
            token => Request(HttpMethod.Get, "/reports/2021-06-30/documents/" + Uri.EscapeDataString(documentId), token),
            true, ct);
        var d = document.RootElement;
        return new(id, "COMPLETED", new Uri(d.GetProperty("url").GetString()!),
            d.TryGetProperty("compressionAlgorithm", out var c) ? c.GetString() : null,
            root.TryGetProperty("processingEndTime", out var end) ? end.GetDateTime() : null);
    }
}
