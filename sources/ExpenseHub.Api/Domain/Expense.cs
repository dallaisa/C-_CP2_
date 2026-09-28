namespace ExpenseHub.Api.Domain;

using System;
using System.Collections.Generic;

/// <summary>
/// Represents an expense submitted by an owner.
/// </summary>
internal sealed class Expense
{
    /// <summary>Gets or sets the unique identifier of the expense.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the identifier of the expense owner.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Gets or sets the expense description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the expense amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Gets or sets the date on which the expense occurred.</summary>
    public DateOnly ExpenseDate { get; set; }

    /// <summary>Gets or sets the current approval status.</summary>
    public ExpenseStatus Status { get; set; } = ExpenseStatus.Draft;

    /// <summary>Gets or sets the identifier of the expense category.</summary>
    public int ExpenseCategoryId { get; set; }

    /// <summary>Gets or sets the category associated with the expense.</summary>
    public ExpenseCategory Category { get; set; } = null!;

    /// <summary>Gets the history entries associated with the expense.</summary>
    public ICollection<ExpenseHistory> History { get; } = new List<ExpenseHistory>();

    /// <summary>Gets or sets the payment record, if the expense has been paid.</summary>
    public PaymentRecord? Payment { get; set; }
}
