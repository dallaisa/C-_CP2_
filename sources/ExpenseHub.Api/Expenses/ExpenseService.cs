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
    /// <param name="actor">The authenticated user, who becomes the owner.</param>
    /// <param name="request">The draft values sent by the client.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The operation result.</returns>
    internal async Task<ExpenseOperationResult> CreateDraftAsync(
        ExpenseViewer actor,
        ExpenseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        if (!actor.IsEmployee || string.IsNullOrWhiteSpace(actor.UserId))
        {
            return ExpenseOperationResult.Forbidden();
        }

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

        Expense expense = ExpenseDraftRules.CreateDraft(actor.UserId, values, nowUtc);
        expense.Category = category;

        // The expense and its history entry are saved by the same SaveChanges call.
        dbContext.Expenses.Add(expense);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ExpenseOperationResult.Success(expense);
    }

    /// <summary>Edits a draft expense owned by the authenticated user.</summary>
    /// <param name="expenseId">The identifier of the expense to edit.</param>
    /// <param name="actor">The authenticated user.</param>
    /// <param name="request">The draft values sent by the client.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The operation result.</returns>
    internal async Task<ExpenseOperationResult> UpdateDraftAsync(
        Guid expenseId,
        ExpenseViewer actor,
        ExpenseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        Dictionary<string, string[]> errors =
            ExpenseRequestValidator.Validate(request, DateOnly.FromDateTime(nowUtc.UtcDateTime));
        if (errors.Count > 0)
        {
            return ExpenseOperationResult.Invalid(errors);
        }

        Expense? expense = await FindOwnedAsync(dbContext, expenseId, actor, includeCategory: false, cancellationToken);
        ExpenseAccessDecision decision = ExpenseAccessPolicy.EvaluateDraftChange(actor, expense);
        if (decision != ExpenseAccessDecision.Allowed)
        {
            return FromDecision(decision, EditConflictMessage);
        }

        ExpenseDraftValues values = ExpenseRequestValidator.ToDraftValues(request);
        ExpenseCategory? category = await FindCategoryAsync(dbContext, values.ExpenseCategoryId, cancellationToken);
        if (category is null)
        {
            return InvalidCategory();
        }

        DraftEditOutcome outcome = ExpenseDraftRules.EditDraft(expense!, actor.UserId, values, nowUtc);
        if (outcome == DraftEditOutcome.NotOwner)
        {
            return ExpenseOperationResult.NotFound();
        }

        if (outcome == DraftEditOutcome.NotDraft)
        {
            return ExpenseOperationResult.Conflict(EditConflictMessage);
        }

        expense!.Category = category;

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
    /// <param name="actor">The authenticated user.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The operation result.</returns>
    internal async Task<ExpenseOperationResult> SubmitAsync(
        Guid expenseId,
        ExpenseViewer actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        Expense? expense = await FindOwnedAsync(dbContext, expenseId, actor, includeCategory: true, cancellationToken);
        ExpenseAccessDecision decision = ExpenseAccessPolicy.EvaluateDraftChange(actor, expense);

        // A repeated submission finds the expense outside Draft and records nothing.
        if (decision != ExpenseAccessDecision.Allowed)
        {
            return FromDecision(decision, SubmitConflictMessage);
        }

        DraftSubmitOutcome outcome = ExpenseDraftRules.Submit(expense!, actor.UserId, timeProvider.GetUtcNow());
        if (outcome == DraftSubmitOutcome.NotOwner)
        {
            return ExpenseOperationResult.NotFound();
        }

        if (outcome == DraftSubmitOutcome.NotDraft)
        {
            return ExpenseOperationResult.Conflict(SubmitConflictMessage);
        }

        // The new state and its history entry are saved by the same SaveChanges call.
        if (!await TrySaveAsync(dbContext, cancellationToken))
        {
            return ExpenseOperationResult.Conflict(SubmitConflictMessage);
        }

        return ExpenseOperationResult.Success(expense!);
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

    private static ExpenseOperationResult FromDecision(ExpenseAccessDecision decision, string conflictMessage) =>
        decision switch
        {
            ExpenseAccessDecision.Forbidden => ExpenseOperationResult.Forbidden(),
            ExpenseAccessDecision.Conflict => ExpenseOperationResult.Conflict(conflictMessage),
            _ => ExpenseOperationResult.NotFound(),
        };

    private static Task<Expense?> FindOwnedAsync(
        ExpenseHubDbContext context,
        Guid expenseId,
        ExpenseViewer actor,
        bool includeCategory,
        CancellationToken cancellationToken)
    {
        // Ownership is part of the query: another user's expense is indistinguishable from a missing one.
        string ownerId = actor.UserId;
        IQueryable<Expense> query = context.Expenses;
        if (includeCategory)
        {
            query = query.Include(item => item.Category);
        }

        return query.FirstOrDefaultAsync(
            item => item.Id == expenseId && item.OwnerId == ownerId,
            cancellationToken);
    }

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
