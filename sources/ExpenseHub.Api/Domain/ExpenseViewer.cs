namespace ExpenseHub.Api.Domain;

/// <summary>
/// Describes the authenticated user who is reading expenses.
/// </summary>
/// <remarks>
/// Only functional roles are represented; the Admin role grants no access to expenses.
/// </remarks>
internal sealed class ExpenseViewer
{
    /// <summary>Gets the identifier of the authenticated user.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the user has the Employee role.</summary>
    public bool IsEmployee { get; init; }

    /// <summary>Gets a value indicating whether the user has the Approver role.</summary>
    public bool IsApprover { get; init; }

    /// <summary>Gets a value indicating whether the user has the Finance role.</summary>
    public bool IsFinance { get; init; }

    /// <summary>Gets a value indicating whether the user has the Auditor role.</summary>
    public bool IsAuditor { get; init; }
}
