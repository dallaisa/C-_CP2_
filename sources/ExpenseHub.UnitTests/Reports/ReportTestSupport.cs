using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.UnitTests.Reports;

internal sealed class ReportTestClock : TimeProvider
{
    internal static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class MemoryReportStore : IReportStore
{
    internal List<Report> Reports { get; } = [];

    internal List<Expense> Expenses { get; } = [];

    internal int ExpenseReads { get; private set; }

    internal bool ThrowOnAdd { get; set; }

    internal bool ThrowOnRead { get; set; }

    internal bool SaveSucceeds { get; set; } = true;

    internal List<ReportStatus> SavedStates { get; } = [];

    public Task AddAsync(Report report, CancellationToken cancellationToken)
    {
        if (ThrowOnAdd)
        {
            throw new DbUpdateException("sensitive database details");
        }

        Reports.Add(report);
        return Task.CompletedTask;
    }

    public Task<List<Report>> ListOwnAsync(string userId, CancellationToken cancellationToken)
        => Task.FromResult(Reports.Where(report => report.RequestedByUserId == userId).ToList());

    public Task<Report?> FindOwnAsync(Guid id, string userId, CancellationToken cancellationToken)
        => Task.FromResult(Reports.SingleOrDefault(report => report.Id == id && report.RequestedByUserId == userId));

    public Task<Report?> FindAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(Reports.SingleOrDefault(report => report.Id == id));

    public Task<List<Expense>> ReadExpensesAsync(Report report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExpenseReads++;
        if (ThrowOnRead)
        {
            throw new InvalidOperationException("Sensitive connection details must never reach the response.");
        }

        return Task.FromResult(Expenses.ToList());
    }

    public Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        SavedStates.Add(Reports[0].Status);
        return Task.FromResult(SaveSucceeds);
    }

    internal static Report Pending() => new Report
    {
        Id = Guid.NewGuid(),
        RequestedByUserId = "owner",
        FromDate = new DateOnly(2026, 9, 1),
        ToDate = new DateOnly(2026, 9, 30),
        Scope = ReportScope.Mine,
        Status = ReportStatus.Pending,
        CreatedAt = ReportTestClock.Now,
    };
}

internal sealed class ConfirmingPublisher(MemoryReportStore store, bool confirmed) : IReportPublisher
{
    internal bool Called { get; private set; }

    internal bool SawPersistedPending { get; private set; }

    public Task<bool> PublishConfirmedAsync(Guid reportId, CancellationToken cancellationToken)
    {
        Called = true;
        SawPersistedPending = store.Reports.Any(report => report.Id == reportId && report.Status == ReportStatus.Pending);
        return Task.FromResult(confirmed);
    }
}
