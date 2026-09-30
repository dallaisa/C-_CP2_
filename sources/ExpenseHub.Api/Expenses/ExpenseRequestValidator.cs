namespace ExpenseHub.Api.Expenses;

using System;
using System.Collections.Generic;
using System.Linq;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Endpoints;

/// <summary>
/// Validates draft expense requests against the ExpenseHub field contract.
/// </summary>
internal static class ExpenseRequestValidator
{
    /// <summary>Gets the minimum description length.</summary>
    internal const int DescriptionMinLength = 10;

    /// <summary>Gets the maximum description length.</summary>
    internal const int DescriptionMaxLength = 500;

    /// <summary>Gets the maximum number of decimal places accepted for amounts.</summary>
    internal const int AmountDecimalPlaces = 2;

    /// <summary>Validates a draft expense request.</summary>
    /// <param name="request">The request to validate.</param>
    /// <param name="todayUtc">The current server date in UTC, used to reject future dates.</param>
    /// <returns>The validation errors grouped by field; empty when the request is valid.</returns>
    internal static Dictionary<string, string[]> Validate(ExpenseRequest request, DateOnly todayUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        Dictionary<string, string[]> errors = RequestValidation.Validate(request);

        string? description = request.Description?.Trim();
        if (!errors.ContainsKey(nameof(ExpenseRequest.Description))
            && description is not null
            && (description.Length < DescriptionMinLength || description.Length > DescriptionMaxLength))
        {
            AddError(
                errors,
                nameof(ExpenseRequest.Description),
                "Description must have between 10 and 500 characters, ignoring surrounding spaces.");
        }

        if (!errors.ContainsKey(nameof(ExpenseRequest.Amount))
            && request.Amount is decimal amount
            && decimal.Round(amount, AmountDecimalPlaces) != amount)
        {
            AddError(errors, nameof(ExpenseRequest.Amount), "Amount must have at most 2 decimal places.");
        }

        if (request.ExpenseDate is DateOnly expenseDate && expenseDate > todayUtc)
        {
            AddError(errors, nameof(ExpenseRequest.ExpenseDate), "ExpenseDate cannot be in the future.");
        }

        return errors;
    }

    /// <summary>Converts a request that has already passed validation into draft values.</summary>
    /// <param name="request">The validated request.</param>
    /// <returns>The normalized draft values.</returns>
    internal static ExpenseDraftValues ToDraftValues(ExpenseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Description is null
            || request.Amount is null
            || request.ExpenseDate is null
            || request.CategoryId is null)
        {
            throw new ArgumentException("The request must be validated before conversion.", nameof(request));
        }

        return new ExpenseDraftValues
        {
            Description = request.Description.Trim(),
            Amount = request.Amount.Value,
            ExpenseDate = request.ExpenseDate.Value,
            ExpenseCategoryId = request.CategoryId.Value,
        };
    }

    private static void AddError(Dictionary<string, string[]> errors, string field, string message)
    {
        string[] existing = errors.GetValueOrDefault(field, Array.Empty<string>());
        errors[field] = existing.Append(message).ToArray();
    }
}
