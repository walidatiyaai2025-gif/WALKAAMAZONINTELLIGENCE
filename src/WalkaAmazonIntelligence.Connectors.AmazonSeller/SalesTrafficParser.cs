using System.Text.Json;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
namespace WalkaAmazonIntelligence.Connectors.AmazonSeller;

public sealed class SalesTrafficParser : IReportParser
{
    public ParsedReport Parse(Stream json, DataScope scope, DateOnly date)
    {
        scope.Validate(); using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        var specification = root.GetProperty("reportSpecification");
        if (specification.GetProperty("reportType").GetString() != "GET_SALES_AND_TRAFFIC_REPORT" ||
            DateOnly.Parse(specification.GetProperty("dataStartTime").GetString()![..10]) != date ||
            DateOnly.Parse(specification.GetProperty("dataEndTime").GetString()![..10]) != date ||
            specification.GetProperty("marketplaceIds").GetArrayLength() != 1 ||
            specification.GetProperty("marketplaceIds")[0].GetString() != scope.Marketplace)
            throw new InvalidDataException("Report date, type or marketplace mismatch.");
        var result = new List<DailySales>();
        foreach (var item in root.GetProperty("salesAndTrafficByAsin").EnumerateArray())
        {
            var s = item.GetProperty("salesByAsin"); var t = item.GetProperty("trafficByAsin");
            var money = s.GetProperty("orderedProductSales");
            if (money.GetProperty("currencyCode").GetString() != scope.Currency) throw new InvalidDataException("Currency mismatch.");
            var row = new DailySales { Account = scope.Account, Marketplace = scope.Marketplace, Date = date, Currency = scope.Currency,
                Asin = item.GetProperty("childAsin").GetString()!, ParentAsin = item.GetProperty("parentAsin").GetString()!,
                Sales = money.GetProperty("amount").GetDecimal(), Units = s.GetProperty("unitsOrdered").GetInt32(),
                OrderItems = s.GetProperty("totalOrderItems").GetInt32(),
                Sessions = t.TryGetProperty("sessions", out var sessions) ? sessions.GetInt32() : null,
                PageViews = t.TryGetProperty("pageViews", out var views) ? views.GetInt32() : null };
            if (string.IsNullOrWhiteSpace(row.Asin) || row.Sales < 0 || row.Units < 0 || row.OrderItems < 0 || row.Sessions < 0 || row.PageViews < 0)
                throw new InvalidDataException("Invalid sales metric.");
            result.Add(row);
        }
        if (result.Select(r => r.Asin).Distinct().Count() != result.Count) throw new InvalidDataException("Duplicate ASIN rows.");
        return new(result, []);
    }
}
