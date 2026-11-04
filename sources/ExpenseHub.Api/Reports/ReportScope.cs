namespace ExpenseHub.Api.Reports;

/// <summary>Defines the permitted expense populations.</summary>
internal enum ReportScope
{
    /// <summary>Only the requester's expenses.</summary>
    Mine,

    /// <summary>All employees' expenses.</summary>
    Global,
}
