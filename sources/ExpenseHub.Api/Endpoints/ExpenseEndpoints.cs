namespace ExpenseHub.Api.Endpoints;

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Expenses;
using ExpenseHub.Api.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Defines expense endpoints.
/// </summary>
internal static class ExpenseEndpoints
{
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

        return endpoints;
    }

    private static async Task<IResult> CreateDraftAsync(
        ExpenseRequest request,
        ClaimsPrincipal principal,
        ExpenseService expenseService,
        CancellationToken cancellationToken)
    {
        string? ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Results.Unauthorized();
        }

        ExpenseOperationResult result =
            await expenseService.CreateDraftAsync(ownerId, request, cancellationToken);

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
        string? actorId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return Results.Unauthorized();
        }

        ExpenseOperationResult result =
            await expenseService.UpdateDraftAsync(id, actorId, request, cancellationToken);

        if (result.Status == ExpenseOperationStatus.Success && result.Expense is not null)
        {
            return Results.Ok(ExpenseResponse.FromExpense(result.Expense));
        }

        return ToProblem(result);
    }

    private static IResult ToProblem(ExpenseOperationResult result) =>
        result.Status switch
        {
            ExpenseOperationStatus.Invalid => Results.ValidationProblem(result.Errors),
            ExpenseOperationStatus.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Expense not found."),
            ExpenseOperationStatus.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Only expenses in Draft can be edited."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unexpected expense operation result."),
        };
}
