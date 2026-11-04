using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Reports;

/// <summary>Persistence boundary shared by the API and future worker.</summary>
internal interface IReportStore
{
    /// <summary>Persists a new pending request.</summary>
    Task AddAsync(Report report, CancellationToken cancellationToken);

    /// <summary>Lists only reports created by the specified identity.</summary>
    Task<List<Report>> ListOwnAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Finds a report owned by the specified identity.</summary>
    Task<Report?> FindOwnAsync(Guid id, string userId, CancellationToken cancellationToken);

    /// <summary>Finds a report for trusted worker processing.</summary>
    Task<Report?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Reads current expenses in the persisted period and scope.</summary>
    Task<List<Expense>> ReadExpensesAsync(Report report, CancellationToken cancellationToken);

    /// <summary>Saves a state change, returning false on a competing transition.</summary>
    Task<bool> SaveAsync(CancellationToken cancellationToken);
}
