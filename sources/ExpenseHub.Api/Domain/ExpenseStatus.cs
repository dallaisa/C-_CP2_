namespace ExpenseHub.Api.Domain;

/// <summary>
/// Defines the lifecycle states of an expense.
/// </summary>
internal enum ExpenseStatus
{
    /// <summary>The expense is being drafted.</summary>
    Draft = 0,

    /// <summary>The expense has been submitted for review.</summary>
    Submitted = 1,

    /// <summary>The expense has been approved.</summary>
    Approved = 2,

    /// <summary>The expense has been rejected.</summary>
    Rejected = 3,

    /// <summary>The expense has been paid.</summary>
    Paid = 4,
}
