using System;
using System.Text.Json.Serialization;

namespace ExpenseHub.Api.Reports;

/// <summary>Accepts only the three client-controlled report fields.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ReportRequest(DateOnly? FromDate, DateOnly? ToDate, string? Scope)
{
    /// <summary>Checks required dates, ordering and the exact scope names.</summary>
    internal bool IsValid => FromDate.HasValue && ToDate.HasValue
        && FromDate <= ToDate && Scope is "Mine" or "Global";
}
