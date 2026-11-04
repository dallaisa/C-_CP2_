using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>CP3-I04: HTTP publication contract, persisted results and workbook content, all in memory.</summary>
[TestClass]
public sealed class ReportContractTests
{
    private const string Body = "{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\",\"scope\":\"Mine\"}";
    private static readonly string[] _header = ["Status", "Count", "Amount"];
    private static readonly string[] _states = ["Draft", "Submitted", "Approved", "Rejected", "Paid"];

    /// <summary>A confirmed publication returns 202, Location and a Pending envelope for the persisted ID.</summary>
    [TestMethod]
    public async Task ConfirmedPublicationReturnsAcceptedWithLocationAndPendingEnvelope()
    {
        MemoryReportStore store = new MemoryReportStore();
        await using WebApplication app = await ReportHttpTests.CreateAppAsync(new ConfirmingPublisher(store, true), store);
        using HttpClient client = Client(app, "Employee");
        using HttpResponseMessage response = await Post(client, Body);

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Report report = store.Reports.Single();
        Assert.AreEqual($"/api/reports/{report.Id}", response.Headers.Location!.OriginalString);
        ReportResponse envelope = (await response.Content.ReadFromJsonAsync<ReportResponse>())!;
        Assert.AreEqual(report.Id, envelope.Id);
        Assert.AreEqual("Pending", envelope.Status);
        Assert.AreEqual("Mine", envelope.Scope);
        Assert.IsNull(envelope.StartedAt);
        Assert.IsNull(envelope.CompletedAt);
        Assert.IsNull(envelope.Result);
        Assert.IsNull(envelope.Error);
    }

    /// <summary>A second valid POST is a new request and never reuses the earlier ID.</summary>
    [TestMethod]
    public async Task EachValidPostCreatesNewIdentifier()
    {
        MemoryReportStore store = new MemoryReportStore();
        await using WebApplication app = await ReportHttpTests.CreateAppAsync(new ConfirmingPublisher(store, true), store);
        using HttpClient client = Client(app, "Employee");
        using HttpResponseMessage first = await Post(client, Body);
        using HttpResponseMessage second = await Post(client, Body);

        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.Accepted, second.StatusCode);
        Assert.AreNotEqual(first.Headers.Location, second.Headers.Location);
        Assert.HasCount(2, store.Reports);
    }

    /// <summary>An unconfirmed publication returns 503 with the saved ID, and the report remains Pending, not Failed.</summary>
    [TestMethod]
    public async Task UnconfirmedPublicationIsNotAFailedReport()
    {
        MemoryReportStore store = new MemoryReportStore();
        await using WebApplication app = await ReportHttpTests.CreateAppAsync(new ConfirmingPublisher(store, false), store);
        using HttpClient client = Client(app, "Employee");
        using HttpResponseMessage created = await Post(client, Body);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, created.StatusCode);
        ReportPublicationFailure failure = (await created.Content.ReadFromJsonAsync<ReportPublicationFailure>())!;
        ReportResponse detail = (await client.GetFromJsonAsync<ReportResponse>($"/api/reports/{failure.Id}"))!;
        Assert.AreEqual("Pending", detail.Status);
        Assert.IsNull(detail.Error);
        Assert.IsNull(detail.CompletedAt);
    }

    /// <summary>Inverted periods are rejected by HTTP before persistence or publication.</summary>
    [TestMethod]
    public async Task InvertedPeriodDoesNotPersistOrPublish()
    {
        MemoryReportStore store = new MemoryReportStore();
        ConfirmingPublisher publisher = new ConfirmingPublisher(store, true);
        await using WebApplication app = await ReportHttpTests.CreateAppAsync(publisher, store);
        using HttpClient client = Client(app, "Employee");
        using HttpResponseMessage response = await Post(client,
            "{\"fromDate\":\"2026-09-30\",\"toDate\":\"2026-09-01\",\"scope\":\"Mine\"}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsEmpty(store.Reports);
        Assert.IsFalse(publisher.Called);
    }

    /// <summary>Repeated reads after an expense changes return the same stored result and never read expenses.</summary>
    [TestMethod]
    public async Task RepeatedReadsReturnPersistedResultWithoutRecalculation()
    {
        MemoryReportStore store = new MemoryReportStore();
        Report report = MemoryReportStore.Pending();
        store.Reports.Add(report);
        Expense expense = new Expense { OwnerId = "owner", ExpenseDate = report.FromDate, Status = ExpenseStatus.Approved, Amount = 40m };
        store.Expenses.Add(expense);
        await Processor(store).ProcessAsync(report.Id, CancellationToken.None);
        string snapshot = report.ResultJson!;
        expense.Amount = 4000m;
        expense.Status = ExpenseStatus.Rejected;

        await using WebApplication app = await ReportHttpTests.CreateAppAsync(null, store);
        using HttpClient client = Client(app, "Employee");
        for (int index = 0; index < 2; index++)
        {
            ReportResponse detail = (await client.GetFromJsonAsync<ReportResponse>($"/api/reports/{report.Id}"))!;
            Assert.AreEqual("Completed", detail.Status);
            Assert.AreEqual(40m, detail.Result!.TotalAmount);
            Assert.AreEqual(1, detail.Result.TotalCount);
            Assert.AreEqual(ReportTestClock.Now, detail.Result.GeneratedAt);
            Assert.AreEqual(40m, detail.Result.ByStatus.Single(row => row.Status == "Approved").Amount);
            Assert.AreEqual(0m, detail.Result.ByStatus.Single(row => row.Status == "Rejected").Amount);
        }

        Assert.AreEqual(1, store.ExpenseReads);
        Assert.AreEqual(snapshot, report.ResultJson);
    }

    /// <summary>Redelivery of a Failed report keeps the failure, timestamps and sanitized error unchanged.</summary>
    [TestMethod]
    public async Task DuplicateOfFailedReportHasNoEffect()
    {
        MemoryReportStore store = new MemoryReportStore { ThrowOnRead = true };
        Report report = MemoryReportStore.Pending();
        store.Reports.Add(report);
        ReportProcessor processor = Processor(store);
        Assert.IsTrue(await processor.ProcessAsync(report.Id, CancellationToken.None));
        DateTimeOffset? started = report.StartedAt;
        DateTimeOffset? completed = report.CompletedAt;
        int saves = store.SavedStates.Count;

        Assert.IsTrue(await processor.ProcessAsync(report.Id, CancellationToken.None));

        Assert.AreEqual(ReportStatus.Failed, report.Status);
        Assert.AreEqual("REPORT_PROCESSING_FAILED", report.ErrorCode);
        Assert.AreEqual(started, report.StartedAt);
        Assert.AreEqual(completed, report.CompletedAt);
        Assert.HasCount(saves, store.SavedStates);
        Assert.AreEqual(1, store.ExpenseReads);
    }

    /// <summary>A duplicate of a Completed report keeps GeneratedAt, CompletedAt and writes nothing.</summary>
    [TestMethod]
    public async Task DuplicateOfCompletedReportKeepsGeneratedAtAndCompletedAt()
    {
        MemoryReportStore store = new MemoryReportStore();
        Report report = MemoryReportStore.Pending();
        store.Reports.Add(report);
        store.Expenses.Add(new Expense { OwnerId = "owner", ExpenseDate = report.FromDate, Status = ExpenseStatus.Paid, Amount = 5m });
        ReportProcessor processor = Processor(store);
        await processor.ProcessAsync(report.Id, CancellationToken.None);
        DateTimeOffset generated = report.ReadResult()!.GeneratedAt;
        DateTimeOffset? completed = report.CompletedAt;
        int saves = store.SavedStates.Count;

        Assert.IsTrue(await processor.ProcessAsync(report.Id, CancellationToken.None));

        Assert.AreEqual(generated, report.ReadResult()!.GeneratedAt);
        Assert.AreEqual(completed, report.CompletedAt);
        Assert.HasCount(saves, store.SavedStates);
        Assert.AreEqual(ReportStatus.Completed, report.Status);
    }

    /// <summary>A duplicate delivery is logged with the report ID so the experiment can be correlated.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DuplicateDeliveryIsLoggedWithReportId(bool failed)
    {
        MemoryReportStore store = new MemoryReportStore { ThrowOnRead = failed };
        Report report = MemoryReportStore.Pending();
        store.Reports.Add(report);
        CapturingLogger<ReportProcessor> logger = new CapturingLogger<ReportProcessor>();
        ReportProcessor processor = new ReportProcessor(
            store, new ReportCalculator(new ReportTestClock()), new ReportTestClock(), logger);
        await processor.ProcessAsync(report.Id, CancellationToken.None);
        logger.Messages.Clear();

        await processor.ProcessAsync(report.Id, CancellationToken.None);

        string message = logger.Messages.Single();
        Assert.Contains(report.Id.ToString(), message, StringComparison.Ordinal);
        Assert.Contains("duplicate delivery ignored", message, StringComparison.Ordinal);
        Assert.Contains(failed ? "Failed" : "Completed", message, StringComparison.Ordinal);
    }

    /// <summary>The worker computes from the saved scope and requester: Mine ignores others, Global includes them.</summary>
    [TestMethod]
    [DataRow("Mine", "10.5", 1)]
    [DataRow("Global", "110.5", 2)]
    public async Task ProcessorUsesSavedRequesterAndScope(string scopeName, string expectedAmount, int expectedCount)
    {
        MemoryReportStore store = new MemoryReportStore();
        Report report = new Report
        {
            Id = Guid.NewGuid(),
            RequestedByUserId = "owner",
            FromDate = new DateOnly(2026, 9, 1),
            ToDate = new DateOnly(2026, 9, 30),
            Scope = Enum.Parse<ReportScope>(scopeName),
            Status = ReportStatus.Pending,
            CreatedAt = ReportTestClock.Now,
        };
        store.Reports.Add(report);
        store.Expenses.Add(new Expense { OwnerId = "owner", ExpenseDate = report.FromDate, Status = ExpenseStatus.Approved, Amount = 10.5m });
        store.Expenses.Add(new Expense { OwnerId = "other", ExpenseDate = report.ToDate, Status = ExpenseStatus.Approved, Amount = 100m });

        await Processor(store).ProcessAsync(report.Id, CancellationToken.None);

        ReportResult result = report.ReadResult()!;
        Assert.AreEqual(decimal.Parse(expectedAmount, System.Globalization.CultureInfo.InvariantCulture), result.TotalAmount);
        Assert.AreEqual(expectedCount, result.TotalCount);
    }

    /// <summary>Workbook headers, metadata and numeric totals match the saved Global result exactly.</summary>
    [TestMethod]
    public async Task WorkbookHasExpectedHeadersMetadataAndNumbers()
    {
        MemoryReportStore store = new MemoryReportStore();
        Report report = new Report
        {
            Id = Guid.NewGuid(),
            RequestedByUserId = "auditor",
            FromDate = new DateOnly(2026, 9, 1),
            ToDate = new DateOnly(2026, 9, 30),
            Scope = ReportScope.Global,
            Status = ReportStatus.Pending,
            CreatedAt = ReportTestClock.Now,
        };
        store.Reports.Add(report);
        store.Expenses.Add(new Expense { OwnerId = "a", ExpenseDate = report.FromDate, Status = ExpenseStatus.Approved, Amount = 150.75m });
        store.Expenses.Add(new Expense { OwnerId = "b", ExpenseDate = report.ToDate, Status = ExpenseStatus.Paid, Amount = 24.25m });
        await Processor(store).ProcessAsync(report.Id, CancellationToken.None);

        await using WebApplication app = await ReportHttpTests.CreateAppAsync(null, store);
        using HttpClient client = Client(app, "Auditor", "auditor");
        using HttpResponseMessage response = await client.GetAsync($"/api/reports/{report.Id}/excel");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(ReportExcelWriter.ContentType, response.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        string fileName = response.Content.Headers.ContentDisposition.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName!;
        Assert.AreEqual($"report-{report.Id}.xlsx", fileName.Trim('"'));
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        using MemoryStream stream = new MemoryStream(bytes);
        using SpreadsheetDocument document = SpreadsheetDocument.Open(stream, false);
        Row[] rows = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Row>().ToArray();

        Assert.AreEqual("Global", Text(rows[1], 1));
        CollectionAssert.AreEqual(_header, new[] { Text(rows[5], 0), Text(rows[5], 1), Text(rows[5], 2) });
        CollectionAssert.AreEqual(_states,
            rows.Skip(6).Take(5).Select(row => Text(row, 0)).ToArray());
        Assert.AreEqual("Total", Text(rows[11], 0));
        Assert.AreEqual(2m, Number(rows[11], 1));
        Assert.AreEqual(175m, Number(rows[11], 2));
        Assert.AreEqual(150.75m, Number(rows[8], 2));
        Assert.AreEqual(24.25m, Number(rows[10], 2));
        Assert.IsTrue(rows.Skip(6).All(row => row.Elements<Cell>().Skip(1).All(cell => cell.DataType!.Value == CellValues.Number)));
    }

    /// <summary>The Excel route refuses unfinished own reports with 409 and no workbook.</summary>
    [TestMethod]
    [DataRow("Pending")]
    [DataRow("Processing")]
    [DataRow("Failed")]
    public async Task ExcelOfUnfinishedOwnReportIsConflictWithoutFile(string status)
    {
        MemoryReportStore store = new MemoryReportStore();
        Report report = MemoryReportStore.Pending();
        report.Status = Enum.Parse<ReportStatus>(status);
        store.Reports.Add(report);
        await using WebApplication app = await ReportHttpTests.CreateAppAsync(null, store);
        using HttpClient client = Client(app, "Employee");
        using HttpResponseMessage response = await client.GetAsync($"/api/reports/{report.Id}/excel");

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreNotEqual(ReportExcelWriter.ContentType, response.Content.Headers.ContentType?.MediaType);
    }

    private static string Text(Row row, int index) => row.Elements<Cell>().ElementAt(index).InnerText;

    private static decimal Number(Row row, int index)
        => decimal.Parse(Text(row, index), System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<HttpResponseMessage> Post(HttpClient client, string body)
    {
        using StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.PostAsync("/api/reports", content);
    }

    private static HttpClient Client(WebApplication app, string roles, string user = "owner")
    {
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("TestRole", roles);
        client.DefaultRequestHeaders.Add("TestUser", user);
        return client;
    }

    private static ReportProcessor Processor(MemoryReportStore store) => new ReportProcessor(
        store, new ReportCalculator(new ReportTestClock()), new ReportTestClock(), NullLogger<ReportProcessor>.Instance);
}

internal sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    internal List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
        TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception));
}
