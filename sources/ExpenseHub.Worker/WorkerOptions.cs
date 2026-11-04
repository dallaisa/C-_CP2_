using System;

namespace ExpenseHub.Worker;

/// <summary>Bounds retry attempts and avoids hot redelivery loops.</summary>
internal sealed class WorkerOptions
{
    /// <summary>Gets or sets attempts before stopping without ACK.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Gets or sets delay between failed attempts.</summary>
    public int RetryDelaySeconds { get; set; } = 5;

    /// <summary>Gets or sets the empty-queue polling interval.</summary>
    public int PollIntervalMilliseconds { get; set; } = 500;

    /// <summary>Validates bounds without hiding configuration errors.</summary>
    internal void Validate()
    {
        if (MaxAttempts is < 1 or > 20 || RetryDelaySeconds is < 1 or > 300
            || PollIntervalMilliseconds is < 50 or > 60000)
        {
            throw new InvalidOperationException("Invalid Worker retry or polling settings.");
        }
    }
}
