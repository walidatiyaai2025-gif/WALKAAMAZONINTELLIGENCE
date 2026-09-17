$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$refs = @{
 'Application'=@('Domain'); 'Analytics'=@('Domain'); 'Persistence'=@('Domain','Application');
 'Infrastructure'=@('Application'); 'Connectors.AmazonSeller'=@('Application','Infrastructure');
 'Connectors.AmazonAds'=@('Application','Infrastructure'); 'Connectors.Browser'=@('Application');
 'Worker'=@('Application','Persistence','Infrastructure','Connectors.AmazonSeller','Connectors.AmazonAds');
 'Desktop'=@('Worker','Analytics')
}
foreach($entry in $refs.GetEnumerator()) { foreach($ref in $entry.Value) {
 dotnet add "src/WalkaAmazonIntelligence.$($entry.Key)" reference "src/WalkaAmazonIntelligence.$ref"
 if($LASTEXITCODE) { throw 'Reference failed' }
} }
$packages = @{
 'Persistence'=@('Microsoft.EntityFrameworkCore.Sqlite@10.0.12','Microsoft.EntityFrameworkCore.Design@10.0.12');
 'Infrastructure'=@('Microsoft.Extensions.Http@10.0.12','System.Security.Cryptography.ProtectedData@10.0.12');
 'Desktop'=@('Microsoft.Extensions.Hosting@10.0.12','CommunityToolkit.Mvvm@8.4.2','Serilog.Extensions.Hosting@10.0.0','Serilog.Sinks.File@7.0.0')
}
foreach($entry in $packages.GetEnumerator()) { foreach($package in $entry.Value) {
 $parts=$package.Split('@'); dotnet add "src/WalkaAmazonIntelligence.$($entry.Key)" package $parts[0] --version $parts[1] --no-restore
 if($LASTEXITCODE) { throw 'Package failed' }
} }
foreach($name in @('Domain','Application','Persistence','Analytics')) { dotnet add "tests/WalkaAmazonIntelligence.$name.Tests" reference "src/WalkaAmazonIntelligence.$name" }
dotnet add tests/WalkaAmazonIntelligence.Connectors.Tests reference src/WalkaAmazonIntelligence.Connectors.AmazonSeller src/WalkaAmazonIntelligence.Connectors.AmazonAds
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.12
