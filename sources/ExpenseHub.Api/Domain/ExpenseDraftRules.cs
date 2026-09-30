namespace ExpenseHub.Api.Domain;

using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;

/// <summary>
/// Applies the creation, edition and submission rules of draft expenses.
/// </summary>
internal static class ExpenseDraftRules
{
    private static readonly JsonSerializerOptions ChangesSerializerOptions = new JsonSerializerOptions
    {
        // History is stored data, not HTML, so non-ASCII text is kept readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Creates a new draft expense owned by the authenticated user.</summary>
    /// <param name="ownerId">The identifier of the authenticated owner.</param>
    /// <param name="values">The validated draft values.</param>
    /// <param name="nowUtc">The current server time in UTC.</param>
    /// <returns>The new draft expense with its creation history entry.</returns>
    internal static Expense CreateDraft(string ownerId, ExpenseDraftValues values, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(values);

        Expense expense = new Expense
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Description = values.Description,
            Amount = values.Amount,
            ExpenseDate = values.ExpenseDate,
            ExpenseCategoryId = values.ExpenseCategoryId,
            Status = ExpenseStatus.Draft,
        };

        expense.History.Add(new ExpenseHistory
        {
            ExpenseId = expense.Id,
            Action = ExpenseHistoryActions.Created,
            ActorId = ownerId,
            OccurredAtUtc = nowUtc.ToUniversalTime(),
            PreviousStatus = null,
            NewStatus = ExpenseStatus.Draft,
        });

        return expense;
    }

    /// <summary>Edits a draft expense when the actor owns it and it is still a draft.</summary>
    /// <param name="expense">The expense to edit.</param>
    /// <param name="actorId">The identifier of the authenticated actor.</param>
    /// <param name="values">The validated draft values.</param>
    /// <param name="nowUtc">The current server time in UTC.</param>
    /// <returns>The outcome of the edit attempt.</returns>
    internal static DraftEditOutcome EditDraft(
        Expense expense,
        string actorId,
        ExpenseDraftValues values,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentNullException.ThrowIfNull(values);

        if (!string.Equals(expense.OwnerId, actorId, StringComparison.Ordinal))
        {
            return DraftEditOutcome.NotOwner;
        }

        if (expense.Status != ExpenseStatus.Draft)
        {
            return DraftEditOutcome.NotDraft;
        }

        Dictionary<string, FieldChange> changes = new Dictionary<string, FieldChange>(StringComparer.Ordinal);

        if (!string.Equals(expense.Description, values.Description, StringComparison.Ordinal))
        {
            changes.Add("description", new FieldChange { From = expense.Description, To = values.Description });
            expense.Description = values.Description;
        }

        if (expense.Amount != values.Amount)
        {
            changes.Add("amount", new FieldChange { From = expense.Amount, To = values.Amount });
            expense.Amount = values.Amount;
        }

        if (expense.ExpenseDate != values.ExpenseDate)
        {
            changes.Add("expenseDate", new FieldChange { From = expense.ExpenseDate, To = values.ExpenseDate });
            expense.ExpenseDate = values.ExpenseDate;
        }

        if (expense.ExpenseCategoryId != values.ExpenseCategoryId)
        {
            changes.Add("categoryId", new FieldChange { From = expense.ExpenseCategoryId, To = values.ExpenseCategoryId });
            expense.ExpenseCategoryId = values.ExpenseCategoryId;
        }

        if (changes.Count == 0)
        {
            return DraftEditOutcome.Unchanged;
        }

        expense.History.Add(new ExpenseHistory
        {
            ExpenseId = expense.Id,
            Action = ExpenseHistoryActions.Updated,
            ActorId = actorId,
            OccurredAtUtc = nowUtc.ToUniversalTime(),
            PreviousStatus = ExpenseStatus.Draft,
            NewStatus = ExpenseStatus.Draft,
            Changes = JsonSerializer.Serialize(changes, ChangesSerializerOptions),
        });

        return DraftEditOutcome.Updated;
    }

    /// <summary>Submits a draft expense when the actor owns it and it is still a draft.</summary>
    /// <param name="expense">The expense to submit.</param>
    /// <param name="actorId">The identifier of the authenticated actor.</param>
    /// <param name="nowUtc">The current server time in UTC.</param>
    /// <returns>The outcome of the submission attempt.</returns>
    internal static DraftSubmitOutcome Submit(Expense expense, string actorId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        if (!string.Equals(expense.OwnerId, actorId, StringComparison.Ordinal))
        {
            return DraftSubmitOutcome.NotOwner;
        }

        if (expense.Status != ExpenseStatus.Draft)
        {
            return DraftSubmitOutcome.NotDraft;
        }

        expense.Status = ExpenseStatus.Submitted;
        expense.History.Add(new ExpenseHistory
        {
            ExpenseId = expense.Id,
            Action = ExpenseHistoryActions.Submitted,
            ActorId = actorId,
            OccurredAtUtc = nowUtc.ToUniversalTime(),
            PreviousStatus = ExpenseStatus.Draft,
            NewStatus = ExpenseStatus.Submitted,
        });

        return DraftSubmitOutcome.Submitted;
    }

    private sealed class FieldChange
    {
        public object? From { get; init; }

        public object? To { get; init; }
    }
}
