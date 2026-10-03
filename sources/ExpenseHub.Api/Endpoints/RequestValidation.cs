using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace ExpenseHub.Api.Endpoints;

/// <summary>
/// Validates request DTOs using their declarative data annotations.
/// </summary>
internal static class RequestValidation
{
    /// <summary>Validates all annotated properties of a request model.</summary>
    /// <param name="model">The request model to validate.</param>
    /// <returns>The validation errors grouped by member name; empty when the model is valid.</returns>
    internal static Dictionary<string, string[]> Validate(object model)
    {
        List<ValidationResult> results = new List<ValidationResult>();
        bool isValid = Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true);

        if (isValid)
        {
            return new Dictionary<string, string[]>();
        }

        return results
            .SelectMany(
                result => result.MemberNames.DefaultIfEmpty(string.Empty),
                (result, member) => new { Member = member, Error = result.ErrorMessage ?? "Invalid value." })
            .GroupBy(item => item.Member)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Error).ToArray());
    }
}
