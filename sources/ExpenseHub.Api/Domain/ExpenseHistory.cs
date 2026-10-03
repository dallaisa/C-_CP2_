using System;

namespace ExpenseHub.Api.Domain;

/// <summary>
/// Records an action performed on an expense.
/// </summary>
internal sealed class ExpenseHistory
{
    /// <summary>Gets or sets the unique identifier of the history entry.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the identifier of the related expense.</summary>
    public Guid ExpenseId { get; set; }

    /// <summary>Gets or sets the related expense.</summary>
    public Expense Expense { get; set; } = null!;

    /// <summary>Gets or sets the action recorded in this entry.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Gets or sets the identifier of the actor who performed the action.</summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC time when the action occurred.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Gets or sets the status before the action, if available.</summary>
    public ExpenseStatus? PreviousStatus { get; set; }

    /// <summary>Gets or sets the status after the action, if available.</summary>
    public ExpenseStatus? NewStatus { get; set; }

    /// <summary>Gets or sets the reason provided when the expense was rejected.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Gets or sets serialized details of the changes.</summary>
    public string? Changes { get; set; }
}
