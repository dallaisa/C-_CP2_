using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Endpoints;
using ExpenseHub.Api.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>Tests HTTP handlers through fakes without a database, broker or network.</summary>
[TestClass]
public sealed class ReportEndpointTests
{
    /// <summary>Each scope requires its exact role; other functional roles imply no privilege.</summary>
    [TestMethod]
    [DataRow("Employee", "Mine", true)]
    [DataRow("Employee", "Global", false)]
    [DataRow("Auditor", "Mine", false)]
    [DataRow("Auditor", "Global", true)]
    [DataRow("Admin", "Mine", false)]
    [DataRow("Admin", "Global", false)]
    [DataRow("Approver", "Mine", false)]
    [DataRow("Approver", "Global", false)]
    [DataRow("Finance", "Mine", false)]
    [DataRow("Finance", "Global", false)]
    [DataRow("", "Mine", false)]
    [DataRow("Employee,Auditor", "Mine", true)]
    [DataRow("Employee,Auditor", "Global", true)]
    public async Task CreationEnforcesScopeRoles(string roles, string scope, bool allowed)
    {
        MemoryReportStore store = new MemoryReportStore();
        IResult result = await ReportEndpoints.CreateAsync(Request(scope), Principal("owner", roles), store,
            new UnavailableReportPublisher(), new ReportTestClock(), CancellationToken.None);
        if (!allowed)
        {
            Assert.IsInstanceOfType<ForbidHttpResult>(result);
            Assert.IsEmpty(store.Reports);
            return;
        }

        Assert.AreEqual(201, ((IStatusCodeHttpResult)result).StatusCode);
        Report report = store.Reports.Single();
        Assert.AreNotEqual(Guid.Empty, report.Id);
        Assert.AreEqual("owner", report.RequestedByUserId);
        Assert.AreEqual(ReportStatus.Pending, report.Status);
        Assert.AreEqual(ReportTestClock.Now, report.CreatedAt);
        Assert.IsNull(report.StartedAt);
        Assert.IsNull(report.CompletedAt);
        Assert.IsNull(report.ResultJson);
        Assert.AreEqual(0, store.ExpenseReads);
    }

    /// <summary>A confirmed publisher sees a persisted request before 202 and Location are returned.</summary>
    [TestMethod]
    public async Task AcceptedRequiresConfirmedPublicationAfterPersistence()
    {
        MemoryReportStore store = new MemoryReportStore();
        ConfirmingPublisher publisher = new ConfirmingPublisher(store, true);
        IResult result = await ReportEndpoints.CreateAsync(Request("Mine"), Principal("owner", "Employee"), store,
            publisher, new ReportTestClock(), CancellationToken.None);
        Assert.IsTrue(publisher.SawPersistedPending);
        Accepted<ReportResponse> accepted = (Accepted<ReportResponse>)result;
        Assert.AreEqual(202, accepted.StatusCode);
        Assert.AreEqual($"/api/reports/{store.Reports.Single().Id}", accepted.Location);
    }

    /// <summary>Unconfirmed publication is not reported as accepted; repeated POST creates a fresh request.</summary>
    [TestMethod]
    public async Task UnconfirmedPublicationStaysPendingAndNewPostsHaveDifferentIds()
    {
        MemoryReportStore store = new MemoryReportStore();
        ConfirmingPublisher publisher = new ConfirmingPublisher(store, false);
        for (int index = 0; index < 2; index++)
        {
            IResult result = await ReportEndpoints.CreateAsync(Request("Mine"), Principal("owner", "Employee"), store,
                publisher, new ReportTestClock(), CancellationToken.None);
            Assert.AreEqual(201, ((IStatusCodeHttpResult)result).StatusCode);
        }

        Assert.IsTrue(publisher.SawPersistedPending);
        Assert.AreEqual(2, store.Reports.Select(report => report.Id).Distinct().Count());
        Assert.IsTrue(store.Reports.All(report => report.Status == ReportStatus.Pending));
    }

    /// <summary>Invalid input and missing identity never persist work.</summary>
    [TestMethod]
    public async Task InvalidInputOrMissingIdentityDoesNotCreateWork()
    {
        MemoryReportStore store = new MemoryReportStore();
        IResult bad = await ReportEndpoints.CreateAsync(new ReportRequest(null, null, "Mine"), Principal("owner", "Employee"), store,
            new UnavailableReportPublisher(), new ReportTestClock(), CancellationToken.None);
        Assert.AreEqual(400, ((IStatusCodeHttpResult)bad).StatusCode);
        IResult anonymous = await ReportEndpoints.CreateAsync(Request("Mine"), new ClaimsPrincipal(), store,
            new UnavailableReportPublisher(), new ReportTestClock(), CancellationToken.None);
        Assert.AreEqual(401, ((IStatusCodeHttpResult)anonymous).StatusCode);
        Assert.IsEmpty(store.Reports);
    }

    /// <summary>Even an Auditor cannot list, inspect or download someone else's request.</summary>
    [TestMethod]
    public async Task OwnershipAppliesToListDetailAndDownload()
    {
        MemoryReportStore store = new MemoryReportStore();
        Report report = MemoryReportStore.Pending();
        store.Reports.Add(report);
        ClaimsPrincipal other = Principal("other", "Auditor");
        IResult list = await ReportEndpoints.ListAsync(other, store, CancellationToken.None);
        Assert.IsEmpty(((Ok<ReportResponse[]>)list).Value!);
        IResult detail = await ReportEndpoints.GetAsync(report.Id, other, store, CancellationToken.None);
        Assert.AreEqual(404, ((IStatusCodeHttpResult)detail).StatusCode);
        IResult excel = await ReportEndpoints.ExcelAsync(report.Id, other, store, CancellationToken.None);
        Assert.AreEqual(404, ((IStatusCodeHttpResult)excel).StatusCode);
        IResult missing = await ReportEndpoints.GetAsync(Guid.NewGuid(), Principal("owner", "Employee"), store, CancellationToken.None);
        Assert.AreEqual(404, ((IStatusCodeHttpResult)missing).StatusCode);
        Assert.AreEqual(0, store.ExpenseReads);
    }

    /// <summary>Every unfinished or failed owned report returns 409 on download.</summary>
    [TestMethod]
    [DataRow("Pending")]
    [DataRow("Processing")]
    [DataRow("Failed")]
    public async Task UnfinishedOwnedReportReturnsConflict(string state)
    {
        MemoryReportStore store = new MemoryReportStore();
        Report report = MemoryReportStore.Pending();
        report.Status = Enum.Parse<ReportStatus>(state);
        store.Reports.Add(report);
        IResult result = await ReportEndpoints.ExcelAsync(report.Id, Principal("owner", "Employee"), store,
            CancellationToken.None);
        Assert.AreEqual(409, ((IStatusCodeHttpResult)result).StatusCode);
    }

    /// <summary>Saved data is enough for listing, detail and XLSX; expense reads are forbidden in this test.</summary>
    [TestMethod]
    public async Task ReadRoutesUseOnlyPersistedSnapshotAndDownloadContract()
    {
        MemoryReportStore store = new MemoryReportStore { ThrowOnRead = true };
        Report report = MemoryReportStore.Pending();
        report.Status = ReportStatus.Completed;
        report.StartedAt = report.CreatedAt;
        report.CompletedAt = report.CreatedAt;
        report.ResultJson = System.Text.Json.JsonSerializer.Serialize(new ReportCalculator(new ReportTestClock())
            .Calculate([], report.FromDate, report.ToDate, report.Scope, report.RequestedByUserId));
        store.Reports.Add(report);
        ClaimsPrincipal owner = Principal("owner", "Employee");
        Ok<ReportResponse[]> list = (Ok<ReportResponse[]>)await ReportEndpoints.ListAsync(owner, store, CancellationToken.None);
        Assert.HasCount(1, list.Value!);
        Ok<ReportResponse> detail = (Ok<ReportResponse>)await ReportEndpoints.GetAsync(report.Id, owner, store, CancellationToken.None);
        Assert.AreEqual(0m, detail.Value!.Result!.TotalAmount);
        FileContentHttpResult file = (FileContentHttpResult)await ReportEndpoints.ExcelAsync(report.Id, owner, store,
            CancellationToken.None);
        Assert.AreEqual(ReportExcelWriter.ContentType, file.ContentType);
        Assert.AreEqual($"report-{report.Id}.xlsx", file.FileDownloadName);
        Assert.IsGreaterThan(0, file.FileContents.Length);
        Assert.AreEqual(0, store.ExpenseReads);
    }

    private static ReportRequest Request(string scope) => new ReportRequest(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), scope);

    private static ClaimsPrincipal Principal(string id, string roles)
    {
        ClaimsIdentity identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, id));
        foreach (string role in roles.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }
}
