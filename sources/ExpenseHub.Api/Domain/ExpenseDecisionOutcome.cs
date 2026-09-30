namespace ExpenseHub.Api.Domain;

/// <summary>
/// Describes the result of an attempt to approve or reject an expense.
/// </summary>
internal enum ExpenseDecisionOutcome
{
    /// <summary>The decision was applied and a history entry was recorded.</summary>
    Applied = 0,

    /// <summary>The actor owns the expense and cannot decide on it.</summary>
    SelfDecision = 1,

    /// <summary>The expense is not in the <see cref="ExpenseStatus.Submitted"/> state.</summary>
    NotSubmitted = 2,
}
