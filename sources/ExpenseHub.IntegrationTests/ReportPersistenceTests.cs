using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Persistence;
using ExpenseHub.Api.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.IntegrationTests;

/// <summary>Uses isolated disposable SQLite files, separate from unit tests and any user's database.</summary>
[TestClass]
[TestCategory("SQLite")]
public sealed class ReportPersistenceTests
{
    /// <summary>New contexts and processors preserve the completed snapshot after expenses change.</summary>
    [TestMethod]
    public async Task RestartAndDuplicatePreserveSnapshotAndGeneratedAt()
    {
        using TestDatabase database = new TestDatabase();
        Guid id = await database.SeedAsync();
        string snapshot;
        DateTimeOffset? completedAt;
        await using (ExpenseHubDbContext first = database.Open())
        {
            Assert.IsTrue(await Processor(first).ProcessAsync(id, CancellationToken.None));
            Report report = await first.Reports.SingleAsync();
            snapshot = report.ResultJson!;
            completedAt = report.CompletedAt;
            Assert.AreEqual(100.25m, report.ReadResult()!.TotalAmount);
            Assert.AreEqual(ExpenseStatus.Approved, (await first.Expenses.SingleAsync()).Status);
        }

        await using (ExpenseHubDbContext edit = database.Open())
        {
            (await edit.Expenses.SingleAsync()).Amount = 999m;
            await edit.SaveChangesAsync();
        }

        await using (ExpenseHubDbContext restarted = database.Open())
        {
            Assert.IsTrue(await Processor(restarted).ProcessAsync(id, CancellationToken.None));
            Report report = await restarted.Reports.SingleAsync();
            Assert.AreEqual(ReportStatus.Completed, report.Status);
            Assert.AreEqual(snapshot, report.ResultJson);
            Assert.AreEqual(completedAt, report.CompletedAt);
            Assert.IsNull(report.ErrorCode);
            Assert.AreEqual(1, await restarted.Reports.CountAsync());
        }
    }

    /// <summary>Persisted Processing can be recovered by a newly created worker scope.</summary>
    [TestMethod]
    public async Task RestartResumesProcessing()
    {
        using TestDatabase database = new TestDatabase();
        Guid id = await database.SeedAsync();
        DateTimeOffset previous = DateTimeOffset.UtcNow;
        await using (ExpenseHubDbContext interrupted = database.Open())
        {
            Report report = await interrupted.Reports.SingleAsync();
            report.Status = ReportStatus.Processing;
            report.StartedAt = previous;
            await interrupted.SaveChangesAsync();
        }

        await using ExpenseHubDbContext resumed = database.Open();
        Assert.IsTrue(await Processor(resumed).ProcessAsync(id, CancellationToken.None));
        Report saved = await resumed.Reports.SingleAsync();
        Assert.AreEqual(ReportStatus.Completed, saved.Status);
        Assert.IsGreaterThan(previous, saved.StartedAt!.Value);
        Assert.AreEqual(100.25m, saved.ReadResult()!.TotalAmount);
    }

    /// <summary>A stale Processing attempt cannot overwrite a newer claim or terminal snapshot.</summary>
    [TestMethod]
    public async Task ConcurrentReclaimFencesOlderAttempt()
    {
        using TestDatabase database = new TestDatabase();
        await database.SeedAsync();
        await using ExpenseHubDbContext older = database.Open();
        Report oldReport = await older.Reports.SingleAsync();
        oldReport.Status = ReportStatus.Processing;
        oldReport.StartedAt = DateTimeOffset.UtcNow;
        Assert.IsTrue(await new EfReportStore(older).SaveAsync(CancellationToken.None));

        await using (ExpenseHubDbContext newer = database.Open())
        {
            Report next = await newer.Reports.SingleAsync();
            next.StartedAt = next.StartedAt!.Value.AddTicks(1);
            Assert.IsTrue(await new EfReportStore(newer).SaveAsync(CancellationToken.None));
            oldReport.Status = ReportStatus.Completed;
            oldReport.ResultJson = "stale attempt";
            Assert.IsFalse(await new EfReportStore(older).SaveAsync(CancellationToken.None));
            Assert.IsTrue(await Processor(newer).ProcessAsync(next.Id, CancellationToken.None));
        }

        await using ExpenseHubDbContext check = database.Open();
        Report saved = await check.Reports.SingleAsync();
        Assert.AreEqual(ReportStatus.Completed, saved.Status);
        Assert.AreEqual(100.25m, saved.ReadResult()!.TotalAmount);
    }

    /// <summary>A definitive invalid saved period produces a durable Failed result with no recalculation on restart.</summary>
    [TestMethod]
    public async Task DefinitiveFailureSurvivesRestart()
    {
        using TestDatabase database = new TestDatabase();
        Guid id = await database.SeedAsync(invalidPeriod: true);
        await using (ExpenseHubDbContext first = database.Open())
        {
            Assert.IsTrue(await Processor(first).ProcessAsync(id, CancellationToken.None));
        }

        await using ExpenseHubDbContext restarted = database.Open();
        Assert.IsTrue(await Processor(restarted).ProcessAsync(id, CancellationToken.None));
        Report report = await restarted.Reports.SingleAsync();
        Assert.AreEqual(ReportStatus.Failed, report.Status);
        Assert.IsNull(report.ResultJson);
        Assert.AreEqual("REPORT_PROCESSING_FAILED", report.ErrorCode);
        Assert.IsNotNull(report.CompletedAt);
    }

    private static ReportProcessor Processor(ExpenseHubDbContext db) => new ReportProcessor(
        new EfReportStore(db), new ReportCalculator(TimeProvider.System), TimeProvider.System,
        NullLogger<ReportProcessor>.Instance);

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"expensehub-i03-test-{Guid.NewGuid():N}.db");

        internal ExpenseHubDbContext Open() => new ExpenseHubDbContext(
            new DbContextOptionsBuilder<ExpenseHubDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);

        internal async Task<Guid> SeedAsync(bool invalidPeriod = false)
        {
            await using ExpenseHubDbContext db = Open();
            await db.Database.EnsureCreatedAsync();
            db.ExpenseCategories.Add(new ExpenseCategory { Id = 1, Name = "Test category" });
            db.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(),
                OwnerId = "owner",
                Amount = 100.25m,
                ExpenseDate = new DateOnly(2026, 9, 1),
                ExpenseCategoryId = 1,
                Status = ExpenseStatus.Approved,
            });
            Guid id = Guid.NewGuid();
            db.Reports.Add(new Report
            {
                Id = id,
                RequestedByUserId = "owner",
                Scope = ReportScope.Mine,
                FromDate = new DateOnly(2026, 9, 1),
                ToDate = invalidPeriod ? new DateOnly(2026, 8, 1) : new DateOnly(2026, 9, 30),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public void Dispose()
        {
            // Only the randomly named database created by this test is removed.
            foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                File.Delete(_path + suffix);
            }
        }
    }
}
