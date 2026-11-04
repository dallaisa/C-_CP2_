using System;
using RabbitMQ.Client;

namespace ExpenseHub.Api.Messaging;

/// <summary>External RabbitMQ configuration shared by producer and consumer; credentials have no defaults.</summary>
internal sealed class RabbitMqOptions
{
    /// <summary>Gets or sets the broker hostname.</summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>Gets or sets the AMQP port.</summary>
    public int Port { get; set; } = 5672;

    /// <summary>Gets or sets the virtual host.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Gets or sets the username supplied externally.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the password supplied externally.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the shared durable queue name.</summary>
    public string QueueName { get; set; } = "expensehub.reports";

    /// <summary>Gets or sets whether TLS is required.</summary>
    public bool UseTls { get; set; }

    /// <summary>Gets or sets the connection and publication timeout.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Creates a validated connection factory without logging secrets.</summary>
    internal ConnectionFactory CreateFactory()
    {
        if (string.IsNullOrWhiteSpace(HostName) || string.IsNullOrWhiteSpace(UserName)
            || string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(QueueName)
            || Port is < 1 or > 65535 || TimeoutSeconds is < 1 or > 120)
        {
            throw new InvalidOperationException("Configure RabbitMq host, credentials, queue, port and timeout externally.");
        }

        return new ConnectionFactory
        {
            HostName = HostName,
            Port = Port,
            VirtualHost = VirtualHost,
            UserName = UserName,
            Password = Password,
            AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(TimeoutSeconds),
            Ssl = new SslOption { Enabled = UseTls, ServerName = HostName },
        };
    }
}
