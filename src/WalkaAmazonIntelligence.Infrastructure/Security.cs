using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
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
        var path = PathFor(name); Directory.CreateDirectory(directory);
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var temp = path + "." + Guid.NewGuid().ToString("N");
            try { await File.WriteAllBytesAsync(temp, protectedBytes, ct); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public async Task<string?> ReadAsync(string name, CancellationToken ct = default)
    {
        var path = PathFor(name); if (!File.Exists(path)) return null;
        var bytes = ProtectedData.Unprotect(await File.ReadAllBytesAsync(path, ct), null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
public sealed class LwaTokenProvider(HttpClient client, ISecretStore secrets, string prefix, string clientId) : IAccessTokenProvider
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? token;
    private DateTime expires;
    public async Task<string> GetAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (token is not null && expires > DateTime.UtcNow.AddMinutes(2)) return token;
            var refresh = await secrets.ReadAsync(prefix + "-refresh", ct);
            var secret = await secrets.ReadAsync(prefix + "-client-secret", ct);
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(refresh) || string.IsNullOrWhiteSpace(secret)) throw new AuthenticationRequiredException();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = clientId, ["client_secret"] = secret });
            using var response = await client.PostAsync("https://api.amazon.com/auth/o2/token", content, ct);
            if ((int)response.StatusCode is 400 or 401 or 403) throw new AuthenticationRequiredException();
            if (!response.IsSuccessStatusCode) throw new RemoteApiException((int)response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            token = doc.RootElement.GetProperty("access_token").GetString() ?? throw new AuthenticationRequiredException();
            expires = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32());
            return token;
        }
        finally { gate.Release(); }
    }
}
