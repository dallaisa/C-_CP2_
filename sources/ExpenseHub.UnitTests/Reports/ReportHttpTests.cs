using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>The default I01 response is Pending without a false acceptance or Location.</summary>
    [TestMethod]
    public async Task PendingCreationAndOwnershipFlow()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("TestRole", "Employee");
        using StringContent content = new StringContent("{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\",\"scope\":\"Mine\"}", Encoding.UTF8, "application/json");
        using HttpResponseMessage created = await client.PostAsync("/api/reports", content);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
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

    private static async Task<WebApplication> CreateAppAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging(logging => logging.ClearProviders());
        builder.Services.AddSingleton<IReportStore, MemoryReportStore>();
        builder.Services.AddSingleton<IReportPublisher, UnavailableReportPublisher>();
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
            ClaimsIdentity identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Role, role.ToString())], "test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "test")));
        }
    }
}
