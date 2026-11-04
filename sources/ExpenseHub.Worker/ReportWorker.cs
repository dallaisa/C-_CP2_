using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ExpenseHub.Worker;

/// <summary>Consumes one delivery at a time through a long-lived connection with manual acknowledgements.</summary>
internal sealed class ReportWorker(
    IOptions<RabbitMqOptions> brokerOptions, IOptions<WorkerOptions> workerOptions,
    ReportDeliveryHandler handler, ILogger<ReportWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ConsumeAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Disposal closes the channel and returns any unacknowledged delivery to the queue.
        }
        catch
        {
            Environment.ExitCode = 1;
            throw;
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        WorkerOptions settings = workerOptions.Value;
        settings.Validate();
        RabbitMqOptions broker = brokerOptions.Value;
        ConnectionFactory factory = broker.CreateFactory();
        await using IConnection connection = await ConnectAsync(factory, settings, stoppingToken);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(broker.QueueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppingToken);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Report worker consuming durable queue {QueueName}", broker.QueueName);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            BasicGetResult? message = await channel.BasicGetAsync(broker.QueueName, autoAck: false, stoppingToken);
            if (message is null)
            {
                await Task.Delay(settings.PollIntervalMilliseconds, stoppingToken);
                continue;
            }

            await handler.HandleAsync(new RabbitDelivery(channel, message), stoppingToken);
        }
    }

    private async Task<IConnection> ConnectAsync(ConnectionFactory factory, WorkerOptions settings, CancellationToken token)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await factory.CreateConnectionAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (attempt < settings.MaxAttempts)
            {
                logger.LogWarning("Broker connection attempt {Attempt} failed: {ErrorType}", attempt, exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds), token);
            }
        }
    }

    private sealed class RabbitDelivery(IChannel channel, BasicGetResult message) : IReportDelivery
    {
        public ReadOnlyMemory<byte> Body => message.Body;

        public Task AckAsync(CancellationToken cancellationToken)
            => channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken).AsTask();

        public Task RejectAsync(CancellationToken cancellationToken)
            => channel.BasicRejectAsync(message.DeliveryTag, requeue: false, cancellationToken).AsTask();
    }
}
