using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Endpoints;
using ExpenseHub.Api.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>Verifies explicit, sanitized failures without false acceptance or invented persisted IDs.</summary>
[TestClass]
public sealed class ReportPublicationTests
{
    /// <summary>Publisher exceptions preserve the request and cannot expose infrastructure details.</summary>
    [TestMethod]
    public async Task PublicationExceptionReturnsSavedIdAndSafeError()
    {
        MemoryReportStore store = new MemoryReportStore();
        IResult result = await CreateAsync(store, new ThrowingPublisher());
        JsonHttpResult<ReportPublicationFailure> response = (JsonHttpResult<ReportPublicationFailure>)result;
        Assert.AreEqual(503, response.StatusCode);
        Assert.AreEqual(store.Reports[0].Id, response.Value!.Id);
        Assert.AreEqual("REPORT_PUBLICATION_UNCONFIRMED", response.Value.Error.Code);
        Assert.IsFalse(response.Value.Error.Message.Contains("sensitive", StringComparison.Ordinal));
        Assert.AreEqual(ReportStatus.Pending, store.Reports[0].Status);
        Assert.IsNull(store.Reports[0].ErrorCode);
    }

    /// <summary>A database failure produces no persisted identifier and cannot reach the publisher.</summary>
    [TestMethod]
    public async Task PersistenceFailureDoesNotPublishOrReturnId()
    {
        MemoryReportStore store = new MemoryReportStore { ThrowOnAdd = true };
        ConfirmingPublisher publisher = new ConfirmingPublisher(store, true);
        ProblemHttpResult result = (ProblemHttpResult)await CreateAsync(store, publisher);
        Assert.AreEqual(503, result.StatusCode);
        Assert.AreEqual("REPORT_PERSISTENCE_UNAVAILABLE", result.ProblemDetails.Title);
        Assert.IsEmpty(result.ProblemDetails.Extensions);
        Assert.IsEmpty(store.Reports);
        Assert.IsFalse(publisher.Called);
    }

    private static Task<IResult> CreateAsync(MemoryReportStore store, IReportPublisher publisher)
    {
        ClaimsPrincipal principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Role, "Employee")], "test"));
        return ReportEndpoints.CreateAsync(new ReportRequest(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Mine"),
            principal, store, publisher, new ReportTestClock(), NullLogger<Report>.Instance, CancellationToken.None);
    }

    private sealed class ThrowingPublisher : IReportPublisher
    {
        public Task<bool> PublishConfirmedAsync(Guid reportId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("sensitive infrastructure details");
    }
}
