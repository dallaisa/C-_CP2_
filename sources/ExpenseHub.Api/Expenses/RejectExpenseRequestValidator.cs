using System;
using System.Collections.Generic;
using ExpenseHub.Api.Endpoints;

namespace ExpenseHub.Api.Expenses;

/// <summary>
/// Validates rejection requests against the justification contract.
/// </summary>
internal static class RejectExpenseRequestValidator
{
    /// <summary>Gets the minimum justification length.</summary>
    internal const int ReasonMinLength = 10;

    /// <summary>Gets the maximum justification length.</summary>
    internal const int ReasonMaxLength = 500;

    /// <summary>Validates a rejection request.</summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>The validation errors grouped by field; empty when the request is valid.</returns>
    internal static Dictionary<string, string[]> Validate(RejectExpenseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Dictionary<string, string[]> errors = RequestValidation.Validate(request);

        string? reason = request.Reason?.Trim();
        if (!errors.ContainsKey(nameof(RejectExpenseRequest.Reason))
            && reason is not null
            && (reason.Length < ReasonMinLength || reason.Length > ReasonMaxLength))
        {
            errors[nameof(RejectExpenseRequest.Reason)] = new[]
            {
                "Reason must have between 10 and 500 characters, ignoring surrounding spaces.",
            };
        }

        return errors;
    }
}
