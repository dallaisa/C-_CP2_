using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Messaging;
using ExpenseHub.Api.Reports;
using ExpenseHub.UnitTests.Reports;
using ExpenseHub.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Messaging;

/// <summary>Verifies delivery settlement against serialized persistence fakes, without broker, network or database.</summary>
[TestClass]
public sealed class ReportDeliveryTests
{
    /// <summary>Only reportId is serialized and valid payloads round-trip.</summary>
    [TestMethod]
    public void MessageCarriesOnlyPersistedIdentifier()
    {
        Guid id = Guid.NewGuid();
        byte[] body = new ReportMessage(id).Serialize();
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.AreEqual(1, json.RootElement.EnumerateObject().Count());
        Assert.AreEqual(id, json.RootElement.GetProperty("reportId").GetGuid());
        Assert.AreEqual(id, ReportMessage.Parse(body)!.ReportId);
    }

    /// <summary>Invalid or authority-bearing payloads are rejected without processing or ACK.</summary>
    [TestMethod]
    [DataRow("{}")]
    [DataRow("null")]
    [DataRow("not json")]
    [DataRow("{\"reportId\":\"invalid\"}")]
    [DataRow("{\"reportId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [DataRow("{\"reportId\":\"00000000-0000-0000-0000-000000000001\",\"scope\":\"Global\"}")]
    [DataRow("{\"reportId\":\"00000000-0000-0000-0000-000000000001\",\"requestedByUserId\":\"other\"}")]
    public async Task InvalidMessageIsRejected(string json)
    {
        State state = new State();
        using ServiceProvider services = Services(state);
        Delivery delivery = new Delivery(Encoding.UTF8.GetBytes(json), state);
        await Handler(services).HandleAsync(delivery, CancellationToken.None);
        Assert.IsTrue(delivery.Rejected);
        Assert.IsFalse(delivery.Acknowledged);
        Assert.AreEqual(0, state.Reads);
    }

    /// <summary>Missing report IDs are rejected rather than endlessly redelivered.</summary>
    [TestMethod]
    public async Task MissingReportIsRejected()
    {
        State state = new State();
        using ServiceProvider services = Services(state);
        Delivery delivery = new Delivery(new ReportMessage(Guid.NewGuid()).Serialize(), state);
        await Handler(services).HandleAsync(delivery, CancellationToken.None);
        Assert.IsTrue(delivery.Rejected);
        Assert.IsFalse(delivery.Acknowledged);
    }

    /// <summary>A recreated handler and store cannot replace a saved result on duplicate delivery.</summary>
    [TestMethod]
    public async Task AckFollowsPersistenceAndRestartedDuplicateHasNoEffect()
    {
        State state = new State();
        using (ServiceProvider first = Services(state))
        {
            Delivery delivery = DeliveryFor(state);
            await Handler(first).HandleAsync(delivery, CancellationToken.None);
            Assert.IsTrue(delivery.Acknowledged);
            Assert.IsTrue(delivery.SawPersistedTerminal);
        }

        string saved = state.Snapshot;
        using ServiceProvider restarted = Services(state);
        Delivery duplicate = DeliveryFor(state);
        await Handler(restarted).HandleAsync(duplicate, CancellationToken.None);
        Assert.IsTrue(duplicate.Acknowledged);
        Assert.AreEqual(saved, state.Snapshot);
        Assert.AreEqual(1, state.Reads);
    }

    /// <summary>A definitive calculation failure is saved as Failed before acknowledgement.</summary>
    [TestMethod]
    public async Task DefinitiveFailureIsPersistedBeforeAck()
    {
        State state = new State { DefinitiveFailure = true };
        using ServiceProvider services = Services(state);
        Delivery delivery = DeliveryFor(state);
        await Handler(services).HandleAsync(delivery, CancellationToken.None);
        Report saved = state.Load();
        Assert.AreEqual(ReportStatus.Failed, saved.Status);
        Assert.AreEqual("REPORT_PROCESSING_FAILED", saved.ErrorCode);
        Assert.IsNull(saved.ResultJson);
        Assert.IsTrue(delivery.Acknowledged);
        Assert.IsTrue(delivery.SawPersistedTerminal);
    }

    /// <summary>Failure to save either terminal state cannot ACK or pretend a terminal snapshot exists.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TerminalPersistenceFailureDoesNotAck(bool definitive)
    {
        State state = new State { DefinitiveFailure = definitive, FailTerminalSave = true };
        using ServiceProvider services = Services(state);
        Delivery delivery = DeliveryFor(state);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Handler(services).HandleAsync(delivery, CancellationToken.None));
        Assert.IsFalse(delivery.Acknowledged);
        Assert.IsFalse(delivery.Rejected);
        Assert.AreEqual(ReportStatus.Processing, state.Load().Status);
        Assert.IsNull(state.Load().ResultJson);
    }

    /// <summary>A transient read failure leaves Processing and a later delivery resumes from saved parameters.</summary>
    [TestMethod]
    public async Task TransientFailureDoesNotAckAndProcessingCanResume()
    {
        State state = new State { TransientFailures = 1 };
        using ServiceProvider services = Services(state);
        Delivery delivery = DeliveryFor(state);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Handler(services).HandleAsync(delivery, CancellationToken.None));
        Assert.IsFalse(delivery.Acknowledged);
        Assert.AreEqual(ReportStatus.Processing, state.Load().Status);
        Assert.IsNull(state.Load().ErrorCode);
        DateTimeOffset? previous = state.Load().StartedAt;
        Delivery retried = DeliveryFor(state);
        await Handler(services).HandleAsync(retried, CancellationToken.None);
        Assert.IsTrue(retried.Acknowledged);
        Assert.IsGreaterThan(previous!.Value, state.Load().StartedAt!.Value);
    }

    /// <summary>A bounded retry uses a new store and acknowledges only the successful attempt.</summary>
    [TestMethod]
    public async Task RetryUsesFreshScopeAndRecoversTransientRead()
    {
        State state = new State { TransientFailures = 1 };
        using ServiceProvider services = Services(state);
        Delivery delivery = DeliveryFor(state);
        await Handler(services, attempts: 2).HandleAsync(delivery, CancellationToken.None);
        Assert.IsTrue(delivery.Acknowledged);
        Assert.AreEqual(2, state.StoreInstances);
        Assert.AreEqual(ReportStatus.Completed, state.Load().Status);
    }

    /// <summary>An ACK failure after saving does not recalculate; redelivery acknowledges the saved snapshot.</summary>
    [TestMethod]
    public async Task LostAckDoesNotRepeatPersistedEffect()
    {
        State state = new State();
        using ServiceProvider services = Services(state);
        Delivery first = DeliveryFor(state);
        first.FailAck = true;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Handler(services).HandleAsync(first, CancellationToken.None));
        string saved = state.Snapshot;
        Delivery redelivery = DeliveryFor(state);
        await Handler(services).HandleAsync(redelivery, CancellationToken.None);
        Assert.IsTrue(redelivery.Acknowledged);
        Assert.AreEqual(saved, state.Snapshot);
        Assert.AreEqual(1, state.Reads);
    }

    private static ServiceProvider Services(State state)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<TimeProvider, ReportTestClock>();
        services.AddSingleton<ReportCalculator>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ReportProcessor>>(NullLogger<ReportProcessor>.Instance);
        services.AddScoped<IReportStore>(_ => new Store(state));
        services.AddScoped<ReportProcessor>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static ReportDeliveryHandler Handler(ServiceProvider services, int attempts = 1) => new ReportDeliveryHandler(
        services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new WorkerOptions { MaxAttempts = attempts, RetryDelaySeconds = 1 }),
        TimeProvider.System, NullLogger<ReportDeliveryHandler>.Instance);

    private static Delivery DeliveryFor(State state) => new Delivery(new ReportMessage(state.Load().Id).Serialize(), state);

    private sealed class State
    {
        internal string Snapshot { get; set; } = JsonSerializer.Serialize(MemoryReportStore.Pending());

        internal int Reads { get; set; }

        internal int StoreInstances { get; set; }

        internal int TransientFailures { get; set; }

        internal bool DefinitiveFailure { get; set; }

        internal bool FailTerminalSave { get; set; }

        internal Report Load() => JsonSerializer.Deserialize<Report>(Snapshot)!;
    }

    private sealed class Store : IReportStore
    {
        private readonly State _state;
        private readonly Report _tracked;

        internal Store(State state)
        {
            _state = state;
            _tracked = state.Load();
            state.StoreInstances++;
        }

        public Task<Report?> FindAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(id == _tracked.Id ? _tracked : null);

        public Task<List<Expense>> ReadExpensesAsync(Report report, CancellationToken cancellationToken)
        {
            _state.Reads++;
            if (_state.TransientFailures > 0)
            {
                _state.TransientFailures--;
                throw new DbUpdateException("temporary database outage");
            }

            if (_state.DefinitiveFailure)
            {
                throw new InvalidOperationException("invalid saved business data");
            }

            return Task.FromResult(new List<Expense>
            {
                new Expense { OwnerId = "owner", ExpenseDate = report.FromDate, Amount = 10.25m },
                new Expense { OwnerId = "other", ExpenseDate = report.FromDate, Amount = 100m },
            });
        }

        public Task<bool> SaveAsync(CancellationToken cancellationToken)
        {
            if (_state.FailTerminalSave && _tracked.Status is ReportStatus.Completed or ReportStatus.Failed)
            {
                throw new DbUpdateException("cannot save terminal state");
            }

            _state.Snapshot = JsonSerializer.Serialize(_tracked);
            return Task.FromResult(true);
        }

        public Task AddAsync(Report report, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<List<Report>> ListOwnAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Report?> FindOwnAsync(Guid id, string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Delivery(ReadOnlyMemory<byte> body, State state) : IReportDelivery
    {
        public ReadOnlyMemory<byte> Body => body;

        internal bool Acknowledged { get; private set; }

        internal bool Rejected { get; private set; }

        internal bool SawPersistedTerminal { get; private set; }

        internal bool FailAck { get; set; }

        public Task AckAsync(CancellationToken cancellationToken)
        {
            if (FailAck)
            {
                throw new InvalidOperationException("ACK lost");
            }

            Report report = state.Load();
            SawPersistedTerminal = report.CompletedAt.HasValue &&
                ((report.Status == ReportStatus.Completed && report.ResultJson is not null && report.ErrorCode is null)
                || (report.Status == ReportStatus.Failed && report.ResultJson is null && report.ErrorCode is not null));
            Acknowledged = true;
            return Task.CompletedTask;
        }

        public Task RejectAsync(CancellationToken cancellationToken)
        {
            Rejected = true;
            return Task.CompletedTask;
        }
    }
}
