using System;

namespace ExpenseHub.Api.Domain;

/// <summary>
/// Applies the simulated payment of approved expenses.
/// </summary>
/// <remarks>
/// The new status, the payment record and the history entry are attached to the same expense,
/// so a single save persists all of them or none of them. <see cref="ExpenseStatus.Paid"/> is final.
/// </remarks>
internal static class ExpensePaymentRules
{
    /// <summary>Pays an approved expense owned by another user.</summary>
    /// <param name="expense">The expense to pay.</param>
    /// <param name="actorId">The identifier of the authenticated Finance user.</param>
    /// <param name="nowUtc">The current server time in UTC.</param>
    /// <returns>The outcome of the payment attempt.</returns>
    internal static ExpensePaymentOutcome Pay(Expense expense, string actorId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        if (string.Equals(expense.OwnerId, actorId, StringComparison.Ordinal))
        {
            return ExpensePaymentOutcome.SelfPayment;
        }

        if (expense.Status != ExpenseStatus.Approved || expense.Payment is not null)
        {
            return ExpensePaymentOutcome.NotApproved;
        }

        DateTimeOffset paidAtUtc = nowUtc.ToUniversalTime();

        expense.Status = ExpenseStatus.Paid;

        // The key is left unset so Entity Framework treats the record as new and generates it on insert.
        expense.Payment = new PaymentRecord
        {
            ExpenseId = expense.Id,
            ActorId = actorId,
            PaidAtUtc = paidAtUtc,
        };
        expense.History.Add(new ExpenseHistory
        {
            ExpenseId = expense.Id,
            Action = ExpenseHistoryActions.Paid,
            ActorId = actorId,
            OccurredAtUtc = paidAtUtc,
            PreviousStatus = ExpenseStatus.Approved,
            NewStatus = ExpenseStatus.Paid,
        });

        return ExpensePaymentOutcome.Paid;
    }
}
