using System;

namespace ExpenseHub.Api.Reports;

/// <summary>Defines the report envelope shared by creation, listing and detail.</summary>
internal sealed record ReportResponse(
    Guid Id,
    string Scope,
    DateOnly FromDate,
    DateOnly ToDate,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    ReportResult? Result,
    ReportError? Error)
{
    /// <summary>Builds an envelope exclusively from persisted report data.</summary>
    internal static ReportResponse FromReport(Report report) => new(
        report.Id, report.Scope.ToString(), report.FromDate, report.ToDate,
        report.Status.ToString(), report.CreatedAt, report.StartedAt, report.CompletedAt,
        report.ReadResult(), report.ErrorCode is null ? null
            : new ReportError(report.ErrorCode, report.ErrorMessage ?? string.Empty));
}
