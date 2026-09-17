using System.Net;
using System.Text;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
using WalkaAmazonIntelligence.Infrastructure;
using WalkaAmazonIntelligence.Connectors.AmazonSeller;
using WalkaAmazonIntelligence.Connectors.AmazonAds;
namespace WalkaAmazonIntelligence.Connectors.Tests;

public class ConnectorTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class Token : IAccessTokenProvider
    {
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("TEST_FIXTURE_TOKEN");
    }

    private sealed class RefreshingToken : IRefreshableAccessTokenProvider
    {
        private string value = "OLD";
        public int Invalidations { get; private set; }
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult(value);
        public Task InvalidateAsync(CancellationToken ct)
        {
            Invalidations++;
            value = "NEW";
            return Task.CompletedTask;
        }
    }

    private sealed class MemorySecrets(Dictionary<string, string> values) : ISecretStore
    {
        public Task SaveAsync(string name, string value, CancellationToken ct = default)
        {
            values[name] = value;
            return Task.CompletedTask;
        }
        public Task<string?> ReadAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(values.TryGetValue(name, out var value) ? value : null);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact] public async Task RetryIsBoundedAndUnauthorizedIsNotRetried()
    {
        var attempts = 0;
        var client = new HttpClient(new Handler(_ => { attempts++; return new(HttpStatusCode.TooManyRequests); }));
        var http = new AmazonHttp(client, (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<RemoteApiException>(() => http.SendAsync(() => new(HttpMethod.Get, "https://example.com"), true, default));
        Assert.Equal(4, attempts);
        attempts = 0;
        http = new(new HttpClient(new Handler(_ => { attempts++; return new(HttpStatusCode.Unauthorized); })), (_, _) => Task.CompletedTask);
        var auth = await Assert.ThrowsAsync<AuthenticationRequiredException>(() => http.SendAsync(() => new(HttpMethod.Get, "https://example.com"), true, default));
        Assert.Equal(401, auth.StatusCode);
        Assert.Equal(1, attempts);
    }

    [Fact] public async Task LwaProviderCachesTokenAndKeepsSecretsOutOfUri()
    {
        var requests = 0;
        var client = new HttpClient(new Handler(request =>
        {
            requests++;
            Assert.Equal("https://api.amazon.com/auth/o2/token", request.RequestUri!.ToString());
            Assert.DoesNotContain("REFRESH_SECRET", request.RequestUri.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("CLIENT_SECRET", request.RequestUri.ToString(), StringComparison.Ordinal);
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("grant_type=refresh_token", body, StringComparison.Ordinal);
            Assert.Contains("refresh_token=REFRESH_SECRET", body, StringComparison.Ordinal);
            Assert.Contains("client_secret=CLIENT_SECRET", body, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, "{\"access_token\":\"ACCESS_TOKEN\",\"expires_in\":3600}");
        }));
        var secrets = new MemorySecrets(new()
        {
            ["seller-refresh"] = "REFRESH_SECRET",
            ["seller-client-secret"] = "CLIENT_SECRET"
        });
        var provider = new LwaTokenProvider(client, secrets, "seller", "SELLER_CLIENT_ID");

        Assert.Equal("ACCESS_TOKEN", await provider.GetAsync(default));
        Assert.Equal("ACCESS_TOKEN", await provider.GetAsync(default));
        Assert.Equal(1, requests);
    }

    [Fact] public async Task SellerCapabilityProbeRefreshesExactlyOnceAfter401AndScopesMarketplace()
    {
        var tokens = new RefreshingToken();
        var attempts = 0;
        var http = new AmazonHttp(new HttpClient(new Handler(request =>
        {
            attempts++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/reports/2021-06-30/reports", request.RequestUri!.AbsolutePath);
            Assert.Contains("reportTypes=GET_SALES_AND_TRAFFIC_REPORT", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("marketplaceIds=ATVPDKIKX0DER", request.RequestUri.Query, StringComparison.Ordinal);
            var token = request.Headers.GetValues("x-amz-access-token").Single();
            return token == "OLD"
                ? new(HttpStatusCode.Unauthorized)
                : Json(HttpStatusCode.OK, "{\"reports\":[]}");
        })), (_, _) => Task.CompletedTask);

        await new SalesTrafficConnector(http, tokens, "NA")
            .ProbeCapabilitiesAsync(new("TEST_FIXTURE", "ATVPDKIKX0DER", "USD"), default);

        Assert.Equal(2, attempts);
        Assert.Equal(1, tokens.Invalidations);
    }

    [Fact] public async Task ForbiddenCapabilityDoesNotRefreshToken()
    {
        var tokens = new RefreshingToken();
        var http = new AmazonHttp(new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden))), (_, _) => Task.CompletedTask);
        var ex = await Assert.ThrowsAsync<AuthenticationRequiredException>(() =>
            new SalesTrafficConnector(http, tokens, "NA")
                .ProbeCapabilitiesAsync(new("TEST_FIXTURE", "ATVPDKIKX0DER", "USD"), default));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(0, tokens.Invalidations);
    }

    [Fact] public async Task SellerRequestUsesOfficialRouteAndDoesNotRetryCreation()
    {
        var attempts = 0;
        var postAttempts = 0;
        var http = new AmazonHttp(new HttpClient(new Handler(request =>
        {
            attempts++;
            Assert.True(request.Headers.Contains("x-amz-access-token"));
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/reports/2021-06-30/reports", request.RequestUri!.AbsolutePath);
                return Json(HttpStatusCode.OK, "{\"reports\":[]}");
            }
            postAttempts++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/reports/2021-06-30/reports", request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.ServiceUnavailable);
        })), (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<RemoteApiException>(() =>
            new SalesTrafficConnector(http, new Token(), "NA")
                .RequestAsync(new("TEST_FIXTURE", "US", "USD"), new(2026, 1, 1), default));
        Assert.Equal(2, attempts);
        Assert.Equal(1, postAttempts);
    }

    [Fact] public async Task AdsCapabilityProbeUsesReadOnlyProfilesWithoutScopeHeader()
    {
        var http = new AmazonHttp(new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v2/profiles", request.RequestUri!.AbsolutePath);
            Assert.Contains("accessLevel=view", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("apiProgram=report", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Equal("TEST_FIXTURE_TOKEN", request.Headers.Authorization?.Parameter);
            Assert.Equal("ADS_CLIENT_ID", request.Headers.GetValues("Amazon-Advertising-API-ClientId").Single());
            Assert.False(request.Headers.Contains("Amazon-Advertising-API-Scope"));
            return Json(HttpStatusCode.OK, "[{\"profileId\":123,\"currencyCode\":\"USD\",\"accountInfo\":{\"marketplaceStringId\":\"ATVPDKIKX0DER\"}}]");
        })), (_, _) => Task.CompletedTask);

        await new AdsReportConnector(http, new Token(), "NA", "ADS_CLIENT_ID", "123")
            .ProbeCapabilitiesAsync(new("TEST_FIXTURE", "ATVPDKIKX0DER", "USD", "123"), default);
    }

    [Fact] public async Task AdsCapabilityProbeFailsClosedWhenProfileIsNotReportEntitled()
    {
        var http = new AmazonHttp(new HttpClient(new Handler(_ =>
            Json(HttpStatusCode.OK, "[{\"profileId\":999,\"currencyCode\":\"USD\",\"accountInfo\":{\"marketplaceStringId\":\"ATVPDKIKX0DER\"}}]"))),
            (_, _) => Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<CapabilityUnavailableException>(() =>
            new AdsReportConnector(http, new Token(), "NA", "ADS_CLIENT_ID", "123")
                .ProbeCapabilitiesAsync(new("TEST_FIXTURE", "ATVPDKIKX0DER", "USD", "123"), default));
        Assert.Equal("ADS_REPORT_PROFILE_UNAVAILABLE", ex.Code);
    }

    [Fact] public async Task DpapiSecretStoreRoundTripsWithoutPlaintextAtRestOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "walka-dpapi-test-" + Guid.NewGuid().ToString("N"));
        const string secret = "TEST_FIXTURE_SECRET_VALUE";
        try
        {
            var store = new DpapiSecretStore(directory);
            await store.SaveAsync("seller-refresh", secret);
            Assert.Equal(secret, await store.ReadAsync("seller-refresh"));
            var raw = await File.ReadAllBytesAsync(Path.Combine(directory, "seller-refresh.protected"));
            Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact] public void AdsParserRejectsWrongDatesAndMissingMetrics()
    {
        const string json = """[{"date":"2026-01-01","campaignId":1,"adGroupId":2,"adId":3,"advertisedAsin":"B000000001","advertisedSku":"TEST_FIXTURE","impressions":100,"clicks":10,"spend":5,"sales7d":20,"purchases7d":1}]""";
        var parser = new AdsReportParser(); var scope = new DataScope("TEST_FIXTURE", "US", "USD", "1");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var rows = parser.Parse(input, scope, new(2026, 1, 1)); Assert.Equal(5, rows.Ads[0].Spend);
        input.Position = 0; Assert.Throws<InvalidDataException>(() => parser.Parse(input, scope, new(2026, 1, 2)));
        using var missing = new MemoryStream(Encoding.UTF8.GetBytes(json.Replace("\"spend\":5,", "")));
        Assert.Throws<KeyNotFoundException>(() => parser.Parse(missing, scope, new(2026, 1, 1)));
    }
}
