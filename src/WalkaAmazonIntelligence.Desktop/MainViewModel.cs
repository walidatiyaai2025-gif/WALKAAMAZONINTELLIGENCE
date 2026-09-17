using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
using WalkaAmazonIntelligence.Infrastructure;
using WalkaAmazonIntelligence.Persistence;
using WalkaAmazonIntelligence.Connectors.AmazonSeller;
using WalkaAmazonIntelligence.Connectors.AmazonAds;
using WalkaAmazonIntelligence.Worker;
namespace WalkaAmazonIntelligence.Desktop;

public partial class MainViewModel(DashboardService dashboard, DatabaseFactory database, ISecretStore secrets,
    SyncCoordinator sync, BackupService backup, IHttpClientFactory http, RuntimeInfo runtime) : ObservableObject
{
    [ObservableProperty] private string account = "";
    [ObservableProperty] private string marketplace = "";
    [ObservableProperty] private string profile = "";
    [ObservableProperty] private string currency = "USD";
    [ObservableProperty] private string region = "NA";
    [ObservableProperty] private string clientId = "";
    [ObservableProperty] private DateTime startDate = DateTime.Today.AddDays(-30);
    [ObservableProperty] private DateTime endDate = DateTime.Today.AddDays(-1);
    [ObservableProperty] private DateTime reportDate = DateTime.Today.AddDays(-2);
    [ObservableProperty] private string status = "Ready. No sample data is loaded.";
    [ObservableProperty] private string sellerStatus = "NOT_CONFIGURED";
    [ObservableProperty] private string adsStatus = "NOT_CONFIGURED";
    [ObservableProperty] private string salesValue = "—";
    [ObservableProperty] private string spendValue = "—";
    [ObservableProperty] private string acosValue = "—";
    [ObservableProperty] private string coverageValue = "No imports";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private FlowDirection direction = FlowDirection.LeftToRight;
    public string EnvironmentLabel => runtime.Environment.ToUpperInvariant() + "  •  READ ONLY";
    public string StoragePath => runtime.StorageRoot;
    public string Version => "0.1.0";
    public ObservableCollection<DashboardRow> Products { get; } = [];
    public ObservableCollection<SyncRun> Runs { get; } = [];
    public ObservableCollection<ReportArtifact> Artifacts { get; } = [];
    private DataScope Scope() => new(Account.Trim(), Marketplace.Trim(), Currency.Trim().ToUpperInvariant(), Profile.Trim());
    public async Task InitializeAsync()
    {
        var settings = await dashboard.SettingsAsync();
        Account = settings.GetValueOrDefault("account", ""); Marketplace = settings.GetValueOrDefault("marketplace", "");
        Profile = settings.GetValueOrDefault("profile", ""); Currency = settings.GetValueOrDefault("currency", "USD");
        Region = settings.GetValueOrDefault("region", "NA"); ClientId = settings.GetValueOrDefault("clientId", "");
        SetTheme(settings.GetValueOrDefault("theme", "Dark")); SetLanguage(settings.GetValueOrDefault("language", "en"));
        await RefreshAsync();
    }
    private async Task Guard(Func<Task> action)
    {
        if (Busy) return; Busy = true;
        try { await action(); }
        catch (Exception ex)
        {
            Status = ex is AuthenticationRequiredException ? "AUTH_REQUIRED: supply valid credentials and account authorization." :
                ex is ArgumentException ? ex.Message : "Operation failed: " + ex.GetType().Name + ". Inspect sync and evidence status.";
            Log.Warning("Operation failed: {ErrorType}", ex.GetType().Name);
        }
        finally { Busy = false; }
    }
    [RelayCommand] private Task SaveAsync() => Guard(async () =>
    {
        Scope().Validate(); _ = AmazonRegions.Seller(Region);
        foreach (var item in new Dictionary<string, string> { ["account"] = Account.Trim(), ["marketplace"] = Marketplace.Trim(), ["profile"] = Profile.Trim(),
            ["currency"] = Currency.Trim().ToUpperInvariant(), ["region"] = Region, ["clientId"] = ClientId.Trim() }) await dashboard.SaveSettingAsync(item.Key, item.Value);
        Status = "Settings saved. Connection is verified only after an authorized report request.";
    });
    [RelayCommand] private Task RefreshAsync() => Guard(RefreshCoreAsync);
    private async Task RefreshCoreAsync()
    {
        Products.Clear();
        if (!string.IsNullOrWhiteSpace(Account) && !string.IsNullOrWhiteSpace(Marketplace))
            foreach (var row in await dashboard.ReadAsync(Scope(), new(DateOnly.FromDateTime(StartDate), DateOnly.FromDateTime(EndDate)))) Products.Add(row);
        var saleRows = Products.Where(r => r.Sales.HasValue).ToList(); var adRows = Products.Where(r => r.AdSpend.HasValue).ToList();
        SalesValue = saleRows.Count > 0 ? $"{saleRows.Sum(r => r.Sales):N2} {Currency}" : "—";
        SpendValue = adRows.Count > 0 ? $"{adRows.Sum(r => r.AdSpend):N2} {Currency}" : "—";
        AcosValue = Metrics.Acos(adRows.Count > 0 ? adRows.Sum(r => r.AdSpend) : null, adRows.Count > 0 ? adRows.Sum(r => r.AdSales) : null)?.ToString("P1") ?? "—";
        CoverageValue = Products.Count > 0 ? Products.Count + " ASINs · imported dates" : "No imports";
        await using var db = database.Create();
        Runs.Clear(); foreach (var run in await db.SyncRuns.AsNoTracking().OrderByDescending(r => r.StartedUtc).Take(100).ToListAsync()) Runs.Add(run);
        Artifacts.Clear(); foreach (var artifact in await db.Artifacts.AsNoTracking().OrderByDescending(r => r.DownloadedUtc).Take(100).ToListAsync()) Artifacts.Add(artifact);
    }
    public Task ProtectSecretsAsync(string sellerSecret, string sellerRefresh, string adsSecret, string adsRefresh) => Guard(async () =>
    {
        foreach (var item in new Dictionary<string, string> { ["seller-client-secret"] = sellerSecret, ["seller-refresh"] = sellerRefresh,
            ["ads-client-secret"] = adsSecret, ["ads-refresh"] = adsRefresh })
            if (!string.IsNullOrWhiteSpace(item.Value)) await secrets.SaveAsync(item.Key, item.Value);
        Status = "Entered credentials protected for this Windows user. Existing values are kept for blank fields.";
    });
    [RelayCommand] private Task SellerSyncAsync() => RunSyncAsync(false);
    [RelayCommand] private Task AdsSyncAsync() => RunSyncAsync(true);
    private Task RunSyncAsync(bool ads) => Guard(async () =>
    {
        if (ads) AdsStatus = "CONNECTING"; else SellerStatus = "CONNECTING";
        try
        {
            var client = http.CreateClient("api"); var tokens = new LwaTokenProvider(client, secrets, ads ? "ads" : "seller", ClientId.Trim());
            IReportConnector connector = ads ? new AdsReportConnector(new(client), tokens, Region, ClientId.Trim(), Profile.Trim()) : new SalesTrafficConnector(new(client), tokens, Region);
            IReportParser parser = ads ? new AdsReportParser() : new SalesTrafficParser();
            Status = await sync.StepAsync(connector, parser, Scope(), DateOnly.FromDateTime(ReportDate));
            if (ads) AdsStatus = "CONNECTED"; else SellerStatus = "CONNECTED";
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            var state = ex is AuthenticationRequiredException ? "AUTH_REQUIRED" : "ERROR";
            if (ads) AdsStatus = state; else SellerStatus = state;
            await RefreshCoreAsync(); throw;
        }
    });
    [RelayCommand] private Task BackupAsync() => Guard(async () => { _ = await backup.CreateAsync(); Status = "Database backup created and integrity checked."; });
    private string theme = "Dark";
    private string language = "en";
    private void SetTheme(string value)
    {
        theme = value == "Light" ? "Light" : "Dark";
        System.Windows.Application.Current.Resources.MergedDictionaries[0] = new() { Source = new Uri($"Resources/{theme}.xaml", UriKind.Relative) };
    }
    private void SetLanguage(string value)
    {
        language = value == "ar" ? "ar" : "en";
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        if (dictionaries.Count > 3) dictionaries.RemoveAt(3);
        if (language == "ar") dictionaries.Add(new() { Source = new Uri("Resources/Strings.ar.xaml", UriKind.Relative) });
        Direction = language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }
    [RelayCommand] private Task ThemeAsync() => Guard(async () => { SetTheme(theme == "Dark" ? "Light" : "Dark"); await dashboard.SaveSettingAsync("theme", theme); });
    [RelayCommand] private Task LanguageAsync() => Guard(async () => { SetLanguage(language == "en" ? "ar" : "en"); await dashboard.SaveSettingAsync("language", language); });
}
