using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Persistence;
using ExpenseHub.Api.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.IntegrationTests;

/// <summary>CP3-I04: verifies the SQL period and scope filters against a disposable SQLite file (no network, no broker).</summary>
[TestClass]
[TestCategory("SQLite")]
public sealed class ReportQueryTests
{
    private static readonly DateOnly _from = new DateOnly(2026, 9, 1);
    private static readonly DateOnly _to = new DateOnly(2026, 9, 30);

    /// <summary>Mine sums only the requester's expenses; boundaries are inclusive and outside dates are excluded.</summary>
    [TestMethod]
    public async Task MineFiltersRequesterAndInclusivePeriodInSql()
    {
        ReportResult result = await RunAsync(ReportScope.Mine);

        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(175m, result.TotalAmount);
        Assert.AreEqual(150m, result.ByStatus.Single(row => row.Status == "Approved").Amount);
        Assert.AreEqual(25m, result.ByStatus.Single(row => row.Status == "Paid").Amount);
        Assert.AreEqual(0m, result.ByStatus.Single(row => row.Status == "Draft").Amount);
        Assert.HasCount(5, result.ByStatus);
    }

    /// <summary>Global includes every employee in the period, still excluding out-of-period expenses.</summary>
    [TestMethod]
    public async Task GlobalIncludesAllEmployeesInsidePeriod()
    {
        ReportResult result = await RunAsync(ReportScope.Global);

        Assert.AreEqual(4, result.TotalCount);
        Assert.AreEqual(375m, result.TotalAmount);
        Assert.AreEqual(350m, result.ByStatus.Single(row => row.Status == "Approved").Amount);
    }

    private static async Task<ReportResult> RunAsync(ReportScope scope)
    {
        string path = Path.Combine(Path.GetTempPath(), $"expensehub-i04-test-{Guid.NewGuid():N}.db");
        try
        {
            DbContextOptions<ExpenseHubDbContext> options = new DbContextOptionsBuilder<ExpenseHubDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False").Options;
            Guid id = Guid.NewGuid();
            await using (ExpenseHubDbContext seed = new ExpenseHubDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.ExpenseCategories.Add(new ExpenseCategory { Id = 1, Name = "Test category" });
                seed.Expenses.AddRange(
                    Expense("owner", ExpenseStatus.Approved, 100m, _from),
                    Expense("owner", ExpenseStatus.Approved, 50m, _to),
                    Expense("owner", ExpenseStatus.Paid, 25m, new DateOnly(2026, 9, 15)),
                    Expense("other", ExpenseStatus.Approved, 200m, _from),
                    Expense("owner", ExpenseStatus.Paid, 500m, _to.AddDays(1)),
                    Expense("owner", ExpenseStatus.Paid, 900m, _from.AddDays(-1)));
                seed.Reports.Add(new Report
                {
                    Id = id,
                    RequestedByUserId = "owner",
                    Scope = scope,
                    FromDate = _from,
                    ToDate = _to,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await seed.SaveChangesAsync();
            }

            await using ExpenseHubDbContext db = new ExpenseHubDbContext(options);
            ReportProcessor processor = new ReportProcessor(new EfReportStore(db), new ReportCalculator(TimeProvider.System),
                TimeProvider.System, NullLogger<ReportProcessor>.Instance);
            Assert.IsTrue(await processor.ProcessAsync(id, CancellationToken.None));
            Report saved = await db.Reports.SingleAsync();
            Assert.AreEqual(ReportStatus.Completed, saved.Status);
            return saved.ReadResult()!;
        }
        finally
        {
            // Only the randomly named database created above is removed.
            foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                File.Delete(path + suffix);
            }
        }
    }

    private static Expense Expense(string owner, ExpenseStatus status, decimal amount, DateOnly date) => new Expense
    {
        Id = Guid.NewGuid(),
        OwnerId = owner,
        Status = status,
        Amount = amount,
        ExpenseDate = date,
        ExpenseCategoryId = 1,
    };
}
