namespace ExpenseHub.Api.Expenses;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Applies ownership, state and validation rules to draft expense operations.
/// </summary>
/// <param name="dbContext">The ExpenseHub database context.</param>
/// <param name="timeProvider">The provider of the current server time.</param>
internal sealed class ExpenseService(ExpenseHubDbContext dbContext, TimeProvider timeProvider)
{
    private static readonly string[] InvalidCategoryErrors = new[] { "CategoryId must be a valid category." };

    /// <summary>Creates a draft expense owned by the authenticated user.</summary>
    /// <param name="ownerId">The identifier of the authenticated user.</param>
    /// <param name="request">The draft values sent by the client.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The operation result.</returns>
    internal async Task<ExpenseOperationResult> CreateDraftAsync(
        string ownerId,
        ExpenseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        Dictionary<string, string[]> errors =
            ExpenseRequestValidator.Validate(request, DateOnly.FromDateTime(nowUtc.UtcDateTime));
        if (errors.Count > 0)
        {
            return ExpenseOperationResult.Invalid(errors);
        }

        ExpenseDraftValues values = ExpenseRequestValidator.ToDraftValues(request);
        ExpenseCategory? category = await FindCategoryAsync(dbContext, values.ExpenseCategoryId, cancellationToken);
        if (category is null)
        {
            return InvalidCategory();
        }

        Expense expense = ExpenseDraftRules.CreateDraft(ownerId, values, nowUtc);
        expense.Category = category;

        // The expense and its history entry are saved by the same SaveChanges call.
        dbContext.Expenses.Add(expense);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ExpenseOperationResult.Success(expense);
    }

    /// <summary>Edits a draft expense owned by the authenticated user.</summary>
    /// <param name="expenseId">The identifier of the expense to edit.</param>
    /// <param name="actorId">The identifier of the authenticated user.</param>
    /// <param name="request">The draft values sent by the client.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The operation result.</returns>
    internal async Task<ExpenseOperationResult> UpdateDraftAsync(
        Guid expenseId,
        string actorId,
        ExpenseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        Dictionary<string, string[]> errors =
            ExpenseRequestValidator.Validate(request, DateOnly.FromDateTime(nowUtc.UtcDateTime));
        if (errors.Count > 0)
        {
            return ExpenseOperationResult.Invalid(errors);
        }

        // Ownership is part of the query: another user's expense is indistinguishable from a missing one.
        Expense? expense = await dbContext.Expenses
            .FirstOrDefaultAsync(
                item => item.Id == expenseId && item.OwnerId == actorId,
                cancellationToken);
        if (expense is null)
        {
            return ExpenseOperationResult.NotFound();
        }

        if (expense.Status != ExpenseStatus.Draft)
        {
            return ExpenseOperationResult.Conflict();
        }

        ExpenseDraftValues values = ExpenseRequestValidator.ToDraftValues(request);
        ExpenseCategory? category = await FindCategoryAsync(dbContext, values.ExpenseCategoryId, cancellationToken);
        if (category is null)
        {
            return InvalidCategory();
        }

        DraftEditOutcome outcome = ExpenseDraftRules.EditDraft(expense, actorId, values, nowUtc);
        if (outcome == DraftEditOutcome.NotOwner)
        {
            return ExpenseOperationResult.NotFound();
        }

        if (outcome == DraftEditOutcome.NotDraft)
        {
            return ExpenseOperationResult.Conflict();
        }

        expense.Category = category;
        if (outcome == DraftEditOutcome.Updated)
        {
            // The changed expense and its history entry are saved by the same SaveChanges call.
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ExpenseOperationResult.Success(expense);
    }

    private static ExpenseOperationResult InvalidCategory() =>
        ExpenseOperationResult.Invalid(new Dictionary<string, string[]>
        {
            [nameof(ExpenseRequest.CategoryId)] = InvalidCategoryErrors,
        });

    private static Task<ExpenseCategory?> FindCategoryAsync(
        ExpenseHubDbContext context,
        int categoryId,
        CancellationToken cancellationToken) =>
        context.ExpenseCategories.FirstOrDefaultAsync(
            category => category.Id == categoryId,
            cancellationToken);
}
