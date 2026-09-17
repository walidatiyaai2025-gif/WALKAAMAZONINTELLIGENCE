using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Markup;
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
    [ObservableProperty] private string sellerClientId = "";
    [ObservableProperty] private string adsClientId = "";
    [ObservableProperty] private DateTime startDate = DateTime.Today.AddDays(-30);
    [ObservableProperty] private DateTime endDate = DateTime.Today.AddDays(-1);
    [ObservableProperty] private DateTime reportDate = DateTime.Today.AddDays(-2);
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string sellerStatus = "NOT_CONFIGURED";
    [ObservableProperty] private string adsStatus = "NOT_CONFIGURED";
    [ObservableProperty] private string salesValue = "—";
    [ObservableProperty] private string spendValue = "—";
    [ObservableProperty] private string acosValue = "—";
    [ObservableProperty] private string coverageValue = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private FlowDirection direction = FlowDirection.LeftToRight;
    [ObservableProperty] private XmlLanguage uiLanguage = XmlLanguage.GetLanguage("en-US");

    public string EnvironmentLabel => runtime.Environment.ToUpperInvariant() + "  •  " + L("ReadOnly");
    public string StoragePath => runtime.StorageRoot;
    public string Version => "0.1.0";
    public ObservableCollection<DashboardRow> Products { get; } = [];
    public ObservableCollection<SyncRun> Runs { get; } = [];
    public ObservableCollection<ReportArtifact> Artifacts { get; } = [];

    private static string L(string key) => System.Windows.Application.Current.TryFindResource(key)?.ToString() ?? key;
    private DataScope Scope() => new(Account.Trim(), Marketplace.Trim(), Currency.Trim().ToUpperInvariant(), Profile.Trim());

    public async Task InitializeAsync()
    {
        var settings = await dashboard.SettingsAsync();
        Account = settings.GetValueOrDefault("account", ""); Marketplace = settings.GetValueOrDefault("marketplace", "");
        Profile = settings.GetValueOrDefault("profile", ""); Currency = settings.GetValueOrDefault("currency", "USD");
        Region = settings.GetValueOrDefault("region", "NA");
        var legacyClientId = settings.GetValueOrDefault("clientId", "");
        SellerClientId = settings.GetValueOrDefault("sellerClientId", legacyClientId);
        AdsClientId = settings.GetValueOrDefault("adsClientId", legacyClientId);
        SetTheme(settings.GetValueOrDefault("theme", "Dark"));
        SetLanguage(settings.GetValueOrDefault("language", "en"));
        Status = L("StatusReady");
        await RefreshAsync();
    }

    private async Task Guard(Func<Task> action)
    {
        if (Busy) return;
        Busy = true;
        try { await action(); }
        catch (Exception ex)
        {
            Status = ex is AuthenticationRequiredException ? L("StatusAuthRequired") :
                ex is CapabilityUnavailableException capability ? string.Format(CultureInfo.CurrentCulture, L("StatusCapabilityUnavailable"), capability.Code) :
                ex is ArgumentException ? ex.Message :
                string.Format(CultureInfo.CurrentCulture, L("StatusOperationFailed"), ex.GetType().Name);
            Log.Warning("Operation failed: {ErrorType}", ex.GetType().Name);
        }
        finally { Busy = false; }
    }

    [RelayCommand] private Task SaveAsync() => Guard(async () =>
    {
        Scope().Validate();
        _ = AmazonRegions.Seller(Region);
        foreach (var item in new Dictionary<string, string>
        {
            ["account"] = Account.Trim(), ["marketplace"] = Marketplace.Trim(), ["profile"] = Profile.Trim(),
            ["currency"] = Currency.Trim().ToUpperInvariant(), ["region"] = Region.Trim().ToUpperInvariant(),
            ["sellerClientId"] = SellerClientId.Trim(), ["adsClientId"] = AdsClientId.Trim()
        })
            await dashboard.SaveSettingAsync(item.Key, item.Value);
        Status = L("StatusSettingsSaved");
    });

    [RelayCommand] private Task RefreshAsync() => Guard(RefreshCoreAsync);

    private async Task RefreshCoreAsync()
    {
        Products.Clear();
        if (!string.IsNullOrWhiteSpace(Account) && !string.IsNullOrWhiteSpace(Marketplace))
            foreach (var row in await dashboard.ReadAsync(Scope(), new(DateOnly.FromDateTime(StartDate), DateOnly.FromDateTime(EndDate))))
                Products.Add(row);

        var saleRows = Products.Where(r => r.Sales.HasValue).ToList();
        var adRows = Products.Where(r => r.AdSpend.HasValue).ToList();
        SalesValue = saleRows.Count > 0 ? $"{saleRows.Sum(r => r.Sales):N2} {Currency}" : "—";
        SpendValue = adRows.Count > 0 ? $"{adRows.Sum(r => r.AdSpend):N2} {Currency}" : "—";
        AcosValue = Metrics.Acos(adRows.Count > 0 ? adRows.Sum(r => r.AdSpend) : null,
            adRows.Count > 0 ? adRows.Sum(r => r.AdSales) : null)?.ToString("P1", CultureInfo.CurrentCulture) ?? "—";
        CoverageValue = Products.Count > 0
            ? string.Format(CultureInfo.CurrentCulture, L("CoverageAsins"), Products.Count)
            : L("CoverageNoImports");

        await using var db = database.Create();
        Runs.Clear();
        foreach (var run in await db.SyncRuns.AsNoTracking().OrderByDescending(r => r.StartedUtc).Take(100).ToListAsync())
            Runs.Add(run);
        Artifacts.Clear();
        foreach (var artifact in await db.Artifacts.AsNoTracking().OrderByDescending(r => r.DownloadedUtc).Take(100).ToListAsync())
            Artifacts.Add(artifact);
    }

    public Task ProtectSecretsAsync(string sellerSecret, string sellerRefresh, string adsSecret, string adsRefresh) => Guard(async () =>
    {
        foreach (var item in new Dictionary<string, string>
        {
            ["seller-client-secret"] = sellerSecret, ["seller-refresh"] = sellerRefresh,
            ["ads-client-secret"] = adsSecret, ["ads-refresh"] = adsRefresh
        })
            if (!string.IsNullOrWhiteSpace(item.Value))
                await secrets.SaveAsync(item.Key, item.Value);
        Status = L("StatusSecretsProtected");
    });

    private (IReportConnector Connector, ICapabilityProbe Capability) CreateConnector(bool ads)
    {
        var client = http.CreateClient("api");
        var clientId = ads ? AdsClientId.Trim() : SellerClientId.Trim();
        var tokens = new LwaTokenProvider(client, secrets, ads ? "ads" : "seller", clientId);
        if (ads)
        {
            var connector = new AdsReportConnector(new(client), tokens, Region.Trim().ToUpperInvariant(), clientId, Profile.Trim());
            return (connector, connector);
        }
        else
        {
            var connector = new SalesTrafficConnector(new(client), tokens, Region.Trim().ToUpperInvariant());
            return (connector, connector);
        }
    }

    private void SetConnectionState(bool ads, string state)
    {
        if (ads) AdsStatus = state; else SellerStatus = state;
    }

    private async Task RecordConnectionOutcomeAsync(string source, string state, string code)
    {
        try { await dashboard.RecordConnectionAsync(source, state, code); }
        catch (Exception ex) { Log.Warning("Connection audit failed: {ErrorType}", ex.GetType().Name); }
    }

    [RelayCommand] private Task TestSellerConnectionAsync() => TestConnectionAsync(false);
    [RelayCommand] private Task TestAdsConnectionAsync() => TestConnectionAsync(true);

    private Task TestConnectionAsync(bool ads) => Guard(async () =>
    {
        SetConnectionState(ads, "CONNECTING");
        var (connector, capability) = CreateConnector(ads);
        try
        {
            await capability.ProbeCapabilitiesAsync(Scope(), CancellationToken.None);
            SetConnectionState(ads, "CONNECTED");
            await RecordConnectionOutcomeAsync(connector.Source, "CONNECTED", "REPORTING_AVAILABLE");
            Status = string.Format(CultureInfo.CurrentCulture, L("StatusConnectionVerified"), connector.Source);
        }
        catch (CapabilityUnavailableException ex)
        {
            SetConnectionState(ads, "DEGRADED");
            await RecordConnectionOutcomeAsync(connector.Source, "DEGRADED", ex.Code);
            throw;
        }
        catch (AuthenticationRequiredException ex)
        {
            SetConnectionState(ads, "AUTH_REQUIRED");
            await RecordConnectionOutcomeAsync(connector.Source, "AUTH_REQUIRED", ex.StatusCode is int statusCode ? $"HTTP_{statusCode}" : "AUTH_REQUIRED");
            throw;
        }
        catch
        {
            SetConnectionState(ads, "ERROR");
            await RecordConnectionOutcomeAsync(connector.Source, "ERROR", "CONNECTOR_ERROR");
            throw;
        }
    });

    [RelayCommand] private Task SellerSyncAsync() => RunSyncAsync(false);
    [RelayCommand] private Task AdsSyncAsync() => RunSyncAsync(true);

    private Task RunSyncAsync(bool ads) => Guard(async () =>
    {
        SetConnectionState(ads, "CONNECTING");
        var (connector, _) = CreateConnector(ads);
        try
        {
            IReportParser parser = ads ? new AdsReportParser() : new SalesTrafficParser();
            Status = await sync.StepAsync(connector, parser, Scope(), DateOnly.FromDateTime(ReportDate));
            SetConnectionState(ads, "CONNECTED");
            await RecordConnectionOutcomeAsync(connector.Source, "CONNECTED", "SYNC_AUTHORIZED");
            await RefreshCoreAsync();
        }
        catch (CapabilityUnavailableException ex)
        {
            SetConnectionState(ads, "DEGRADED");
            await RecordConnectionOutcomeAsync(connector.Source, "DEGRADED", ex.Code);
            await RefreshCoreAsync();
            throw;
        }
        catch (AuthenticationRequiredException ex)
        {
            SetConnectionState(ads, "AUTH_REQUIRED");
            await RecordConnectionOutcomeAsync(connector.Source, "AUTH_REQUIRED", ex.StatusCode is int statusCode ? $"HTTP_{statusCode}" : "AUTH_REQUIRED");
            await RefreshCoreAsync();
            throw;
        }
        catch
        {
            SetConnectionState(ads, "ERROR");
            await RecordConnectionOutcomeAsync(connector.Source, "ERROR", "CONNECTOR_ERROR");
            await RefreshCoreAsync();
            throw;
        }
    });

    [RelayCommand] private Task BackupAsync() => Guard(async () =>
    {
        _ = await backup.CreateAsync();
        Status = L("StatusBackupCreated");
    });

    private string theme = "Dark";
    private string language = "en";

    private static void ReplaceMergedDictionary(Func<ResourceDictionary, bool> match, string relativeSource)
    {
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(match);
        var index = current is null ? dictionaries.Count : dictionaries.IndexOf(current);
        if (current is not null) dictionaries.Remove(current);
        dictionaries.Insert(index, new() { Source = new Uri(relativeSource, UriKind.Relative) });
    }

    private void SetTheme(string value)
    {
        theme = string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        ReplaceMergedDictionary(
            d => d.Source?.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) == true ||
                 d.Source?.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) == true,
            $"Resources/{theme}.xaml");
    }

    private void SetLanguage(string value)
    {
        language = string.Equals(value, "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
        ReplaceMergedDictionary(
            d => d.Source?.OriginalString.EndsWith("Strings.en.xaml", StringComparison.OrdinalIgnoreCase) == true ||
                 d.Source?.OriginalString.EndsWith("Strings.ar.xaml", StringComparison.OrdinalIgnoreCase) == true,
            $"Resources/Strings.{language}.xaml");

        var culture = CultureInfo.GetCultureInfo(language == "ar" ? "ar-KW" : "en-US");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        UiLanguage = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        Direction = language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        OnPropertyChanged(nameof(EnvironmentLabel));

        CoverageValue = Products.Count > 0
            ? string.Format(CultureInfo.CurrentCulture, L("CoverageAsins"), Products.Count)
            : L("CoverageNoImports");
    }

    [RelayCommand] private Task ThemeAsync() => Guard(async () =>
    {
        SetTheme(theme == "Dark" ? "Light" : "Dark");
        await dashboard.SaveSettingAsync("theme", theme);
        Status = L("StatusThemeChanged");
    });

    [RelayCommand] private Task LanguageAsync() => Guard(async () =>
    {
        SetLanguage(language == "en" ? "ar" : "en");
        await dashboard.SaveSettingAsync("language", language);
        Status = L("StatusLanguageChanged");
    });
}