using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Expenses;
using ExpenseHub.Api.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExpenseHub.Api.Endpoints;

/// <summary>
/// Defines expense endpoints.
/// </summary>
internal static class ExpenseEndpoints
{
    private static readonly string[] _readerRoles =
    [
        ApplicationRoles.Employee,
        ApplicationRoles.Approver,
        ApplicationRoles.Finance,
        ApplicationRoles.Auditor,
    ];

    /// <summary>Maps expense endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder with expense routes mapped.</returns>
    internal static IEndpointRouteBuilder MapExpenseEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/expenses", CreateDraftAsync)
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Employee));

        endpoints.MapPut("/api/expenses/{id:guid}", UpdateDraftAsync)
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Employee));

        endpoints.MapPost("/api/expenses/{id:guid}/submit", SubmitAsync)
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Employee));

        endpoints.MapPost("/api/expenses/{id:guid}/approve", ApproveAsync)
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Approver));

        endpoints.MapPost("/api/expenses/{id:guid}/reject", RejectAsync)
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Approver));

        endpoints.MapPost("/api/expenses/{id:guid}/pay", PayAsync)
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Finance));

        // Admin is intentionally absent: it grants no functional access to expenses.
        endpoints.MapGet("/api/expenses", ListAsync)
            .RequireAuthorization(policy => policy.RequireRole(_readerRoles));

        endpoints.MapGet("/api/expenses/{id:guid}", GetByIdAsync)
            .RequireAuthorization(policy => policy.RequireRole(_readerRoles));

        endpoints.MapGet("/api/expenses/{id:guid}/history", GetHistoryAsync)
            .RequireAuthorization(policy => policy.RequireRole(_readerRoles));

        return endpoints;
    }

    private static async Task<IResult> CreateDraftAsync(
        ExpenseRequest request,
        ClaimsPrincipal principal,
        ExpenseService expenseService,
        CancellationToken cancellationToken)
    {
        ExpenseViewer? actor = CreateViewer(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        ExpenseOperationResult result =
            await expenseService.CreateDraftAsync(actor, request, cancellationToken);

        if (result.Status == ExpenseOperationStatus.Success && result.Expense is not null)
        {
            return Results.Created(
                $"/api/expenses/{result.Expense.Id}",
                ExpenseResponse.FromExpense(result.Expense));
        }

        return ToProblem(result);
    }

    private static async Task<IResult> UpdateDraftAsync(
        Guid id,
        ExpenseRequest request,
        ClaimsPrincipal principal,
        ExpenseService expenseService,
        CancellationToken cancellationToken)
    {
        ExpenseViewer? actor = CreateViewer(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        ExpenseOperationResult result =
            await expenseService.UpdateDraftAsync(id, actor, request, cancellationToken);

        if (result.Status == ExpenseOperationStatus.Success && result.Expense is not null)
        {
            return Results.Ok(ExpenseResponse.FromExpense(result.Expense));
        }

        return ToProblem(result);
    }

    private static async Task<IResult> SubmitAsync(
        Guid id,
        ClaimsPrincipal principal,
        ExpenseService expenseService,
        CancellationToken cancellationToken)
    {
        ExpenseViewer? actor = CreateViewer(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        ExpenseOperationResult result =
            await expenseService.SubmitAsync(id, actor, cancellationToken);

        if (result.Status == ExpenseOperationStatus.Success && result.Expense is not null)
        {
            return Results.Ok(ExpenseResponse.FromExpense(result.Expense));
        }

        return ToProblem(result);
    }

    private static async Task<IResult> ApproveAsync(
        Guid id,
        ClaimsPrincipal principal,
        ExpenseService expenseService,
        CancellationToken cancellationToken)
    {
        ExpenseViewer? actor = CreateViewer(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        ExpenseOperationResult result =
            await expenseService.ApproveAsync(id, actor, cancellationToken);

        return ToResponse(result);
    }

    private static async Task<IResult> RejectAsync(
        Guid id,
        RejectExpenseRequest request,
        ClaimsPrincipal principal,
        ExpenseService expenseService,
        CancellationToken cancellationToken)
    {
        ExpenseViewer? actor = CreateViewer(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        ExpenseOperationResult result =
            await expenseService.RejectAsync(id, actor, request, cancellationToken);

        return ToResponse(result);
    }

    private static async Task<IResult> PayAsync(
        Guid id,
        ClaimsPrincipal principal,
        ExpenseService expenseService,
        CancellationToken cancellationToken)
    {
        ExpenseViewer? actor = CreateViewer(principal);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        ExpenseOperationResult result =
            await expenseService.PayAsync(id, actor, cancellationToken);

        return ToResponse(result);
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        ExpenseService expenseService,
        CancellationToken cancellationToken)
    {
        ExpenseViewer? viewer = CreateViewer(principal);
        if (viewer is null)
        {
            return Results.Unauthorized();
        }

        List<Expense> expenses = await expenseService.ListVisibleAsync(viewer, cancellationToken);
        return Results.Ok(expenses.Select(ExpenseResponse.FromExpense).ToList());
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ClaimsPrincipal principal,
        ExpenseService expenseService,
        CancellationToken cancellationToken)
    {
        ExpenseViewer? viewer = CreateViewer(principal);
        if (viewer is null)
        {
            return Results.Unauthorized();
        }

        // A missing expense and one outside the viewer's scope produce the same response.
        Expense? expense = await expenseService.FindVisibleAsync(id, viewer, cancellationToken);
        return expense is null
            ? ToProblem(ExpenseOperationResult.NotFound())
            : Results.Ok(ExpenseResponse.FromExpense(expense));
    }

    private static async Task<IResult> GetHistoryAsync(
        Guid id,
        ClaimsPrincipal principal,
        ExpenseService expenseService,
        CancellationToken cancellationToken)
    {
        ExpenseViewer? viewer = CreateViewer(principal);
        if (viewer is null)
        {
            return Results.Unauthorized();
        }

        // A missing expense and one outside the viewer's scope produce the same response.
        List<ExpenseHistory>? history = await expenseService.ListHistoryAsync(id, viewer, cancellationToken);
        return history is null
            ? ToProblem(ExpenseOperationResult.NotFound())
            : Results.Ok(history.Select(ExpenseHistoryResponse.FromHistory).ToList());
    }

    private static ExpenseViewer? CreateViewer(ClaimsPrincipal principal)
    {
        string? userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return new ExpenseViewer
        {
            UserId = userId,
            IsEmployee = principal.IsInRole(ApplicationRoles.Employee),
            IsApprover = principal.IsInRole(ApplicationRoles.Approver),
            IsFinance = principal.IsInRole(ApplicationRoles.Finance),
            IsAuditor = principal.IsInRole(ApplicationRoles.Auditor),
        };
    }

    private static IResult ToResponse(ExpenseOperationResult result) =>
        result.Status == ExpenseOperationStatus.Success && result.Expense is not null
            ? Results.Ok(ExpenseResponse.FromExpense(result.Expense))
            : ToProblem(result);

    private static IResult ToProblem(ExpenseOperationResult result) =>
        result.Status switch
        {
            ExpenseOperationStatus.Invalid => Results.ValidationProblem(result.Errors),
            ExpenseOperationStatus.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Expense not found."),
            ExpenseOperationStatus.Forbidden => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "The operation is not allowed for the current user."),
            ExpenseOperationStatus.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: result.Message ?? "The expense state does not allow this operation."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unexpected expense operation result."),
        };
}
