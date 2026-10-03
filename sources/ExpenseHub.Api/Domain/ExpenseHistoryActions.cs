namespace ExpenseHub.Api.Domain;

/// <summary>
/// Contains the action names recorded in the expense history.
/// </summary>
internal static class ExpenseHistoryActions
{
    /// <summary>Gets the action recorded when a draft is created.</summary>
    internal const string Created = "Created";

    /// <summary>Gets the action recorded when a draft is edited.</summary>
    internal const string Updated = "Updated";

    /// <summary>Gets the action recorded when a draft is submitted for approval.</summary>
    internal const string Submitted = "Submitted";

    /// <summary>Gets the action recorded when a submitted expense is approved.</summary>
    internal const string Approved = "Approved";

    /// <summary>Gets the action recorded when a submitted expense is rejected.</summary>
    internal const string Rejected = "Rejected";

    /// <summary>Gets the action recorded when an approved expense is paid.</summary>
    internal const string Paid = "Paid";
}
