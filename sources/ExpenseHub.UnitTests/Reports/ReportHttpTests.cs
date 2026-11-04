using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Endpoints;
using ExpenseHub.Api.Reports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>Exercises real HTTP middleware and JSON binding with in-memory transport and persistence fakes.</summary>
[TestClass]
public sealed class ReportHttpTests
{
    /// <summary>Every report route requires authentication and a functional report role.</summary>
    [TestMethod]
    [DataRow("GET", "/api/reports")]
    [DataRow("POST", "/api/reports")]
    [DataRow("GET", "/api/reports/00000000-0000-0000-0000-000000000001")]
    [DataRow("GET", "/api/reports/00000000-0000-0000-0000-000000000001/excel")]
    public async Task MiddlewareRejectsAnonymousAndUnauthorizedRoles(string method, string path)
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();
        foreach (string role in new[] { "anonymous", "Admin", "Approver", "Finance", "None" })
        {
            using HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), path);
            if (role != "anonymous")
            {
                request.Headers.Add("TestRole", role);
            }

            request.Content = new StringContent("{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\",\"scope\":\"Mine\"}", Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.SendAsync(request);
            Assert.AreEqual(role == "anonymous" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    /// <summary>Malformed or untrusted JSON is rejected before persistence.</summary>
    [TestMethod]
    [DataRow("{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\",\"scope\":\"Mine\",\"RequestedByUserId\":\"other\"}")]
    [DataRow("{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\",\"scope\":\"Mine\",\"userId\":\"other\"}")]
    [DataRow("{}")]
    [DataRow("{\"fromDate\":\"2026-02-30\",\"toDate\":\"2026-09-30\",\"scope\":\"Mine\"}")]
    [DataRow("{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\",\"scope\":\"Mine\",\"ownerId\":\"other\"}")]
    [DataRow("{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\",\"scope\":0}")]
    public async Task InvalidHttpBodyCreatesNoWork(string body)
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("TestRole", "Employee");
        using StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/reports", content);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsEmpty(((MemoryReportStore)app.Services.GetRequiredService<IReportStore>()).Reports);
    }

    /// <summary>Unconfirmed publication returns 503 while the persisted request stays Pending without Location.</summary>
    [TestMethod]
    public async Task PendingCreationAndOwnershipFlow()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("TestRole", "Employee");
        using StringContent content = new StringContent("{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\",\"scope\":\"Mine\"}", Encoding.UTF8, "application/json");
        using HttpResponseMessage created = await client.PostAsync("/api/reports", content);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, created.StatusCode);
        Assert.IsNull(created.Headers.Location);
        MemoryReportStore store = (MemoryReportStore)app.Services.GetRequiredService<IReportStore>();
        Guid id = store.Reports[0].Id;
        using HttpResponseMessage detail = await client.GetAsync($"/api/reports/{id}");
        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
        using HttpResponseMessage excel = await client.GetAsync($"/api/reports/{id}/excel");
        Assert.AreEqual(HttpStatusCode.Conflict, excel.StatusCode);
        ReportProcessor processor = new ReportProcessor(store, new ReportCalculator(new ReportTestClock()),
            new ReportTestClock(), NullLogger<ReportProcessor>.Instance);
        Assert.IsTrue(await processor.ProcessAsync(id, CancellationToken.None));
        using HttpResponseMessage completedExcel = await client.GetAsync($"/api/reports/{id}/excel");
        Assert.AreEqual(HttpStatusCode.OK, completedExcel.StatusCode);
        Assert.AreEqual(ReportExcelWriter.ContentType, completedExcel.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual("attachment", completedExcel.Content.Headers.ContentDisposition!.DispositionType);
        Assert.AreEqual($"report-{id}.xlsx", completedExcel.Content.Headers.ContentDisposition.FileNameStar);
        client.DefaultRequestHeaders.Add("TestUser", "other");
        using HttpResponseMessage hidden = await client.GetAsync($"/api/reports/{id}");
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    /// <summary>HTTP creation honors accumulated roles and persists only the authenticated requester and scope.</summary>
    [TestMethod]
    [DataRow("Employee", "Mine", 503)]
    [DataRow("Employee", "Global", 403)]
    [DataRow("Auditor", "Global", 503)]
    [DataRow("Auditor", "Mine", 403)]
    [DataRow("Employee,Auditor", "Mine", 503)]
    [DataRow("Employee,Auditor", "Global", 503)]
    [DataRow("Admin,Employee", "Mine", 503)]
    [DataRow("Admin,Employee", "Global", 403)]
    [DataRow("Approver,Auditor", "Global", 503)]
    [DataRow("Finance,Auditor", "Mine", 403)]
    [DataRow("Finance,Employee", "Mine", 503)]
    [DataRow("Admin,Approver,Finance", "Global", 403)]
    [DataRow("Employee", "Unknown", 400)]
    [DataRow("Auditor", "global", 400)]
    public async Task ScopeMatrixAndUntrustedParameters(string roles, string scope, int expected)
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("TestRole", roles);
        client.DefaultRequestHeaders.Add("TestUser", "employee-a");
        client.DefaultRequestHeaders.Add("X-User-Id", "employee-b");
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/reports?ownerId=employee-b&requestedByUserId=employee-b&scope=Global",
            new { fromDate = "2026-09-01", toDate = "2026-09-30", scope });
        Assert.AreEqual((HttpStatusCode)expected, response.StatusCode);
        MemoryReportStore store = (MemoryReportStore)app.Services.GetRequiredService<IReportStore>();
        if (expected != 503)
        {
            Assert.IsEmpty(store.Reports);
            return;
        }

        Report report = store.Reports.Single();
        Assert.AreEqual("employee-a", report.RequestedByUserId);
        Assert.AreEqual(scope, report.Scope.ToString());
        ReportPublicationFailure failure = (await response.Content.ReadFromJsonAsync<ReportPublicationFailure>())!;
        Assert.AreEqual(report.Id, failure.Id);
        Assert.AreEqual("REPORT_PUBLICATION_UNCONFIRMED", failure.Error.Code);
        Assert.AreEqual(ReportStatus.Pending, report.Status);
        Assert.IsNull(report.ErrorCode);
        Assert.IsNull(response.Headers.Location);
    }

    /// <summary>Two Employees and an Auditor cannot discover each other's envelopes or Excel at any state.</summary>
    [TestMethod]
    [DataRow("Pending")]
    [DataRow("Processing")]
    [DataRow("Completed")]
    [DataRow("Failed")]
    public async Task ThreeAccountsAreIsolatedAcrossAllStates(string status)
    {
        await using WebApplication app = await CreateAppAsync();
        MemoryReportStore store = (MemoryReportStore)app.Services.GetRequiredService<IReportStore>();
        string[] users = ["employee-a", "employee-b", "auditor"];
        foreach (string user in users)
        {
            store.Reports.Add(new Report
            {
                Id = Guid.NewGuid(),
                RequestedByUserId = user,
                Scope = user == "auditor" ? ReportScope.Global : ReportScope.Mine,
                FromDate = new DateOnly(2026, 9, 1),
                ToDate = new DateOnly(2026, 9, 30),
                CreatedAt = ReportTestClock.Now,
            });
        }

        store.Expenses.Add(new Expense { OwnerId = "employee-a", ExpenseDate = new DateOnly(2026, 9, 1), Amount = 100m });
        store.Expenses.Add(new Expense { OwnerId = "employee-b", ExpenseDate = new DateOnly(2026, 9, 30), Amount = 250m });
        ReportProcessor processor = new ReportProcessor(store, new ReportCalculator(new ReportTestClock()),
            new ReportTestClock(), NullLogger<ReportProcessor>.Instance);
        foreach (Report report in store.Reports)
        {
            if (status == "Completed")
            {
                Assert.IsTrue(await processor.ProcessAsync(report.Id, CancellationToken.None));
                decimal expected = report.RequestedByUserId == "auditor" ? 350m
                    : report.RequestedByUserId == "employee-a" ? 100m : 250m;
                Assert.AreEqual(expected, report.ReadResult()!.TotalAmount);
            }
            else
            {
                report.Status = Enum.Parse<ReportStatus>(status);
            }
        }

        foreach (string user in users)
        {
            using HttpClient client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("TestRole", user == "auditor" ? "Auditor" : "Employee");
            client.DefaultRequestHeaders.Add("TestUser", user);
            ReportResponse[] listed = (await client.GetFromJsonAsync<ReportResponse[]>("/api/reports?ownerId=someone-else"))!;
            Assert.HasCount(1, listed);
            Assert.AreEqual(store.Reports.Single(report => report.RequestedByUserId == user).Id, listed[0].Id);
            foreach (Report report in store.Reports)
            {
                bool own = report.RequestedByUserId == user;
                using HttpResponseMessage detail = await client.GetAsync($"/api/reports/{report.Id}");
                Assert.AreEqual(own ? HttpStatusCode.OK : HttpStatusCode.NotFound, detail.StatusCode);
                using HttpResponseMessage excel = await client.GetAsync($"/api/reports/{report.Id}/excel");
                Assert.AreEqual(!own ? HttpStatusCode.NotFound
                    : status == "Completed" ? HttpStatusCode.OK : HttpStatusCode.Conflict, excel.StatusCode);
                if (!own)
                {
                    Assert.AreEqual(string.Empty, await detail.Content.ReadAsStringAsync());
                    Assert.AreEqual(string.Empty, await excel.Content.ReadAsStringAsync());
                }
            }

            using HttpResponseMessage missing = await client.GetAsync($"/api/reports/{Guid.NewGuid()}/excel");
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    internal static async Task<WebApplication> CreateAppAsync(
        IReportPublisher? publisher = null, MemoryReportStore? store = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging(logging => logging.ClearProviders());
        builder.Services.AddSingleton<IReportStore>(store ?? new MemoryReportStore());
        builder.Services.AddSingleton<IReportPublisher>(publisher ?? new UnavailableReportPublisher());
        builder.Services.AddSingleton<TimeProvider, ReportTestClock>();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
        builder.Services.AddAuthorization();
        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapReportEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("TestRole", out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            string user = Request.Headers.TryGetValue("TestUser", out var supplied) ? supplied.ToString() : "owner";
            ClaimsIdentity identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test");
            foreach (string name in role.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, name));
            }

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "test")));
        }
    }
}
