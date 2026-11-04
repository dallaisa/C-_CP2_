using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExpenseHub.Api.Reports;

/// <summary>Integration boundary for I03; implementations must await actual broker confirmation.</summary>
internal interface IReportPublisher
{
    /// <summary>Publishes the persisted report ID and returns true only after broker confirmation.</summary>
    Task<bool> PublishConfirmedAsync(Guid reportId, CancellationToken cancellationToken);
}
