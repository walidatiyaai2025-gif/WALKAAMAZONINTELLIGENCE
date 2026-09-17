using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Persistence;

public sealed class WalkaDbContext(DbContextOptions<WalkaDbContext> options) : DbContext(options)
{
    public DbSet<DailySales> Sales => Set<DailySales>();
    public DbSet<AdvertisingDailyMetric> Ads => Set<AdvertisingDailyMetric>();
    public DbSet<ReportArtifact> Artifacts => Set<ReportArtifact>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<AuditEvent> Audit => Set<AuditEvent>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<ListingSnapshot> Listings => Set<ListingSnapshot>();
    public DbSet<ChangeEvent> Changes => Set<ChangeEvent>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppSetting>().HasKey(x => x.Key);
        b.Entity<DailySales>().HasIndex(x => new { x.Account, x.Marketplace, x.Date, x.Asin }).IsUnique();
        b.Entity<AdvertisingDailyMetric>().HasIndex(x => new { x.Account, x.Marketplace, x.Profile, x.Date, x.AdId }).IsUnique();
        b.Entity<DailySales>().HasOne<ReportArtifact>().WithMany().HasForeignKey(x => x.ArtifactId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AdvertisingDailyMetric>().HasOne<ReportArtifact>().WithMany().HasForeignKey(x => x.ArtifactId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReportArtifact>().HasIndex(x => new { x.Source, x.Account, x.Marketplace, x.Profile, x.ReportType, x.Start, x.End, x.Sha256 }).IsUnique();
        b.Entity<ReportArtifact>().HasOne<SyncRun>().WithMany().HasForeignKey(x => x.SyncRunId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SyncRun>().HasIndex(x => new { x.Source, x.Scope, x.Date, x.Status });
        b.Entity<ListingSnapshot>().HasIndex(x => new { x.Account, x.Marketplace, x.Asin, x.ObservedUtc });
        b.Entity<ChangeEvent>().HasOne<ListingSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class DesignFactory : IDesignTimeDbContextFactory<WalkaDbContext>
{
    public WalkaDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<WalkaDbContext>()
        .UseSqlite("Data Source=walka.design.db").Options);
}
public sealed class DatabaseFactory(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public WalkaDbContext Create() => new(new DbContextOptionsBuilder<WalkaDbContext>()
        .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path, ForeignKeys = true, DefaultTimeout = 30 }.ToString()).Options);
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        await using var db = Create();
        await db.Database.MigrateAsync(ct);
    }
}
