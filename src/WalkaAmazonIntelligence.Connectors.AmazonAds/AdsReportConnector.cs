using System.Net.Http.Headers;
using System.Net.Http.Json;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
using WalkaAmazonIntelligence.Infrastructure;
namespace WalkaAmazonIntelligence.Connectors.AmazonAds;

public sealed class AdsReportConnector(AmazonHttp http, IAccessTokenProvider tokens, string region, string clientId, string profile) : IReportConnector
{
    private readonly Uri endpoint = AmazonRegions.Ads(region);
    public string Source => "Ads";
    public string ReportType => "spAdvertisedProduct";
    public async Task ValidateConfigurationAsync(DataScope scope, CancellationToken ct)
    {
        scope.Validate(); if (string.IsNullOrWhiteSpace(profile) || scope.Profile != profile || string.IsNullOrWhiteSpace(clientId)) throw new AuthenticationRequiredException();
        _ = await tokens.GetAsync(ct);
    }
    private HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(endpoint, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Amazon-Advertising-API-ClientId", clientId);
        request.Headers.Add("Amazon-Advertising-API-Scope", profile);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
            request.Content.Headers.ContentType = new("application/vnd.createasyncreportrequest.v3+json");
        }
        return request;
    }
    public async Task<ReportTicket> RequestAsync(DataScope scope, DateOnly date, CancellationToken ct)
    {
        await ValidateConfigurationAsync(scope, ct); var token = await tokens.GetAsync(ct);
        var body = new { name = $"WALKA {date:yyyy-MM-dd}", startDate = date.ToString("yyyy-MM-dd"), endDate = date.ToString("yyyy-MM-dd"),
            configuration = new { adProduct = "SPONSORED_PRODUCTS", groupBy = new[] { "advertiser" }, reportTypeId = ReportType,
                timeUnit = "DAILY", format = "GZIP_JSON", columns = new[] { "date", "campaignId", "adGroupId", "adId", "advertisedAsin", "advertisedSku", "impressions", "clicks", "spend", "sales7d", "purchases7d" } } };
        using var doc = await http.SendAsync(() => Request(HttpMethod.Post, "/reporting/reports", token, body), false, ct);
        return new(doc.RootElement.GetProperty("reportId").GetString()!, "PENDING");
    }
    public async Task<ReportTicket> PollAsync(string id, CancellationToken ct)
    {
        var token = await tokens.GetAsync(ct);
        using var doc = await http.SendAsync(() => Request(HttpMethod.Get, "/reporting/reports/" + Uri.EscapeDataString(id), token), true, ct);
        var r = doc.RootElement; var status = r.GetProperty("status").GetString()!;
        return new(id, status, status == "COMPLETED" ? new Uri(r.GetProperty("url").GetString()!) : null, "GZIP",
            r.TryGetProperty("generatedAt", out var generated) && generated.ValueKind == System.Text.Json.JsonValueKind.String ? generated.GetDateTime() : null);
    }
}
