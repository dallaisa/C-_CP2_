namespace ExpenseHub.Api.Expenses;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Applies ownership, state, visibility and validation rules to expense operations.
/// </summary>
/// <param name="dbContext">The ExpenseHub database context.</param>
/// <param name="timeProvider">The provider of the current server time.</param>
internal sealed class ExpenseService(ExpenseHubDbContext dbContext, TimeProvider timeProvider)
{
    private const string EditConflictMessage = "Only expenses in Draft can be edited.";

    private const string SubmitConflictMessage = "Only expenses in Draft can be submitted.";

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
            return ExpenseOperationResult.Conflict(EditConflictMessage);
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
            return ExpenseOperationResult.Conflict(EditConflictMessage);
        }

        expense.Category = category;

        // The changed expense and its history entry are saved by the same SaveChanges call.
        if (outcome == DraftEditOutcome.Updated
            && !await TrySaveAsync(dbContext, cancellationToken))
        {
            return ExpenseOperationResult.Conflict(EditConflictMessage);
        }

        return ExpenseOperationResult.Success(expense);
    }

    /// <summary>Submits a draft expense owned by the authenticated user.</summary>
    /// <param name="expenseId">The identifier of the expense to submit.</param>
    /// <param name="actorId">The identifier of the authenticated user.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The operation result.</returns>
    internal async Task<ExpenseOperationResult> SubmitAsync(
        Guid expenseId,
        string actorId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        // Ownership is part of the query: another user's expense is indistinguishable from a missing one.
        Expense? expense = await dbContext.Expenses
            .Include(item => item.Category)
            .FirstOrDefaultAsync(
                item => item.Id == expenseId && item.OwnerId == actorId,
                cancellationToken);
        if (expense is null)
        {
            return ExpenseOperationResult.NotFound();
        }

        DraftSubmitOutcome outcome = ExpenseDraftRules.Submit(expense, actorId, timeProvider.GetUtcNow());
        if (outcome == DraftSubmitOutcome.NotOwner)
        {
            return ExpenseOperationResult.NotFound();
        }

        // A repeated submission finds the expense outside Draft and records nothing.
        if (outcome == DraftSubmitOutcome.NotDraft)
        {
            return ExpenseOperationResult.Conflict(SubmitConflictMessage);
        }

        // The new state and its history entry are saved by the same SaveChanges call.
        if (!await TrySaveAsync(dbContext, cancellationToken))
        {
            return ExpenseOperationResult.Conflict(SubmitConflictMessage);
        }

        return ExpenseOperationResult.Success(expense);
    }

    /// <summary>Lists the expenses visible to the authenticated user.</summary>
    /// <param name="viewer">The authenticated viewer.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The visible expenses, most recent expense date first.</returns>
    internal async Task<List<Expense>> ListVisibleAsync(
        ExpenseViewer viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        return await dbContext.Expenses
            .AsNoTracking()
            .Where(ExpenseVisibility.VisibleTo(viewer))
            .Include(expense => expense.Category)
            .OrderByDescending(expense => expense.ExpenseDate)
            .ThenBy(expense => expense.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Finds an expense when it is visible to the authenticated user.</summary>
    /// <param name="expenseId">The identifier of the expense.</param>
    /// <param name="viewer">The authenticated viewer.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The expense, or <see langword="null"/> when it does not exist or is not visible.</returns>
    internal Task<Expense?> FindVisibleAsync(
        Guid expenseId,
        ExpenseViewer viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        return dbContext.Expenses
            .AsNoTracking()
            .Where(ExpenseVisibility.VisibleTo(viewer))
            .Include(expense => expense.Category)
            .FirstOrDefaultAsync(expense => expense.Id == expenseId, cancellationToken);
    }

    private static ExpenseOperationResult InvalidCategory() =>
        ExpenseOperationResult.Invalid(new Dictionary<string, string[]>
        {
            [nameof(ExpenseRequest.CategoryId)] = InvalidCategoryErrors,
        });

    private static async Task<bool> TrySaveAsync(
        ExpenseHubDbContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request changed the status first; nothing from this request was saved.
            return false;
        }
    }

    private static Task<ExpenseCategory?> FindCategoryAsync(
        ExpenseHubDbContext context,
        int categoryId,
        CancellationToken cancellationToken) =>
        context.ExpenseCategories.FirstOrDefaultAsync(
            category => category.Id == categoryId,
            cancellationToken);
}
