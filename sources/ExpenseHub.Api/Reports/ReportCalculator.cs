using System;
using System.Collections.Generic;
using System.Linq;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Reports;

/// <summary>Calculates report snapshots without database or messaging dependencies.</summary>
internal sealed class ReportCalculator(TimeProvider clock)
{
    /// <summary>Filters by inclusive expense dates and scope, retaining all five states.</summary>
    internal ReportResult Calculate(
        IEnumerable<Expense> expenses, DateOnly fromDate, DateOnly toDate,
        ReportScope scope, string requestedByUserId)
    {
        ArgumentNullException.ThrowIfNull(expenses);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedByUserId);
        if (fromDate > toDate)
        {
            throw new ArgumentException("The report period is inverted.", nameof(toDate));
        }

        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        Dictionary<ExpenseStatus, (int Count, decimal Amount)> amounts = Enum.GetValues<ExpenseStatus>()
            .ToDictionary(status => status, _ => (Count: 0, Amount: 0m));
        foreach (Expense expense in expenses)
        {
            if (expense.ExpenseDate >= fromDate && expense.ExpenseDate <= toDate
                && (scope == ReportScope.Global || expense.OwnerId == requestedByUserId))
            {
                (int count, decimal amount) = amounts[expense.Status];
                amounts[expense.Status] = (checked(count + 1), amount + expense.Amount);
            }
        }

        ReportStatusTotal[] totals = amounts.Select(pair => new ReportStatusTotal(
            pair.Key.ToString(), pair.Value.Count, pair.Value.Amount)).ToArray();
        return new ReportResult(clock.GetUtcNow().ToUniversalTime(), totals.Sum(row => row.Count),
            totals.Sum(row => row.Amount), Array.AsReadOnly(totals));
    }
}
