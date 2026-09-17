using System.Security.Cryptography;
using System.IO.Compression;
using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Infrastructure;

public sealed class EvidenceArchive(HttpClient downloads, string root)
{
    public async Task<ReportArtifact> DownloadAsync(Uri uri, string source, string reportType, DataScope scope, DateOnly date,
        Guid runId, DateTime? generated, CancellationToken ct)
    {
        if (uri.Scheme != "https" || uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Invalid report download URL.");
        var directory = Path.Combine(root, "RawReports", source); Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            using var response = await downloads.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new Application.RemoteApiException((int)response.StatusCode);
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long size = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    size += count; if (size > 512L * 1024 * 1024) throw new InvalidDataException("Report exceeds 512 MiB limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                if (size == 0) throw new InvalidDataException("Empty report download.");
            }
            string hash;
            await using (var file = File.OpenRead(temp)) hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
            var final = Path.Combine(directory, hash + ".raw");
            if (File.Exists(final)) File.Delete(temp); else File.Move(temp, final);
            return new() { Source = source, ReportType = reportType, Account = scope.Account, Marketplace = scope.Marketplace, Profile = scope.Profile,
                Start = date, End = date, Path = final, Sha256 = hash, Size = new FileInfo(final).Length, SyncRunId = runId, GeneratedUtc = generated };
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static Stream OpenJson(string path, string? compression)
    {
        var input = File.OpenRead(path);
        var first = input.ReadByte(); var second = input.ReadByte(); input.Position = 0;
        if (compression is not (null or "" or "GZIP")) { input.Dispose(); throw new InvalidDataException("Unsupported compression."); }
        return first == 0x1f && second == 0x8b ? new GZipStream(input, CompressionMode.Decompress) : input;
    }
}
