namespace ExpenseHub.Api.Domain;

/// <summary>
/// Describes the result of an attempt to edit a draft expense.
/// </summary>
internal enum DraftEditOutcome
{
    /// <summary>The draft was changed and a history entry was recorded.</summary>
    Updated = 0,

    /// <summary>The submitted values match the current draft; nothing was recorded.</summary>
    Unchanged = 1,

    /// <summary>The actor does not own the expense.</summary>
    NotOwner = 2,

    /// <summary>The expense is no longer in the <see cref="ExpenseStatus.Draft"/> state.</summary>
    NotDraft = 3,
}
