using System;

namespace ExpenseHub.Api.Domain;

/// <summary>
/// Holds the already validated values that a client may set on a draft expense.
/// </summary>
internal sealed class ExpenseDraftValues
{
    /// <summary>Gets the expense description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the expense amount.</summary>
    public decimal Amount { get; init; }

    /// <summary>Gets the date on which the expense occurred.</summary>
    public DateOnly ExpenseDate { get; init; }

    /// <summary>Gets the identifier of the expense category.</summary>
    public int ExpenseCategoryId { get; init; }
}
