using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WalkaAmazonIntelligence.Application;
namespace WalkaAmazonIntelligence.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(string directory) : ISecretStore
{
    private string PathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Invalid secret name.");
        return Path.Combine(directory, name + ".protected");
    }

    public async Task SaveAsync(string name, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Secret value cannot be empty.", nameof(value));

        var path = PathFor(name);
        Directory.CreateDirectory(directory);
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var temp = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(temp, protectedBytes, ct);
                File.Move(temp, path, true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public async Task<string?> ReadAsync(string name, CancellationToken ct = default)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return null;
        var bytes = ProtectedData.Unprotect(await File.ReadAllBytesAsync(path, ct), null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

public sealed class LwaTokenProvider(HttpClient client, ISecretStore secrets, string prefix, string clientId)
    : IRefreshableAccessTokenProvider
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string normalizedClientId = clientId.Trim();
    private string? token;
    private DateTime expires;

    public async Task<string> GetAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (token is not null && expires > DateTime.UtcNow.AddMinutes(2)) return token;
            token = null;
            expires = default;

            var refresh = await secrets.ReadAsync(prefix + "-refresh", ct);
            var secret = await secrets.ReadAsync(prefix + "-client-secret", ct);
            if (string.IsNullOrWhiteSpace(normalizedClientId) || string.IsNullOrWhiteSpace(refresh) || string.IsNullOrWhiteSpace(secret))
                throw new AuthenticationRequiredException();

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refresh,
                ["client_id"] = normalizedClientId,
                ["client_secret"] = secret
            });
            using var response = await client.PostAsync("https://api.amazon.com/auth/o2/token", content, ct);
            if ((int)response.StatusCode is 400 or 401 or 403)
                throw new AuthenticationRequiredException((int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
                throw new RemoteApiException((int)response.StatusCode);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            if (!doc.RootElement.TryGetProperty("access_token", out var accessToken) ||
                accessToken.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(accessToken.GetString()) ||
                !doc.RootElement.TryGetProperty("expires_in", out var expiresIn) ||
                !expiresIn.TryGetInt32(out var lifetimeSeconds) ||
                lifetimeSeconds <= 0)
                throw new AuthenticationRequiredException();

            token = accessToken.GetString()!;
            expires = DateTime.UtcNow.AddSeconds(lifetimeSeconds);
            return token;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task InvalidateAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            token = null;
            expires = default;
        }
        finally
        {
            gate.Release();
        }
    }
}

public static class AmazonAuthorization
{
    public static async Task<JsonDocument> SendAsync(
        AmazonHttp http,
        IAccessTokenProvider tokens,
        Func<string, HttpRequestMessage> create,
        bool safeToRetry,
        CancellationToken ct)
    {
        var token = await tokens.GetAsync(ct);
        try
        {
            return await http.SendAsync(() => create(token), safeToRetry, ct);
        }
        catch (AuthenticationRequiredException ex)
            when (ex.StatusCode == 401 && tokens is IRefreshableAccessTokenProvider refreshable)
        {
            // HTTP 401 means Amazon rejected the request before authorization. Refresh exactly once.
            // Ambiguous network/5xx report-creation outcomes remain fail-closed in orchestration.
            await refreshable.InvalidateAsync(ct);
            var refreshed = await tokens.GetAsync(ct);
            return await http.SendAsync(() => create(refreshed), safeToRetry, ct);
        }
    }
}
