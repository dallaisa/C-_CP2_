namespace ExpenseHub.Api.Domain;

using System;

/// <summary>
/// Combines role, ownership, state and visibility into the contextual decisions of the access matrix.
/// </summary>
/// <remarks>
/// Route attributes only check that the caller has a role able to use the route.
/// These rules decide whether that caller may act on a specific expense, and keep the
/// self-approval and self-payment prohibitions even when roles are accumulated.
/// </remarks>
internal static class ExpenseAccessPolicy
{
    /// <summary>Checks whether a viewer can read an expense, using the same rule as the database filter.</summary>
    /// <param name="viewer">The authenticated viewer.</param>
    /// <param name="expense">The expense.</param>
    /// <returns><see langword="true"/> when the expense is visible to the viewer.</returns>
    internal static bool CanRead(ExpenseViewer viewer, Expense expense)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(expense);

        return ExpenseVisibility.VisibleTo(viewer).Compile().Invoke(expense);
    }

    /// <summary>Decides whether a viewer may edit or submit a draft.</summary>
    /// <param name="viewer">The authenticated viewer.</param>
    /// <param name="expense">The expense, or <see langword="null"/> when it does not exist.</param>
    /// <returns>The access decision.</returns>
    internal static ExpenseAccessDecision EvaluateDraftChange(ExpenseViewer viewer, Expense? expense)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (!viewer.IsEmployee)
        {
            return ExpenseAccessDecision.Forbidden;
        }

        // Another Employee's expense is treated as missing so its existence is not revealed.
        if (expense is null || !IsOwner(viewer, expense))
        {
            return ExpenseAccessDecision.NotFound;
        }

        return expense.Status == ExpenseStatus.Draft
            ? ExpenseAccessDecision.Allowed
            : ExpenseAccessDecision.Conflict;
    }

    /// <summary>Decides whether a viewer may approve or reject an expense.</summary>
    /// <param name="viewer">The authenticated viewer.</param>
    /// <param name="expense">The expense, or <see langword="null"/> when it does not exist.</param>
    /// <returns>The access decision.</returns>
    internal static ExpenseAccessDecision EvaluateApprovalDecision(ExpenseViewer viewer, Expense? expense)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (!viewer.IsApprover)
        {
            return ExpenseAccessDecision.Forbidden;
        }

        // Expenses that already left the approval queue stay in scope so a repeated decision is a conflict.
        bool inScope = expense is not null
            && (CanRead(viewer, expense)
                || expense.Status is ExpenseStatus.Approved or ExpenseStatus.Rejected or ExpenseStatus.Paid);
        if (!inScope)
        {
            return ExpenseAccessDecision.NotFound;
        }

        // The owner never decides on their own expense, whatever other roles they have.
        if (IsOwner(viewer, expense!))
        {
            return ExpenseAccessDecision.Forbidden;
        }

        return expense!.Status == ExpenseStatus.Submitted
            ? ExpenseAccessDecision.Allowed
            : ExpenseAccessDecision.Conflict;
    }

    /// <summary>Decides whether a viewer may record the payment of an expense.</summary>
    /// <param name="viewer">The authenticated viewer.</param>
    /// <param name="expense">The expense, or <see langword="null"/> when it does not exist.</param>
    /// <returns>The access decision.</returns>
    internal static ExpenseAccessDecision EvaluatePayment(ExpenseViewer viewer, Expense? expense)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (!viewer.IsFinance)
        {
            return ExpenseAccessDecision.Forbidden;
        }

        if (expense is null || !CanRead(viewer, expense))
        {
            return ExpenseAccessDecision.NotFound;
        }

        // The owner never pays their own expense, whatever other roles they have.
        if (IsOwner(viewer, expense))
        {
            return ExpenseAccessDecision.Forbidden;
        }

        return expense.Status == ExpenseStatus.Approved
            ? ExpenseAccessDecision.Allowed
            : ExpenseAccessDecision.Conflict;
    }

    private static bool IsOwner(ExpenseViewer viewer, Expense expense) =>
        !string.IsNullOrWhiteSpace(viewer.UserId)
        && string.Equals(expense.OwnerId, viewer.UserId, StringComparison.Ordinal);
}
