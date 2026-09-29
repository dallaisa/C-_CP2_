namespace ExpenseHub.Api.Identity;

/// <summary>
/// Contains the roles used to authorize application users.
/// </summary>
internal static class ApplicationRoles
{
    /// <summary>Gets the administrator role name.</summary>
    internal const string Admin = "Admin";

    /// <summary>Gets the employee role name.</summary>
    internal const string Employee = "Employee";

    /// <summary>Gets the approver role name.</summary>
    internal const string Approver = "Approver";

    /// <summary>Gets the finance role name.</summary>
    internal const string Finance = "Finance";

    /// <summary>Gets the auditor role name.</summary>
    internal const string Auditor = "Auditor";
}
