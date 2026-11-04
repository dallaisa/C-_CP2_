using System;

namespace ExpenseHub.Api.Reports;

/// <summary>Identifies a saved request whose broker publication has not been confirmed.</summary>
internal sealed record ReportPublicationFailure(Guid Id, ReportError Error);
