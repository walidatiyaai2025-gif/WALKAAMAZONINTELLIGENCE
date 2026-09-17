using System.IO;
using System.Net.Http;
using System.Windows;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Infrastructure;
using WalkaAmazonIntelligence.Persistence;
using WalkaAmazonIntelligence.Worker;
namespace WalkaAmazonIntelligence.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? host;
    private Mutex? singleInstance;
    private bool ownsMutex;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
            builder.Configuration.AddEnvironmentVariables("WALKA_");
            var root = Path.GetFullPath(builder.Configuration["Storage:Root"] ?? "C:\\WALKA-Amazon");
            var mutexKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
            singleInstance = new Mutex(true, "Local\\Walka-" + mutexKey, out ownsMutex);
            if (!ownsMutex) { MessageBox.Show("WALKA is already running for this storage location."); Shutdown(1); return; }
            foreach (var folder in new[] { "Database", "Data", "RawReports", "Logs", "Backups", "BrowserProfile", "Exports", "Listings", "Images", "APlus", "Inventory", "Finance", "Returns" }) Directory.CreateDirectory(Path.Combine(root, folder));
            Log.Logger = new LoggerConfiguration().MinimumLevel.Information().Enrich.WithProperty("Version", "0.1.0")
                .WriteTo.File(Path.Combine(root, "Logs", "walka-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30).CreateLogger();
            builder.Services.AddSerilog();
            builder.Services.AddHttpClient("api", c => c.Timeout = TimeSpan.FromSeconds(60)).RemoveAllLoggers()
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            builder.Services.AddHttpClient("download", c => c.Timeout = TimeSpan.FromMinutes(5)).RemoveAllLoggers()
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            builder.Services.AddSingleton(new RuntimeInfo(root, builder.Environment.EnvironmentName));
            builder.Services.AddSingleton(new DatabaseFactory(Path.Combine(root, "Database", "walka.db")));
            builder.Services.AddSingleton<DashboardService>();
            builder.Services.AddSingleton<ISecretStore>(new DpapiSecretStore(Path.Combine(root, "Data", "Secrets")));
            builder.Services.AddSingleton<IReportImporter, ReportImporter>();
            builder.Services.AddSingleton(sp => new EvidenceArchive(sp.GetRequiredService<IHttpClientFactory>().CreateClient("download"), root));
            builder.Services.AddSingleton<SyncCoordinator>();
            builder.Services.AddSingleton(sp => new BackupService(sp.GetRequiredService<DatabaseFactory>(), Path.Combine(root, "Backups")));
            builder.Services.AddSingleton<MainViewModel>(); builder.Services.AddSingleton<MainWindow>();
            host = builder.Build(); await host.StartAsync();
            await host.Services.GetRequiredService<DatabaseFactory>().InitializeAsync();
            var vm = host.Services.GetRequiredService<MainViewModel>(); await vm.InitializeAsync();
            MainWindow = host.Services.GetRequiredService<MainWindow>(); MainWindow.Show();
            Log.Information("Application started in {Environment}", builder.Environment.EnvironmentName);
            if (e.Args.Contains("--smoke-test"))
            {
                await Dispatcher.InvokeAsync(() => MainWindow.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)MainWindow.ActualWidth, (int)MainWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(MainWindow);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(root, "Logs", "smoke.png"))) encoder.Save(file);
                File.WriteAllText(Path.Combine(root, "Logs", "smoke.ok"), "Window rendered; migrations applied; view model initialized.");
                Shutdown(0);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed: {ErrorType}", ex.GetType().Name);
            if (e.Args.Contains("--smoke-test")) Console.Error.WriteLine(ex);
            else MessageBox.Show("WALKA could not start. Check storage permissions and diagnostics. Error: " + ex.GetType().Name, "WALKA");
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        host?.Dispose(); Log.CloseAndFlush(); if (ownsMutex) singleInstance?.ReleaseMutex(); singleInstance?.Dispose(); base.OnExit(e);
    }
}
public sealed record RuntimeInfo(string StorageRoot, string Environment);
