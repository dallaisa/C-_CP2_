using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Reports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ExpenseHub.Api.Messaging;

/// <summary>Serializes access to a long-lived confirm-enabled channel and awaits real broker confirms.</summary>
internal sealed class RabbitMqReportPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqReportPublisher> logger)
    : IReportPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    /// <inheritdoc />
    public async Task<bool> PublishConfirmedAsync(Guid reportId, CancellationToken cancellationToken)
    {
        RabbitMqOptions settings = options.Value;
        ConnectionFactory factory = settings.CreateFactory();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        await _gate.WaitAsync(timeout.Token);
        try
        {
            if (_channel is null || !_channel.IsOpen || _connection is null || !_connection.IsOpen)
            {
                await CloseAsync();
                _connection = await factory.CreateConnectionAsync(timeout.Token);
                _channel = await _connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                    timeout.Token);
                await _channel.QueueDeclareAsync(settings.QueueName, durable: true, exclusive: false,
                    autoDelete: false, cancellationToken: timeout.Token);
            }

            BasicProperties properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = reportId.ToString(),
                CorrelationId = reportId.ToString(),
            };
            // With confirms AND tracking enabled, await completes only on broker ACK; nack/return throws.
            await _channel.BasicPublishAsync(string.Empty, settings.QueueName, mandatory: true,
                properties, new ReportMessage(reportId).Serialize(), timeout.Token);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Report {ReportId} publication confirmed", reportId);
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _gate.Dispose();
    }

    private async Task CloseAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
