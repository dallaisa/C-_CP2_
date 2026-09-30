namespace ExpenseHub.Api.Domain;

using System;

/// <summary>
/// Applies the approval and rejection transitions of submitted expenses.
/// </summary>
/// <remarks>
/// <see cref="ExpenseStatus.Approved"/> and <see cref="ExpenseStatus.Rejected"/> are reached only
/// from <see cref="ExpenseStatus.Submitted"/>; there is no reopening, cancellation or resubmission.
/// </remarks>
internal static class ExpenseDecisionRules
{
    /// <summary>Approves a submitted expense.</summary>
    /// <param name="expense">The expense to approve.</param>
    /// <param name="actorId">The identifier of the authenticated Approver.</param>
    /// <param name="nowUtc">The current server time in UTC.</param>
    /// <returns>The outcome of the approval attempt.</returns>
    internal static ExpenseDecisionOutcome Approve(Expense expense, string actorId, DateTimeOffset nowUtc)
    {
        return Decide(expense, actorId, ExpenseStatus.Approved, ExpenseHistoryActions.Approved, null, nowUtc);
    }

    /// <summary>Rejects a submitted expense with a justification.</summary>
    /// <param name="expense">The expense to reject.</param>
    /// <param name="actorId">The identifier of the authenticated Approver.</param>
    /// <param name="reason">The validated rejection justification.</param>
    /// <param name="nowUtc">The current server time in UTC.</param>
    /// <returns>The outcome of the rejection attempt.</returns>
    internal static ExpenseDecisionOutcome Reject(
        Expense expense,
        string actorId,
        string reason,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return Decide(expense, actorId, ExpenseStatus.Rejected, ExpenseHistoryActions.Rejected, reason, nowUtc);
    }

    private static ExpenseDecisionOutcome Decide(
        Expense expense,
        string actorId,
        ExpenseStatus newStatus,
        string action,
        string? reason,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        if (string.Equals(expense.OwnerId, actorId, StringComparison.Ordinal))
        {
            return ExpenseDecisionOutcome.SelfDecision;
        }

        if (expense.Status != ExpenseStatus.Submitted)
        {
            return ExpenseDecisionOutcome.NotSubmitted;
        }

        expense.Status = newStatus;
        expense.History.Add(new ExpenseHistory
        {
            ExpenseId = expense.Id,
            Action = action,
            ActorId = actorId,
            OccurredAtUtc = nowUtc.ToUniversalTime(),
            PreviousStatus = ExpenseStatus.Submitted,
            NewStatus = newStatus,
            RejectionReason = reason,
        });

        return ExpenseDecisionOutcome.Applied;
    }
}
