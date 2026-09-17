using Microsoft.Data.Sqlite;
namespace WalkaAmazonIntelligence.Persistence;

public sealed class BackupService(DatabaseFactory factory, string directory)
{
    public async Task<string> CreateAsync(int retain = 30, CancellationToken ct = default)
    {
        if (retain < 1) throw new ArgumentOutOfRangeException(nameof(retain));
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, $"walka-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.db");
        var temporary = path + ".partial";
        try
        {
            await using (var source = new SqliteConnection($"Data Source={factory.Path}"))
            await using (var destination = new SqliteConnection($"Data Source={temporary};Pooling=False"))
            {
                await source.OpenAsync(ct); await destination.OpenAsync(ct);
                source.BackupDatabase(destination);
                await using var check = destination.CreateCommand(); check.CommandText = "PRAGMA quick_check";
                if ((string?)await check.ExecuteScalarAsync(ct) != "ok") throw new InvalidDataException("Backup integrity check failed.");
            }
            File.Move(temporary, path);
            await using var db = factory.Create();
            db.Audit.Add(new() { Action = "BACKUP_CREATED", Detail = System.IO.Path.GetFileName(path) });
            await db.SaveChangesAsync(ct);
            foreach (var old in new DirectoryInfo(directory).GetFiles("walka-*.db").OrderByDescending(f => f.CreationTimeUtc).Skip(retain)) old.Delete();
            return path;
        }
        catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
    }
}
