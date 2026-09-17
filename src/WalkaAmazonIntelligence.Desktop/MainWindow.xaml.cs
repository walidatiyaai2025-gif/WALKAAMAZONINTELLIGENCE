using System.Windows;
namespace WalkaAmazonIntelligence.Desktop;
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
    private async void ProtectSecrets_Click(object sender, RoutedEventArgs e)
    {
        try { await ((MainViewModel)DataContext).ProtectSecretsAsync(SellerSecret.Password, SellerRefresh.Password, AdsSecret.Password, AdsRefresh.Password); }
        finally { SellerSecret.Clear(); SellerRefresh.Clear(); AdsSecret.Clear(); AdsRefresh.Clear(); }
    }
}
