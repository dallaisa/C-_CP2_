using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>Verifies saved snapshots, processing transitions, failures and valid Excel files without infrastructure.</summary>
[TestClass]
public sealed class ReportProcessingTests
{
    /// <summary>Processing saves real data once; subsequent changes and redelivery cannot change the snapshot.</summary>
    [TestMethod]
    public async Task SnapshotAndExcelRemainUnchangedAfterExpenseMutation()
    {
        MemoryReportStore store = new MemoryReportStore();
        Report report = MemoryReportStore.Pending();
        store.Reports.Add(report);
        Expense expense = new Expense { OwnerId = "owner", ExpenseDate = report.FromDate, Status = ExpenseStatus.Approved, Amount = 100.25m };
        store.Expenses.Add(expense);
        ReportProcessor processor = Processor(store);

        Assert.IsTrue(await processor.ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(ExpenseStatus.Approved, expense.Status);
        Assert.AreEqual(ReportTestClock.Now, report.StartedAt);
        Assert.AreEqual(ReportTestClock.Now, report.CompletedAt);
        CollectionAssert.AreEqual(new[] { ReportStatus.Processing, ReportStatus.Completed }, store.SavedStates);
        Assert.IsNull(report.ErrorCode);
        string? snapshot = report.ResultJson;
        expense.Amount = 999m;
        expense.Status = ExpenseStatus.Paid;
        Assert.IsTrue(await processor.ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(1, store.ExpenseReads);
        Assert.AreEqual(snapshot, report.ResultJson);
        Assert.AreEqual(100.25m, ReportResponse.FromReport(report).Result!.TotalAmount);
        ValidateWorkbook(report, "100.25", "1");
        Assert.AreEqual(1, store.ExpenseReads);
    }

    /// <summary>Empty reports still produce a schema-valid workbook with five states and numerical zeros.</summary>
    [TestMethod]
    public async Task EmptyReportProducesValidExcel()
    {
        MemoryReportStore store = new MemoryReportStore();
        Report report = MemoryReportStore.Pending();
        store.Reports.Add(report);
        await Processor(store).ProcessAsync(report.Id, CancellationToken.None);
        ValidateWorkbook(report, "0", "0");
    }

    /// <summary>Failures persist a safe terminal envelope and never expose exception text.</summary>
    [TestMethod]
    public async Task FailureIsSanitizedAndPersisted()
    {
        MemoryReportStore store = new MemoryReportStore { ThrowOnRead = true };
        Report report = MemoryReportStore.Pending();
        store.Reports.Add(report);
        Assert.IsTrue(await Processor(store).ProcessAsync(report.Id, CancellationToken.None));
        ReportResponse response = ReportResponse.FromReport(report);
        Assert.AreEqual("Failed", response.Status);
        Assert.AreEqual("REPORT_PROCESSING_FAILED", response.Error!.Code);
        Assert.IsFalse(response.Error.Message.Contains("Sensitive", StringComparison.Ordinal));
        Assert.IsNotNull(response.StartedAt);
        Assert.IsNotNull(response.CompletedAt);
        Assert.IsNull(response.Result);
        Assert.IsTrue(await Processor(store).ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(1, store.ExpenseReads);
    }

    /// <summary>A competing claim prevents expense reads and calculation.</summary>
    [TestMethod]
    public async Task LostClaimDoesNotCalculate()
    {
        MemoryReportStore store = new MemoryReportStore { SaveSucceeds = false };
        Report report = MemoryReportStore.Pending();
        store.Reports.Add(report);
        Assert.IsFalse(await Processor(store).ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(0, store.ExpenseReads);
        Assert.IsNull(report.ResultJson);
    }

    /// <summary>Missing requests are not processed; abandoned Processing requests are resumed.</summary>
    [TestMethod]
    public async Task MissingIsNotProcessedAndProcessingIsResumed()
    {
        MemoryReportStore store = new MemoryReportStore();
        Assert.IsFalse(await Processor(store).ProcessAsync(Guid.NewGuid(), CancellationToken.None));
        Report report = MemoryReportStore.Pending();
        report.Status = ReportStatus.Processing;
        store.Reports.Add(report);
        Assert.IsTrue(await Processor(store).ProcessAsync(report.Id, CancellationToken.None));
        Assert.AreEqual(1, store.ExpenseReads);
        Assert.AreEqual(ReportStatus.Completed, report.Status);
    }

    private static ReportProcessor Processor(MemoryReportStore store) => new ReportProcessor(
        store, new ReportCalculator(new ReportTestClock()), new ReportTestClock(), NullLogger<ReportProcessor>.Instance);

    private static void ValidateWorkbook(Report report, string amount, string count)
    {
        byte[] bytes = ReportExcelWriter.Write(ReportResponse.FromReport(report));
        Assert.AreEqual((byte)'P', bytes[0]);
        Assert.AreEqual((byte)'K', bytes[1]);
        using MemoryStream stream = new MemoryStream(bytes);
        using SpreadsheetDocument document = SpreadsheetDocument.Open(stream, false);
        Assert.IsEmpty(new OpenXmlValidator().Validate(document).ToArray());
        Row[] rows = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Row>().ToArray();
        Assert.HasCount(12, rows);
        Assert.AreEqual(report.Id.ToString(), rows[0].Elements<Cell>().Last().InnerText);
        Assert.AreEqual("Mine", rows[1].Elements<Cell>().Last().InnerText);
        Assert.AreEqual("2026-09-01", rows[2].Elements<Cell>().Last().InnerText);
        Assert.AreEqual("2026-09-30", rows[3].Elements<Cell>().Last().InnerText);
        Assert.AreEqual(ReportTestClock.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture), rows[4].Elements<Cell>().Last().InnerText);
        CollectionAssert.AreEquivalent(Enum.GetNames<ExpenseStatus>(), rows.Skip(6).Take(5).Select(row => row.Elements<Cell>().First().InnerText).ToArray());
        foreach (Row row in rows.Skip(6))
        {
            Assert.IsTrue(row.Elements<Cell>().Skip(1).All(cell => cell.DataType!.Value == CellValues.Number));
        }

        Assert.AreEqual(count, rows[11].Elements<Cell>().ElementAt(1).InnerText);
        Assert.AreEqual(amount, rows[11].Elements<Cell>().ElementAt(2).InnerText);
        ReportResult saved = report.ReadResult()!;
        foreach (ReportStatusTotal total in saved.ByStatus)
        {
            Cell[] cells = rows.Skip(6).Take(5).Single(row => row.Elements<Cell>().First().InnerText == total.Status).Elements<Cell>().ToArray();
            Assert.AreEqual(total.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), cells[1].InnerText);
            Assert.AreEqual(total.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), cells[2].InnerText);
        }
    }
}
