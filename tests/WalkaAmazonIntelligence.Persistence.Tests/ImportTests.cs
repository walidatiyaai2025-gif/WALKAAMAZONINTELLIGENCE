using Microsoft.EntityFrameworkCore;
using WalkaAmazonIntelligence.Domain;
using WalkaAmazonIntelligence.Application;
namespace WalkaAmazonIntelligence.Persistence.Tests;
public class ImportTests
{
    private static async Task<DatabaseFactory> Create()
    {
        // Test data stays under repository output folders, never system temp.
        var factory = new DatabaseFactory(Path.Combine(AppContext.BaseDirectory, "TestData", Guid.NewGuid().ToString("N"), "test.db"));
        await factory.InitializeAsync(); return factory;
    }
    private static ReportArtifact Artifact(string hash) => new() { Source = "Seller", Account = "TEST_FIXTURE", Marketplace = "US", Start = new(2026, 1, 1), End = new(2026, 1, 1), Sha256 = hash };
    private static ParsedReport Rows(decimal sales) => new([new() { Account = "TEST_FIXTURE", Marketplace = "US", Asin = "B000000001", Currency = "USD", Date = new(2026, 1, 1), Sales = sales }], []);
    [Fact] public async Task ReimportIsIdempotentAndRevisionsReplace()
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
    [Fact] public async Task InvalidScopeCannotDeletePriorFacts()
    {
        var factory = await Create(); var importer = new ReportImporter(factory);
        await importer.ImportAsync(Artifact("A"), Rows(10), default);
        var bad = Rows(100); bad.Sales[0].Marketplace = "OTHER";
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(Artifact("B"), bad, default));
        await using var db = factory.Create(); Assert.Equal(10, (await db.Sales.SingleAsync()).Sales);
    }
    [Fact] public async Task DuplicateKeysFailAndBackupIsReadable()
    {
        var factory = await Create(); var importer = new ReportImporter(factory);
        var row = Rows(10).Sales[0];
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(Artifact("A"), new([row, row], []), default));
        await importer.ImportAsync(Artifact("B"), Rows(10), default);
        var path = await new BackupService(factory, Path.Combine(Path.GetDirectoryName(factory.Path)!, "Backups")).CreateAsync();
        await using var backup = new DatabaseFactory(path).Create(); Assert.Equal(10, (await backup.Sales.SingleAsync()).Sales);
    }
    [Fact] public async Task SettingsRejectSecretsAndPersistAllowedValues()
    {
        var factory = await Create(); var service = new DashboardService(factory);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveSettingAsync("refresh_token", "secret"));
        await service.SaveSettingAsync("currency", "USD"); Assert.Equal("USD", (await service.SettingsAsync())["currency"]);
    }
}
