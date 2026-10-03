namespace ExpenseHub.Api.Domain;

/// <summary>
/// Describes the result of an attempt to pay an expense.
/// </summary>
internal enum ExpensePaymentOutcome
{
    /// <summary>The payment record and its history entry were created.</summary>
    Paid = 0,

    /// <summary>The actor owns the expense and cannot pay it.</summary>
    SelfPayment = 1,

    /// <summary>The expense is not in the <see cref="ExpenseStatus.Approved"/> state.</summary>
    NotApproved = 2,
}
