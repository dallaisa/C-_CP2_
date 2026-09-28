namespace ExpenseHub.Api.Domain;

using System;

/// <summary>
/// Records payment of an expense.
/// </summary>
internal sealed class PaymentRecord
{
    /// <summary>Gets or sets the unique identifier of the payment record.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the identifier of the paid expense.</summary>
    public Guid ExpenseId { get; set; }

    /// <summary>Gets or sets the related expense.</summary>
    public Expense Expense { get; set; } = null!;

    /// <summary>Gets or sets the identifier of the actor who recorded the payment.</summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC time when payment was recorded.</summary>
    public DateTimeOffset PaidAtUtc { get; set; }
}
