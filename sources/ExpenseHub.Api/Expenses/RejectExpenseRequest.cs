using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Expenses;

/// <summary>
/// Represents the body of a rejection request.
/// </summary>
/// <remarks>
/// Only the justification is accepted; actor, time and states are always set by the server.
/// </remarks>
internal sealed class RejectExpenseRequest
{
    /// <summary>Gets the rejection justification.</summary>
    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(
        RejectExpenseRequestValidator.ReasonMaxLength,
        MinimumLength = RejectExpenseRequestValidator.ReasonMinLength,
        ErrorMessage = "Reason must have between 10 and 500 characters.")]
    public string? Reason { get; init; }
}
