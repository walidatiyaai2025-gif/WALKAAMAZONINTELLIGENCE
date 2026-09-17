using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WalkaAmazonIntelligence.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEvidenceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Listings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Account = table.Column<string>(type: "TEXT", nullable: false),
                    Marketplace = table.Column<string>(type: "TEXT", nullable: false),
                    Asin = table.Column<string>(type: "TEXT", nullable: false),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Listings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Records = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    RemoteReportId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Changes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Entity = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    OldValue = table.Column<string>(type: "TEXT", nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", nullable: true),
                    Utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Changes_Listings_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Artifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    ReportType = table.Column<string>(type: "TEXT", nullable: false),
                    Account = table.Column<string>(type: "TEXT", nullable: false),
                    Marketplace = table.Column<string>(type: "TEXT", nullable: false),
                    Profile = table.Column<string>(type: "TEXT", nullable: false),
                    Start = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    End = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    GeneratedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DownloadedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncRunId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Artifacts_SyncRuns_SyncRunId",
                        column: x => x.SyncRunId,
                        principalTable: "SyncRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Ads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Account = table.Column<string>(type: "TEXT", nullable: false),
                    Marketplace = table.Column<string>(type: "TEXT", nullable: false),
                    Profile = table.Column<string>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<string>(type: "TEXT", nullable: false),
                    AdGroupId = table.Column<string>(type: "TEXT", nullable: false),
                    AdId = table.Column<string>(type: "TEXT", nullable: false),
                    Asin = table.Column<string>(type: "TEXT", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    Impressions = table.Column<long>(type: "INTEGER", nullable: false),
                    Clicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Spend = table.Column<decimal>(type: "TEXT", nullable: false),
                    Sales7d = table.Column<decimal>(type: "TEXT", nullable: false),
                    Orders7d = table.Column<int>(type: "INTEGER", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ads_Artifacts_ArtifactId",
                        column: x => x.ArtifactId,
                        principalTable: "Artifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Account = table.Column<string>(type: "TEXT", nullable: false),
                    Marketplace = table.Column<string>(type: "TEXT", nullable: false),
                    Asin = table.Column<string>(type: "TEXT", nullable: false),
                    ParentAsin = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    Sales = table.Column<decimal>(type: "TEXT", nullable: false),
                    Units = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderItems = table.Column<int>(type: "INTEGER", nullable: false),
                    Sessions = table.Column<int>(type: "INTEGER", nullable: true),
                    PageViews = table.Column<int>(type: "INTEGER", nullable: true),
                    ArtifactId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sales_Artifacts_ArtifactId",
                        column: x => x.ArtifactId,
                        principalTable: "Artifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ads_Account_Marketplace_Profile_Date_AdId",
                table: "Ads",
                columns: new[] { "Account", "Marketplace", "Profile", "Date", "AdId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ads_ArtifactId",
                table: "Ads",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_Source_Account_Marketplace_Profile_ReportType_Start_End_Sha256",
                table: "Artifacts",
                columns: new[] { "Source", "Account", "Marketplace", "Profile", "ReportType", "Start", "End", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_SyncRunId",
                table: "Artifacts",
                column: "SyncRunId");

            migrationBuilder.CreateIndex(
                name: "IX_Changes_SnapshotId",
                table: "Changes",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_Account_Marketplace_Asin_ObservedUtc",
                table: "Listings",
                columns: new[] { "Account", "Marketplace", "Asin", "ObservedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_Account_Marketplace_Date_Asin",
                table: "Sales",
                columns: new[] { "Account", "Marketplace", "Date", "Asin" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ArtifactId",
                table: "Sales",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_Source_Scope_Date_Status",
                table: "SyncRuns",
                columns: new[] { "Source", "Scope", "Date", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Ads");

            migrationBuilder.DropTable(
                name: "Audit");

            migrationBuilder.DropTable(
                name: "Changes");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Listings");

            migrationBuilder.DropTable(
                name: "Artifacts");

            migrationBuilder.DropTable(
                name: "SyncRuns");
        }
    }
}
