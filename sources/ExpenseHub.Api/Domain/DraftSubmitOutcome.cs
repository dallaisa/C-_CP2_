namespace ExpenseHub.Api.Domain;

/// <summary>
/// Describes the result of an attempt to submit a draft expense.
/// </summary>
internal enum DraftSubmitOutcome
{
    /// <summary>The draft moved to <see cref="ExpenseStatus.Submitted"/> and a history entry was recorded.</summary>
    Submitted = 0,

    /// <summary>The actor does not own the expense.</summary>
    NotOwner = 1,

    /// <summary>The expense is no longer in the <see cref="ExpenseStatus.Draft"/> state.</summary>
    NotDraft = 2,
}
