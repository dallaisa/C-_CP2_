using System;
using System.Text.Json;

namespace ExpenseHub.Api.Reports;

/// <summary>Stores a report request and its serialized, immutable result.</summary>
internal sealed class Report
{
    /// <summary>Gets the server-generated identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the authenticated requester.</summary>
    public string RequestedByUserId { get; init; } = string.Empty;

    /// <summary>Gets the inclusive first expense date.</summary>
    public DateOnly FromDate { get; init; }

    /// <summary>Gets the inclusive last expense date.</summary>
    public DateOnly ToDate { get; init; }

    /// <summary>Gets the authorized scope.</summary>
    public ReportScope Scope { get; init; }

    /// <summary>Gets or sets the processing state, used for optimistic concurrency.</summary>
    public ReportStatus Status { get; set; }

    /// <summary>Gets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets or sets the UTC processing start.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Gets or sets the UTC terminal instant.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Gets or sets the persisted snapshot, including GeneratedAt.</summary>
    public string? ResultJson { get; set; }

    /// <summary>Gets or sets the safe error code.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Gets or sets the safe error message.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Reads the stored result without consulting expenses.</summary>
    internal ReportResult? ReadResult() => ResultJson is null
        ? null : JsonSerializer.Deserialize<ReportResult>(ResultJson);
}
