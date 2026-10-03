using System.Collections.Generic;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Expenses;

/// <summary>
/// Carries the result of an expense operation from the service to the endpoint.
/// </summary>
internal sealed class ExpenseOperationResult
{
    /// <summary>Gets the operation status.</summary>
    public ExpenseOperationStatus Status { get; init; }

    /// <summary>Gets the affected expense when the operation succeeded.</summary>
    public Expense? Expense { get; init; }

    /// <summary>Gets a human readable explanation of a conflict.</summary>
    public string? Message { get; init; }

    /// <summary>Gets the validation errors when the input is invalid.</summary>
    public IDictionary<string, string[]> Errors { get; init; } = new Dictionary<string, string[]>();

    /// <summary>Creates a successful result.</summary>
    /// <param name="expense">The affected expense.</param>
    /// <returns>A successful result.</returns>
    internal static ExpenseOperationResult Success(Expense expense) =>
        new ExpenseOperationResult { Status = ExpenseOperationStatus.Success, Expense = expense };

    /// <summary>Creates a validation failure result.</summary>
    /// <param name="errors">The validation errors grouped by field.</param>
    /// <returns>A validation failure result.</returns>
    internal static ExpenseOperationResult Invalid(IDictionary<string, string[]> errors) =>
        new ExpenseOperationResult { Status = ExpenseOperationStatus.Invalid, Errors = errors };

    /// <summary>Creates a not found result.</summary>
    /// <returns>A not found result.</returns>
    internal static ExpenseOperationResult NotFound() =>
        new ExpenseOperationResult { Status = ExpenseOperationStatus.NotFound };

    /// <summary>Creates a forbidden result.</summary>
    /// <returns>A forbidden result.</returns>
    internal static ExpenseOperationResult Forbidden() =>
        new ExpenseOperationResult { Status = ExpenseOperationStatus.Forbidden };

    /// <summary>Creates a state conflict result.</summary>
    /// <param name="message">The explanation of the conflict.</param>
    /// <returns>A conflict result.</returns>
    internal static ExpenseOperationResult Conflict(string message) =>
        new ExpenseOperationResult { Status = ExpenseOperationStatus.Conflict, Message = message };
}
