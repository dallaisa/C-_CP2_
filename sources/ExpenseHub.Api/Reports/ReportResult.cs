using System;
using System.Collections.Generic;

namespace ExpenseHub.Api.Reports;

/// <summary>Contains the immutable calculated snapshot.</summary>
internal sealed record ReportResult(
    DateTimeOffset GeneratedAt,
    int TotalCount,
    decimal TotalAmount,
    IReadOnlyList<ReportStatusTotal> ByStatus);

/// <summary>Contains a count and decimal sum for one expense state.</summary>
internal sealed record ReportStatusTotal(string Status, int Count, decimal Amount);

/// <summary>Contains only a stable code and a safe, server-defined message.</summary>
internal sealed record ReportError(string Code, string Message);
