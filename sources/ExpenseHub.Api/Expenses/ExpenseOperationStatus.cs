namespace ExpenseHub.Api.Expenses;

/// <summary>
/// Describes the outcome category of an expense operation.
/// </summary>
internal enum ExpenseOperationStatus
{
    /// <summary>The operation succeeded.</summary>
    Success = 0,

    /// <summary>The request input is invalid.</summary>
    Invalid = 1,

    /// <summary>The expense does not exist or is outside the caller's scope.</summary>
    NotFound = 2,

    /// <summary>The expense is in a state that does not allow the operation.</summary>
    Conflict = 3,
}
