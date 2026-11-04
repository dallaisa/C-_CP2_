using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExpenseHub.Worker;

/// <summary>A manually acknowledged broker delivery; keeps ACK behavior independently testable.</summary>
internal interface IReportDelivery
{
    /// <summary>Gets the message body.</summary>
    ReadOnlyMemory<byte> Body { get; }

    /// <summary>Acknowledges only a persisted terminal state.</summary>
    Task AckAsync(CancellationToken cancellationToken);

    /// <summary>Rejects a malformed or missing-ID message without requeue.</summary>
    Task RejectAsync(CancellationToken cancellationToken);
}
