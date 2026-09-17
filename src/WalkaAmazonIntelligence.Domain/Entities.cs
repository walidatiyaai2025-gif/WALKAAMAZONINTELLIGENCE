namespace WalkaAmazonIntelligence.Domain;

public sealed class ReportArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Source { get; set; } = "";
    public string ReportType { get; set; } = "";
    public string Account { get; set; } = "";
    public string Marketplace { get; set; } = "";
    public string Profile { get; set; } = "";
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public DateTime? GeneratedUtc { get; set; }
    public DateTime DownloadedUtc { get; set; } = DateTime.UtcNow;
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public int RecordCount { get; set; }
    public ImportStatus Status { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public Guid? SyncRunId { get; set; }
}
public sealed class DailySales
{
    public long Id { get; set; }
    public string Account { get; set; } = "";
    public string Marketplace { get; set; } = "";
    public string Asin { get; set; } = "";
    public string ParentAsin { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Currency { get; set; } = "";
    public decimal Sales { get; set; }
    public int Units { get; set; }
    public int OrderItems { get; set; }
    public int? Sessions { get; set; }
    public int? PageViews { get; set; }
    public Guid ArtifactId { get; set; }
}
public sealed class AdvertisingDailyMetric
{
    public long Id { get; set; }
    public string Account { get; set; } = "";
    public string Marketplace { get; set; } = "";
    public string Profile { get; set; } = "";
    public string CampaignId { get; set; } = "";
    public string AdGroupId { get; set; } = "";
    public string AdId { get; set; } = "";
    public string Asin { get; set; } = "";
    public string Sku { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Currency { get; set; } = "";
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Spend { get; set; }
    public decimal Sales7d { get; set; }
    public int Orders7d { get; set; }
    public Guid ArtifactId { get; set; }
}
public sealed class AppSetting { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }
public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTime Utc { get; set; } = DateTime.UtcNow;
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public Guid? CorrelationId { get; set; }
}
public sealed class SyncRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Source { get; set; } = "";
    public string Scope { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
    public JobStatus Status { get; set; }
    public int Records { get; set; }
    public string? Error { get; set; }
    public string? RemoteReportId { get; set; }
}
public sealed class ListingSnapshot
{
    public long Id { get; set; }
    public string Account { get; set; } = "";
    public string Marketplace { get; set; } = "";
    public string Asin { get; set; } = "";
    public DateTime ObservedUtc { get; set; } = DateTime.UtcNow;
    public string ContentHash { get; set; } = "";
    public string Json { get; set; } = "";
}
public sealed class ChangeEvent
{
    public long Id { get; set; }
    public string Entity { get; set; } = "";
    public string Type { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Utc { get; set; } = DateTime.UtcNow;
    public long SnapshotId { get; set; }
}
