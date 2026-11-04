using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Messaging;
using ExpenseHub.Api.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExpenseHub.Worker;

/// <summary>Creates a fresh persistence scope per attempt and never acknowledges failed persistence.</summary>
internal sealed class ReportDeliveryHandler(
    IServiceScopeFactory scopes, IOptions<WorkerOptions> options, TimeProvider clock,
    ILogger<ReportDeliveryHandler> logger)
{
    /// <summary>Processes a delivery with bounded retries; exhaustion closes the worker without ACK.</summary>
    internal async Task HandleAsync(IReportDelivery delivery, CancellationToken cancellationToken)
    {
        ReportMessage? message = ReportMessage.Parse(delivery.Body);
        if (message is null)
        {
            logger.LogWarning("Rejecting invalid report message without requeue");
            await delivery.RejectAsync(cancellationToken);
            return;
        }

        WorkerOptions settings = options.Value;
        settings.Validate();
        for (int attempt = 1; attempt <= settings.MaxAttempts; attempt++)
        {
            bool saved = false;
            bool missing = false;
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                IReportStore store = scope.ServiceProvider.GetRequiredService<IReportStore>();
                missing = await store.FindAsync(message.ReportId, cancellationToken) is null;
                if (!missing)
                {
                    saved = await scope.ServiceProvider.GetRequiredService<ReportProcessor>()
                        .ProcessAsync(message.ReportId, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Report {ReportId} attempt {Attempt} did not persist: {ErrorType}",
                    message.ReportId, attempt, exception.GetType().Name);
            }

            // Broker acknowledgements are outside the processing catch. If ACK fails, close the
            // connection and let the broker redeliver; do not turn an ACK failure into calculation.
            if (missing)
            {
                logger.LogWarning("Report {ReportId} does not exist; rejecting without requeue", message.ReportId);
                await delivery.RejectAsync(cancellationToken);
                return;
            }

            if (saved)
            {
                await delivery.AckAsync(cancellationToken);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Report {ReportId} delivery acknowledged after persistence", message.ReportId);
                }

                return;
            }

            if (attempt < settings.MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds), clock, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Report {message.ReportId} could not be persisted; stopping without ACK.");
    }
}
