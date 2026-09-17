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
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request)); }
    private sealed class Token : IAccessTokenProvider { public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("TEST_FIXTURE_TOKEN"); }
    [Fact] public async Task RetryIsBoundedAndUnauthorizedIsNotRetried()
    {
        var attempts = 0;
        var client = new HttpClient(new Handler(_ => { attempts++; return new(HttpStatusCode.TooManyRequests); }));
        var http = new AmazonHttp(client, (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<RemoteApiException>(() => http.SendAsync(() => new(HttpMethod.Get, "https://example.com"), true, default));
        Assert.Equal(4, attempts);
        attempts = 0;
        http = new(new HttpClient(new Handler(_ => { attempts++; return new(HttpStatusCode.Unauthorized); })), (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => http.SendAsync(() => new(HttpMethod.Get, "https://example.com"), true, default));
        Assert.Equal(1, attempts);
    }
    [Fact] public async Task SellerRequestUsesOfficialRouteAndDoesNotRetryCreation()
    {
        var attempts = 0;
        var http = new AmazonHttp(new HttpClient(new Handler(r =>
        {
            attempts++; Assert.Equal("/reports/2021-06-30/reports", r.RequestUri!.AbsolutePath);
            Assert.True(r.Headers.Contains("x-amz-access-token")); return new(HttpStatusCode.ServiceUnavailable);
        })), (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<RemoteApiException>(() => new SalesTrafficConnector(http, new Token(), "NA").RequestAsync(new("TEST_FIXTURE", "US", "USD"), new(2026, 1, 1), default));
        Assert.Equal(1, attempts);
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
