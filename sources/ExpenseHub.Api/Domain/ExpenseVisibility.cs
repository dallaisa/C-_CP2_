using System;
using System.Linq.Expressions;

namespace ExpenseHub.Api.Domain;

/// <summary>
/// Defines which expenses each profile can read.
/// </summary>
/// <remarks>
/// The rule is an expression so Entity Framework translates it into the SQL query and
/// filters the rows in the database, before any expense is materialized.
/// A user with several roles sees the union of what each role allows.
/// </remarks>
internal static class ExpenseVisibility
{
    /// <summary>Builds the filter of expenses visible to a viewer.</summary>
    /// <param name="viewer">The authenticated viewer.</param>
    /// <returns>A predicate that matches only the expenses the viewer can read.</returns>
    internal static Expression<Func<Expense, bool>> VisibleTo(ExpenseViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (viewer.IsAuditor)
        {
            return expense => true;
        }

        string userId = viewer.UserId;
        bool isEmployee = viewer.IsEmployee && !string.IsNullOrWhiteSpace(userId);
        bool isApprover = viewer.IsApprover;
        bool isFinance = viewer.IsFinance;

        return expense =>
            (isEmployee && expense.OwnerId == userId)
            || (isApprover && expense.Status == ExpenseStatus.Submitted)
            || (isFinance && (expense.Status == ExpenseStatus.Approved || expense.Status == ExpenseStatus.Paid));
    }
}
