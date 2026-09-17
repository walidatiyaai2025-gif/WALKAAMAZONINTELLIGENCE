using System.Text.Json;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Connectors.AmazonAds;

public sealed class AdsReportParser : IReportParser
{
    public ParsedReport Parse(Stream json, DataScope scope, DateOnly date)
    {
        scope.Validate(); if (string.IsNullOrWhiteSpace(scope.Profile)) throw new InvalidDataException("Ads profile is required.");
        using var doc = JsonDocument.Parse(json); var rows = new List<AdvertisingDailyMetric>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (DateOnly.Parse(r.GetProperty("date").GetString()!) != date) throw new InvalidDataException("Ads report date mismatch.");
            var row = new AdvertisingDailyMetric { Account = scope.Account, Marketplace = scope.Marketplace, Profile = scope.Profile,
                Date = date, Currency = scope.Currency, CampaignId = r.GetProperty("campaignId").ToString(), AdGroupId = r.GetProperty("adGroupId").ToString(),
                AdId = r.GetProperty("adId").ToString(), Asin = r.GetProperty("advertisedAsin").GetString()!, Sku = r.GetProperty("advertisedSku").GetString()!,
                Impressions = r.GetProperty("impressions").GetInt64(), Clicks = r.GetProperty("clicks").GetInt64(), Spend = r.GetProperty("spend").GetDecimal(),
                Sales7d = r.GetProperty("sales7d").GetDecimal(), Orders7d = r.GetProperty("purchases7d").GetInt32() };
            if (string.IsNullOrWhiteSpace(row.AdId) || string.IsNullOrWhiteSpace(row.Asin) || row.Impressions < 0 || row.Clicks < 0 || row.Spend < 0 || row.Sales7d < 0 || row.Orders7d < 0)
                throw new InvalidDataException("Invalid advertising metric.");
            rows.Add(row);
        }
        if (rows.Select(r => r.AdId).Distinct().Count() != rows.Count) throw new InvalidDataException("Duplicate advertised product rows.");
        return new([], rows);
    }
}
