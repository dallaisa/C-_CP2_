using System;
using System.Text.Json.Nodes;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Expenses;

/// <summary>
/// Represents a history entry returned by the API.
/// </summary>
internal sealed class ExpenseHistoryResponse
{
    /// <summary>Gets the history entry identifier.</summary>
    public long Id { get; init; }

    /// <summary>Gets the identifier of the related expense.</summary>
    public Guid ExpenseId { get; init; }

    /// <summary>Gets the recorded action.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Gets the identifier of the user who performed the action.</summary>
    public string ActorId { get; init; } = string.Empty;

    /// <summary>Gets the UTC time when the action occurred.</summary>
    public DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Gets the status before the action, if any.</summary>
    public string? PreviousStatus { get; init; }

    /// <summary>Gets the status after the action, if any.</summary>
    public string? NewStatus { get; init; }

    /// <summary>Gets the rejection justification, when the action is a rejection.</summary>
    public string? RejectionReason { get; init; }

    /// <summary>Gets the fields changed while the expense was a draft, when the action is an edit.</summary>
    public JsonNode? Changes { get; init; }

    /// <summary>Creates a response from a history entry.</summary>
    /// <param name="entry">The history entry.</param>
    /// <returns>The response DTO.</returns>
    internal static ExpenseHistoryResponse FromHistory(ExpenseHistory entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new ExpenseHistoryResponse
        {
            Id = entry.Id,
            ExpenseId = entry.ExpenseId,
            Action = entry.Action,
            ActorId = entry.ActorId,
            OccurredAtUtc = entry.OccurredAtUtc,
            PreviousStatus = entry.PreviousStatus?.ToString(),
            NewStatus = entry.NewStatus?.ToString(),
            RejectionReason = entry.RejectionReason,
            Changes = entry.Changes is null ? null : JsonNode.Parse(entry.Changes),
        };
    }
}
