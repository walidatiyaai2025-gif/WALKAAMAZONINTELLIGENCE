using System.Net;
using System.Text.Json;
using WalkaAmazonIntelligence.Application;
namespace WalkaAmazonIntelligence.Infrastructure;

public sealed class AmazonHttp(HttpClient client, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? Task.Delay;
    public async Task<JsonDocument> SendAsync(Func<HttpRequestMessage> create, bool safeToRetry, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = create();
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new AuthenticationRequiredException();
                if (safeToRetry && attempt < 3 && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500))
                {
                    var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                    var duration = retry ?? TimeSpan.FromSeconds(Math.Pow(2, attempt) + Random.Shared.NextDouble());
                    // Do not retry earlier than a long server delay; let orchestration reschedule instead.
                    if (duration > TimeSpan.FromMinutes(5)) throw new RemoteApiException((int)response.StatusCode);
                    await wait(duration < TimeSpan.Zero ? TimeSpan.Zero : duration, ct); continue;
                }
                if (!response.IsSuccessStatusCode) throw new RemoteApiException((int)response.StatusCode);
                return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            }
            catch (HttpRequestException) when (safeToRetry && attempt < 3)
            { await wait(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && safeToRetry && attempt < 3)
            { await wait(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct); }
        }
    }
}
public static class AmazonRegions
{
    public static Uri Seller(string region) => new(region switch
    { "NA" => "https://sellingpartnerapi-na.amazon.com", "EU" => "https://sellingpartnerapi-eu.amazon.com", "FE" => "https://sellingpartnerapi-fe.amazon.com", _ => throw new ArgumentException("Region must be NA, EU or FE.") });
    public static Uri Ads(string region) => new(region switch
    { "NA" => "https://advertising-api.amazon.com", "EU" => "https://advertising-api-eu.amazon.com", "FE" => "https://advertising-api-fe.amazon.com", _ => throw new ArgumentException("Region must be NA, EU or FE.") });
}
