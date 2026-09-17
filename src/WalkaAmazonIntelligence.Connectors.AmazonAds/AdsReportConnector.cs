using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
using WalkaAmazonIntelligence.Infrastructure;
namespace WalkaAmazonIntelligence.Connectors.AmazonAds;

public sealed class AdsReportConnector(AmazonHttp http, IAccessTokenProvider tokens, string region, string clientId, string profile) : IReportConnector, ICapabilityProbe
{
    private readonly Uri endpoint = AmazonRegions.Ads(region);
    private readonly string normalizedClientId = clientId.Trim();
    private readonly string normalizedProfile = profile.Trim();
    public string Source => "Ads";
    public string ReportType => "spAdvertisedProduct";

    public async Task ValidateConfigurationAsync(DataScope scope, CancellationToken ct)
    {
        scope.Validate();
        if (string.IsNullOrWhiteSpace(normalizedProfile) ||
            !string.Equals(scope.Profile, normalizedProfile, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(normalizedClientId))
            throw new AuthenticationRequiredException();
        _ = await tokens.GetAsync(ct);
    }

    public async Task ProbeCapabilitiesAsync(DataScope scope, CancellationToken ct)
    {
        await ValidateConfigurationAsync(scope, ct);
        const string path = "/v2/profiles?accessLevel=view&apiProgram=report";
        using var doc = await AmazonAuthorization.SendAsync(
            http, tokens, token => Request(HttpMethod.Get, path, token, includeScope: false), true, ct);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Amazon Ads profiles capability response schema is invalid.");

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!TryProfileId(item, out var profileId) ||
                !string.Equals(profileId, normalizedProfile, StringComparison.Ordinal))
                continue;

            if (item.TryGetProperty("currencyCode", out var currency) &&
                currency.ValueKind == JsonValueKind.String &&
                !string.Equals(currency.GetString(), scope.Currency, StringComparison.OrdinalIgnoreCase))
                throw new CapabilityUnavailableException("ADS_PROFILE_CURRENCY_MISMATCH");

            if (item.TryGetProperty("accountInfo", out var accountInfo) &&
                accountInfo.ValueKind == JsonValueKind.Object &&
                accountInfo.TryGetProperty("marketplaceStringId", out var marketplace) &&
                marketplace.ValueKind == JsonValueKind.String &&
                !string.Equals(marketplace.GetString(), scope.Marketplace, StringComparison.Ordinal))
                throw new CapabilityUnavailableException("ADS_PROFILE_MARKETPLACE_MISMATCH");

            return;
        }

        throw new CapabilityUnavailableException("ADS_REPORT_PROFILE_UNAVAILABLE");
    }

    private static bool TryProfileId(JsonElement item, out string? value)
    {
        value = null;
        if (!item.TryGetProperty("profileId", out var profileId)) return false;
        value = profileId.ValueKind switch
        {
            JsonValueKind.String => profileId.GetString(),
            JsonValueKind.Number => profileId.GetRawText(),
            _ => null
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null, bool includeScope = true)
    {
        var request = new HttpRequestMessage(method, new Uri(endpoint, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Amazon-Advertising-API-ClientId", normalizedClientId);
        if (includeScope) request.Headers.Add("Amazon-Advertising-API-Scope", normalizedProfile);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
            request.Content.Headers.ContentType = new("application/vnd.createasyncreportrequest.v3+json");
        }
        return request;
    }

    public async Task<ReportTicket> RequestAsync(DataScope scope, DateOnly date, CancellationToken ct)
    {
        await ProbeCapabilitiesAsync(scope, ct);
        var body = new
        {
            name = $"WALKA {date:yyyy-MM-dd}", startDate = date.ToString("yyyy-MM-dd"), endDate = date.ToString("yyyy-MM-dd"),
            configuration = new
            {
                adProduct = "SPONSORED_PRODUCTS", groupBy = new[] { "advertiser" }, reportTypeId = ReportType,
                timeUnit = "DAILY", format = "GZIP_JSON",
                columns = new[] { "date", "campaignId", "adGroupId", "adId", "advertisedAsin", "advertisedSku", "impressions", "clicks", "spend", "sales7d", "purchases7d" }
            }
        };
        using var doc = await AmazonAuthorization.SendAsync(
            http, tokens, token => Request(HttpMethod.Post, "/reporting/reports", token, body), false, ct);
        if (!doc.RootElement.TryGetProperty("reportId", out var reportId) ||
            reportId.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(reportId.GetString()))
            throw new InvalidDataException("Amazon returned an empty Ads report identifier after report creation.");
        return new(reportId.GetString()!, "PENDING");
    }

    public async Task<ReportTicket> PollAsync(string id, CancellationToken ct)
    {
        using var doc = await AmazonAuthorization.SendAsync(
            http, tokens, token => Request(HttpMethod.Get, "/reporting/reports/" + Uri.EscapeDataString(id), token), true, ct);
        var r = doc.RootElement;
        var status = r.GetProperty("status").GetString()!;
        return new(id, status, status == "COMPLETED" ? new Uri(r.GetProperty("url").GetString()!) : null, "GZIP",
            r.TryGetProperty("generatedAt", out var generated) && generated.ValueKind == JsonValueKind.String ? generated.GetDateTime() : null);
    }
}
