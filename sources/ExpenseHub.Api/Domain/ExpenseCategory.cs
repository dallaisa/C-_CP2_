namespace ExpenseHub.Api.Domain;

using System.Collections.Generic;

/// <summary>
/// Defines a category used to classify expenses.
/// </summary>
internal sealed class ExpenseCategory
{
    /// <summary>Gets or sets the unique identifier of the category.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the category name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets the expenses assigned to this category.</summary>
    public ICollection<Expense> Expenses { get; } = new List<Expense>();
}
