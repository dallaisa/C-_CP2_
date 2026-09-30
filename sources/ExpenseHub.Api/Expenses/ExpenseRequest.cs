namespace ExpenseHub.Api.Expenses;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// Represents the fields a client may send to create or edit a draft expense.
/// </summary>
/// <remarks>
/// Owner, status, actors and timestamps are intentionally absent: they are always
/// derived from the authenticated user and the server, so extra JSON properties are ignored.
/// </remarks>
internal sealed class ExpenseRequest
{
    /// <summary>Gets the expense description.</summary>
    [Required(ErrorMessage = "Description is required.")]
    [StringLength(
        ExpenseRequestValidator.DescriptionMaxLength,
        MinimumLength = ExpenseRequestValidator.DescriptionMinLength,
        ErrorMessage = "Description must have between 10 and 500 characters.")]
    public string? Description { get; init; }

    /// <summary>Gets the expense amount.</summary>
    [Required(ErrorMessage = "Amount is required.")]
    [Range(
        typeof(decimal),
        "0.01",
        "2147483647",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true,
        ErrorMessage = "Amount must be between 0.01 and 2147483647.")]
    public decimal? Amount { get; init; }

    /// <summary>Gets the date on which the expense occurred.</summary>
    [Required(ErrorMessage = "ExpenseDate is required.")]
    public DateOnly? ExpenseDate { get; init; }

    /// <summary>Gets the identifier of the expense category.</summary>
    [Required(ErrorMessage = "CategoryId is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "CategoryId must be a valid category.")]
    public int? CategoryId { get; init; }
}
