using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Reports;

/// <summary>Persists requests and snapshots in SQLite through EF Core.</summary>
internal sealed class EfReportStore(ExpenseHubDbContext db) : IReportStore
{
    /// <inheritdoc />
    public async Task AddAsync(Report report, CancellationToken cancellationToken)
    {
        db.Reports.Add(report);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<List<Report>> ListOwnAsync(string userId, CancellationToken cancellationToken)
        => db.Reports.AsNoTracking().Where(report => report.RequestedByUserId == userId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Report?> FindOwnAsync(Guid id, string userId, CancellationToken cancellationToken)
        => db.Reports.AsNoTracking().SingleOrDefaultAsync(
            report => report.Id == id && report.RequestedByUserId == userId, cancellationToken);

    /// <inheritdoc />
    public Task<Report?> FindAsync(Guid id, CancellationToken cancellationToken)
        => db.Reports.SingleOrDefaultAsync(report => report.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<List<Expense>> ReadExpensesAsync(Report report, CancellationToken cancellationToken)
        => db.Expenses.AsNoTracking().Where(expense => expense.ExpenseDate >= report.FromDate
            && expense.ExpenseDate <= report.ToDate
            && (report.Scope == ReportScope.Global || expense.OwnerId == report.RequestedByUserId))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
