namespace ExpenseHub.Api.Reports;

/// <summary>Defines persisted report processing states.</summary>
internal enum ReportStatus
{
    /// <summary>Waiting for processing.</summary>
    Pending,

    /// <summary>Processing has started.</summary>
    Processing,

    /// <summary>The immutable result is available.</summary>
    Completed,

    /// <summary>Processing failed with a sanitized error.</summary>
    Failed,
}
