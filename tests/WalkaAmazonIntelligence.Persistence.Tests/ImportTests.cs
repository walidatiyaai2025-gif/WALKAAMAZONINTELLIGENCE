using Microsoft.EntityFrameworkCore;
using WalkaAmazonIntelligence.Domain;
using WalkaAmazonIntelligence.Application;
namespace WalkaAmazonIntelligence.Persistence.Tests;

public class ImportTests
{
    private static readonly DateOnly ReportDate = new(2026, 1, 1);

    private static async Task<DatabaseFactory> Create()
    {
        // Test data stays under repository output folders, never system temp.
        var factory = new DatabaseFactory(Path.Combine(AppContext.BaseDirectory, "TestData", Guid.NewGuid().ToString("N"), "test.db"));
        await factory.InitializeAsync(); return factory;
    }

    private static ReportArtifact Artifact(string hash) => new()
    {
        Source = "Seller", ReportType = "TEST_SALES_REPORT", Account = "TEST_FIXTURE", Marketplace = "US",
        Start = ReportDate, End = ReportDate, Sha256 = hash
    };

    private static ReportArtifact AdsArtifact(string hash) => new()
    {
        Source = "Ads", ReportType = "TEST_ADS_REPORT", Account = "TEST_FIXTURE", Marketplace = "US", Profile = "PROFILE-1",
        Start = ReportDate, End = ReportDate, Sha256 = hash
    };

    private static ParsedReport Rows(decimal sales) => new(
        [new() { Account = "TEST_FIXTURE", Marketplace = "US", Asin = "B000000001", Currency = "USD", Date = ReportDate, Sales = sales }], []);

    private static ParsedReport AdsRows(decimal spend) => new([], [new()
    {
        Account = "TEST_FIXTURE", Marketplace = "US", Profile = "PROFILE-1", CampaignId = "C1", AdGroupId = "G1",
        AdId = "AD1", Asin = "B000000001", Sku = "SKU1", Currency = "USD", Date = ReportDate, Spend = spend, Sales7d = spend * 2
    }]);

    [Fact]
    public async Task ReimportIsIdempotentAndRevisionsReplace()
    {
        var factory = await Create(); var importer = new ReportImporter(factory);
        await importer.ImportAsync(Artifact("A"), Rows(10), default);
        await importer.ImportAsync(Artifact("A"), Rows(10), default);
        await importer.ImportAsync(Artifact("B"), Rows(15), default);
        await importer.ImportAsync(Artifact("A"), Rows(10), default);
        await using var db = factory.Create();
        Assert.Single(await db.Sales.ToListAsync()); Assert.Equal(15, (await db.Sales.SingleAsync()).Sales);
        Assert.Equal(2, await db.Artifacts.CountAsync()); Assert.Equal(2, await db.Audit.CountAsync());
    }

    [Fact]
    public async Task InvalidScopeCannotDeletePriorFacts()
    {
        var factory = await Create(); var importer = new ReportImporter(factory);
        await importer.ImportAsync(Artifact("A"), Rows(10), default);
        var bad = Rows(100); bad.Sales[0].Marketplace = "OTHER";
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(Artifact("B"), bad, default));
        await using var db = factory.Create(); Assert.Equal(10, (await db.Sales.SingleAsync()).Sales);
    }

    [Fact]
    public async Task SourceTypeMismatchIsRejectedBeforeAnyMutation()
    {
        var factory = await Create(); var importer = new ReportImporter(factory);
        await importer.ImportAsync(Artifact("A"), Rows(10), default);

        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(Artifact("B"), AdsRows(5), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(AdsArtifact("C"), Rows(20), default));

        await using var db = factory.Create();
        Assert.Equal(10, (await db.Sales.SingleAsync()).Sales);
        Assert.Empty(await db.Ads.ToListAsync());
        Assert.Single(await db.Artifacts.ToListAsync());
    }

    [Fact]
    public async Task FailedRevisionRollsBackPriorFactsAndArtifact()
    {
        var factory = await Create(); var importer = new ReportImporter(factory);
        await importer.ImportAsync(Artifact("A"), Rows(10), default);
        var badRevision = Artifact("B");
        badRevision.SyncRunId = Guid.NewGuid(); // Deliberately violates the artifact -> SyncRun foreign key.

        await Assert.ThrowsAsync<DbUpdateException>(() => importer.ImportAsync(badRevision, Rows(99), default));

        await using var db = factory.Create();
        Assert.Equal(10, (await db.Sales.SingleAsync()).Sales);
        Assert.Single(await db.Artifacts.ToListAsync());
        Assert.Single(await db.Audit.ToListAsync());
    }

    [Fact]
    public async Task MigrationIsCurrentAndDatabaseEnforcesForeignAndUniqueKeys()
    {
        var factory = await Create();
        await using (var db = factory.Create())
        {
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
            db.Sales.Add(new()
            {
                Account = "TEST_FIXTURE", Marketplace = "US", Asin = "B000000001", Currency = "USD", Date = ReportDate,
                ArtifactId = Guid.NewGuid()
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        var artifact = Artifact("SCHEMA"); artifact.Status = ImportStatus.Imported;
        await using (var db = factory.Create())
        {
            db.Artifacts.Add(artifact);
            db.Sales.Add(new()
            {
                Account = "TEST_FIXTURE", Marketplace = "US", Asin = "B000000001", Currency = "USD", Date = ReportDate,
                ArtifactId = artifact.Id
            });
            await db.SaveChangesAsync();
        }
        await using (var db = factory.Create())
        {
            db.Sales.Add(new()
            {
                Account = "TEST_FIXTURE", Marketplace = "US", Asin = "B000000001", Currency = "USD", Date = ReportDate,
                ArtifactId = artifact.Id
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task DuplicateKeysFailAndBackupIsReadable()
    {
        var factory = await Create(); var importer = new ReportImporter(factory);
        var row = Rows(10).Sales[0];
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(Artifact("A"), new([row, row], []), default));
        await importer.ImportAsync(Artifact("B"), Rows(10), default);
        var path = await new BackupService(factory, Path.Combine(Path.GetDirectoryName(factory.Path)!, "Backups")).CreateAsync();
        await using var backup = new DatabaseFactory(path).Create(); Assert.Equal(10, (await backup.Sales.SingleAsync()).Sales);
    }

    [Fact]
    public async Task SettingsAreCanonicalPersistedAuditedAndRejectSecretsOrUnsupportedPreferences()
    {
        var factory = await Create(); var service = new DashboardService(factory);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveSettingAsync("refresh_token", "secret"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveSettingAsync("language", "fr"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveSettingAsync("theme", "Blue"));

        await service.SaveSettingAsync("account", "TEST_FIXTURE");
        await service.SaveSettingAsync("currency", "usd");
        await service.SaveSettingAsync("region", "na");
        await service.SaveSettingAsync("language", "AR");
        await service.SaveSettingAsync("theme", "light");
        await service.SaveSettingAsync("language", "en");

        var settings = await service.SettingsAsync();
        Assert.Equal("TEST_FIXTURE", settings["account"]);
        Assert.Equal("USD", settings["currency"]);
        Assert.Equal("NA", settings["region"]);
        Assert.Equal("en", settings["language"]);
        Assert.Equal("Light", settings["theme"]);

        await using var db = factory.Create();
        Assert.Equal(5, await db.Settings.CountAsync());
        var audits = await db.Audit.AsNoTracking().Where(x => x.Action == "SETTING_CHANGED").ToListAsync();
        Assert.Equal(6, audits.Count);
        Assert.All(audits, audit => Assert.DoesNotContain("TEST_FIXTURE", audit.Detail));
        Assert.All(audits, audit => Assert.DoesNotContain("secret", audit.Detail, StringComparison.OrdinalIgnoreCase));
    }
}
