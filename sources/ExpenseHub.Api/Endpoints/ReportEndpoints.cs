using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Identity;
using ExpenseHub.Api.Reports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api.Endpoints;

/// <summary>Defines the I01 report contract; only I03 can confirm broker publication.</summary>
internal static class ReportEndpoints
{
    /// <summary>Maps report routes with explicit functional-role authorization.</summary>
    internal static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/reports")
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Employee, ApplicationRoles.Auditor));
        group.MapPost(string.Empty, CreateAsync);
        group.MapGet(string.Empty, ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/{id:guid}/excel", ExcelAsync);
        return endpoints;
    }

    /// <summary>Validates and persists a request before attempting confirmed publication.</summary>
    internal static async Task<IResult> CreateAsync(
        ReportRequest request, ClaimsPrincipal principal, IReportStore store,
        IReportPublisher publisher, TimeProvider clock, ILogger<Report> logger, CancellationToken cancellationToken)
    {
        string? userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        if (!request.IsValid)
        {
            return Results.BadRequest(new { error = "Informe fromDate <= toDate e scope Mine ou Global." });
        }

        if (!principal.IsInRole(request.Scope == "Mine" ? ApplicationRoles.Employee : ApplicationRoles.Auditor))
        {
            return Results.Forbid();
        }

        Report report = new Report
        {
            Id = Guid.NewGuid(),
            RequestedByUserId = userId,
            FromDate = request.FromDate!.Value,
            ToDate = request.ToDate!.Value,
            Scope = Enum.Parse<ReportScope>(request.Scope!),
            Status = ReportStatus.Pending,
            CreatedAt = clock.GetUtcNow().ToUniversalTime(),
        };
        try
        {
            await store.AddAsync(report, cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            logger.LogError(exception, "Could not persist report request");
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "REPORT_PERSISTENCE_UNAVAILABLE",
                detail: "Não foi possível registrar a solicitação de relatório.");
        }

        bool confirmed;
        try
        {
            confirmed = await publisher.PublishConfirmedAsync(report.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Report publication was not confirmed for {ReportId}", report.Id);
            confirmed = false;
        }

        ReportResponse response = ReportResponse.FromReport(report);
        if (confirmed)
        {
            return Results.Accepted($"/api/reports/{report.Id}", response);
        }

        // Publication uncertainty is not a calculation failure. Keep the persisted request Pending.
        return Results.Json(new ReportPublicationFailure(report.Id,
            new ReportError("REPORT_PUBLICATION_UNCONFIRMED", "A publicação do relatório não foi confirmada.")),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>Lists only the authenticated requester's saved envelopes.</summary>
    internal static async Task<IResult> ListAsync(
        ClaimsPrincipal principal, IReportStore store, CancellationToken cancellationToken)
    {
        string? userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        List<Report> reports = await store.ListOwnAsync(userId, cancellationToken);
        return Results.Ok(reports.OrderByDescending(report => report.CreatedAt)
            .Select(ReportResponse.FromReport).ToArray());
    }

    /// <summary>Reads a saved result without consulting current expenses.</summary>
    internal static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal principal, IReportStore store, CancellationToken cancellationToken)
    {
        string? userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        Report? report = await store.FindOwnAsync(id, userId, cancellationToken);
        return report is null ? Results.NotFound() : Results.Ok(ReportResponse.FromReport(report));
    }

    /// <summary>Downloads only the requester's completed, persisted snapshot.</summary>
    internal static async Task<IResult> ExcelAsync(
        Guid id, ClaimsPrincipal principal, IReportStore store,
        CancellationToken cancellationToken)
    {
        string? userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        Report? report = await store.FindOwnAsync(id, userId, cancellationToken);
        if (report is null)
        {
            return Results.NotFound();
        }

        if (report.Status != ReportStatus.Completed || report.ResultJson is null)
        {
            return Results.Conflict(new { error = "O relatório ainda não possui resultado concluído." });
        }

        return Results.File(ReportExcelWriter.Write(ReportResponse.FromReport(report)),
            ReportExcelWriter.ContentType, $"report-{report.Id}.xlsx");
    }
}
