namespace ExpenseHub.Api.Domain;

/// <summary>
/// Describes the contextual authorization result of an operation on an expense.
/// </summary>
internal enum ExpenseAccessDecision
{
    /// <summary>The operation is allowed.</summary>
    Allowed = 0,

    /// <summary>The expense does not exist or is outside the actor's scope; maps to 404.</summary>
    NotFound = 1,

    /// <summary>The actor can see the expense but may not perform the operation; maps to 403.</summary>
    Forbidden = 2,

    /// <summary>The expense state does not allow the operation; maps to 409.</summary>
    Conflict = 3,
}
