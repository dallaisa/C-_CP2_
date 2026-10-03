using System;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Expenses;

/// <summary>
/// Represents an expense returned by the API.
/// </summary>
internal sealed class ExpenseResponse
{
    /// <summary>Gets the expense identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the identifier of the expense owner.</summary>
    public string OwnerId { get; init; } = string.Empty;

    /// <summary>Gets the expense description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the expense amount.</summary>
    public decimal Amount { get; init; }

    /// <summary>Gets the date on which the expense occurred.</summary>
    public DateOnly ExpenseDate { get; init; }

    /// <summary>Gets the current expense status.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the identifier of the expense category.</summary>
    public int CategoryId { get; init; }

    /// <summary>Gets the name of the expense category, when loaded.</summary>
    public string? CategoryName { get; init; }

    /// <summary>Gets the identifier of the Finance user who paid the expense, when paid.</summary>
    public string? PaidByUserId { get; init; }

    /// <summary>Gets the UTC time of the payment, when paid.</summary>
    public DateTimeOffset? PaidAtUtc { get; init; }

    /// <summary>Creates a response from an expense entity.</summary>
    /// <param name="expense">The expense entity.</param>
    /// <returns>The response DTO.</returns>
    internal static ExpenseResponse FromExpense(Expense expense)
    {
        ArgumentNullException.ThrowIfNull(expense);

        return new ExpenseResponse
        {
            Id = expense.Id,
            OwnerId = expense.OwnerId,
            Description = expense.Description,
            Amount = expense.Amount,
            ExpenseDate = expense.ExpenseDate,
            Status = expense.Status.ToString(),
            CategoryId = expense.ExpenseCategoryId,
            CategoryName = expense.Category?.Name,
            PaidByUserId = expense.Payment?.ActorId,
            PaidAtUtc = expense.Payment?.PaidAtUtc,
        };
    }
}
