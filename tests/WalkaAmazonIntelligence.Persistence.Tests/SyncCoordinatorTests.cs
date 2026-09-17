using Microsoft.EntityFrameworkCore;
using WalkaAmazonIntelligence.Application;
using WalkaAmazonIntelligence.Domain;
using WalkaAmazonIntelligence.Infrastructure;
using WalkaAmazonIntelligence.Persistence;
using WalkaAmazonIntelligence.Worker;

namespace WalkaAmazonIntelligence.Persistence.Tests;

public sealed class SyncCoordinatorTests
{
    private static readonly DataScope Scope = new("TEST_FIXTURE_ACCOUNT", "ATVPDKIKX0DER", "USD", "TEST_FIXTURE_PROFILE");
    private static readonly DateOnly ReportDate = new(2026, 1, 1);

    [Fact]
    public async Task AmbiguousCreationIsNotRepeatedUntilRemoteIdIsReconciled()
    {
        var root = CreateRoot();
        try
        {
            var factory = await CreateDatabaseAsync(root);
            var connector = new FakeConnector
            {
                OnRequest = (_, _, _) => Task.FromException<ReportTicket>(new HttpRequestException("SENSITIVE_FIXTURE_NETWORK_MESSAGE")),
                OnPoll = (id, _) => Task.FromResult(new ReportTicket(id, "PROCESSING"))
            };
            var coordinator = CreateCoordinator(factory, root);

            var first = await Assert.ThrowsAsync<ReportCreationUncertainException>(() =>
                coordinator.StepAsync(connector, new UnusedParser(), Scope, ReportDate));
            Assert.Equal(1, connector.RequestCalls);

            var second = await Assert.ThrowsAsync<ReportCreationUncertainException>(() =>
                coordinator.StepAsync(connector, new UnusedParser(), Scope, ReportDate));
            Assert.Equal(first.SyncRunId, second.SyncRunId);
            Assert.Equal(1, connector.RequestCalls);

            await using (var db = factory.Create())
            {
                var run = await db.SyncRuns.SingleAsync();
                Assert.Equal(JobStatus.Failed, run.Status);
                Assert.Equal("REPORT_CREATION_UNCERTAIN", run.Error);
                Assert.Null(run.RemoteReportId);
                var audit = await db.Audit.ToListAsync();
                Assert.Contains(audit, x => x.Action == "SYNC_REPORT_CREATION_UNCERTAIN");
                Assert.DoesNotContain(audit, x => x.Detail.Contains("SENSITIVE_FIXTURE_NETWORK_MESSAGE", StringComparison.Ordinal));
            }

            await coordinator.ResolveUncertainRequestAsync(first.SyncRunId, "TEST_REMOTE_REPORT_123");
            var message = await coordinator.StepAsync(connector, new UnusedParser(), Scope, ReportDate);

            Assert.Contains("processing", message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, connector.RequestCalls);
            Assert.Equal(1, connector.PollCalls);
            await using (var db = factory.Create())
            {
                var run = await db.SyncRuns.SingleAsync();
                Assert.Equal("TEST_REMOTE_REPORT_123", run.RemoteReportId);
                Assert.Equal(JobStatus.Pending, run.Status);
                Assert.Null(run.Error);
                Assert.Contains(await db.Audit.ToListAsync(), x => x.Action == "SYNC_REPORT_ID_RECOVERED");
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ExplicitConfirmedAbsenceAllowsOneFreshReportRequest()
    {
        var root = CreateRoot();
        try
        {
            var factory = await CreateDatabaseAsync(root);
            var connector = new FakeConnector
            {
                OnRequest = (_, _, _) => Task.FromException<ReportTicket>(new TimeoutException("SENSITIVE_FIXTURE_TIMEOUT"))
            };
            var coordinator = CreateCoordinator(factory, root);

            var uncertain = await Assert.ThrowsAsync<ReportCreationUncertainException>(() =>
                coordinator.StepAsync(connector, new UnusedParser(), Scope, ReportDate));
            Assert.Equal(1, connector.RequestCalls);

            await coordinator.ConfirmNoRemoteReportAsync(uncertain.SyncRunId);
            connector.OnRequest = (_, _, _) => Task.FromResult(new ReportTicket("TEST_REMOTE_REPORT_456", "PENDING"));

            var message = await coordinator.StepAsync(connector, new UnusedParser(), Scope, ReportDate);
            Assert.Contains("requested", message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, connector.RequestCalls);

            await using var db = factory.Create();
            var runs = await db.SyncRuns.OrderBy(x => x.StartedUtc).ToListAsync();
            Assert.Equal(2, runs.Count);
            Assert.Equal("REPORT_CREATION_CONFIRMED_ABSENT", runs[0].Error);
            Assert.Equal("TEST_REMOTE_REPORT_456", runs[1].RemoteReportId);
            Assert.Contains(await db.Audit.ToListAsync(), x => x.Action == "SYNC_REPORT_CREATION_CONFIRMED_ABSENT");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task CancellationWithPersistedRemoteIdResumesSameReportWithoutNewPost()
    {
        var root = CreateRoot();
        try
        {
            var factory = await CreateDatabaseAsync(root);
            var connector = new FakeConnector
            {
                OnRequest = (_, _, _) => Task.FromResult(new ReportTicket("TEST_REMOTE_REPORT_789", "PENDING")),
                OnPoll = (_, _) => Task.FromException<ReportTicket>(new OperationCanceledException("SENSITIVE_FIXTURE_CANCEL"))
            };
            var coordinator = CreateCoordinator(factory, root);

            await coordinator.StepAsync(connector, new UnusedParser(), Scope, ReportDate);
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                coordinator.StepAsync(connector, new UnusedParser(), Scope, ReportDate));

            await using (var db = factory.Create())
            {
                var run = await db.SyncRuns.SingleAsync();
                Assert.Equal(JobStatus.Cancelled, run.Status);
                Assert.Equal("CANCELLED", run.Error);
                Assert.Equal("TEST_REMOTE_REPORT_789", run.RemoteReportId);
                Assert.DoesNotContain(await db.Audit.ToListAsync(), x => x.Detail.Contains("SENSITIVE_FIXTURE_CANCEL", StringComparison.Ordinal));
            }

            connector.OnPoll = (id, _) => Task.FromResult(new ReportTicket(id, "PROCESSING"));
            var resumed = await coordinator.StepAsync(connector, new UnusedParser(), Scope, ReportDate);
            Assert.Contains("processing", resumed, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, connector.RequestCalls);
            Assert.Equal(2, connector.PollCalls);

            await using (var db = factory.Create())
            {
                var run = await db.SyncRuns.SingleAsync();
                Assert.Equal(JobStatus.Pending, run.Status);
                Assert.Null(run.Error);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static SyncCoordinator CreateCoordinator(DatabaseFactory factory, string root) =>
        new(factory, new EvidenceArchive(new HttpClient(new UnexpectedDownloadHandler()), root), new UnusedImporter());

    private static async Task<DatabaseFactory> CreateDatabaseAsync(string root)
    {
        var factory = new DatabaseFactory(Path.Combine(root, "Database", "walka.db"));
        await factory.InitializeAsync();
        return factory;
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "walka-p04-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class FakeConnector : IReportConnector
    {
        public string Source => "Seller";
        public string ReportType => "TEST_FIXTURE_REPORT";
        public int RequestCalls { get; private set; }
        public int PollCalls { get; private set; }
        public Func<DataScope, DateOnly, CancellationToken, Task<ReportTicket>> OnRequest { get; set; } =
            (_, _, _) => Task.FromResult(new ReportTicket("TEST_REMOTE_REPORT", "PENDING"));
        public Func<string, CancellationToken, Task<ReportTicket>> OnPoll { get; set; } =
            (id, _) => Task.FromResult(new ReportTicket(id, "PROCESSING"));

        public Task ValidateConfigurationAsync(DataScope scope, CancellationToken ct)
        {
            scope.Validate();
            return Task.CompletedTask;
        }

        public Task<ReportTicket> RequestAsync(DataScope scope, DateOnly date, CancellationToken ct)
        {
            RequestCalls++;
            return OnRequest(scope, date, ct);
        }

        public Task<ReportTicket> PollAsync(string id, CancellationToken ct)
        {
            PollCalls++;
            return OnPoll(id, ct);
        }
    }

    private sealed class UnusedParser : IReportParser
    {
        public ParsedReport Parse(Stream json, DataScope scope, DateOnly date) =>
            throw new InvalidOperationException("TEST_FIXTURE parser must not be reached in these orchestration tests.");
    }

    private sealed class UnusedImporter : IReportImporter
    {
        public Task<int> ImportAsync(ReportArtifact artifact, ParsedReport rows, CancellationToken ct) =>
            Task.FromResult(rows.Count);
    }

    private sealed class UnexpectedDownloadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new InvalidOperationException("TEST_FIXTURE download must not be reached."));
    }
}
