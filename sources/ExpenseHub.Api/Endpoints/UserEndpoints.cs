using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Endpoints;

/// <summary>
/// Defines registration and user administration endpoints.
/// </summary>
internal static class UserEndpoints
{
    private static readonly HashSet<string> _allowedRoles = new HashSet<string>(StringComparer.Ordinal)
    {
        ApplicationRoles.Admin,
        ApplicationRoles.Employee,
        ApplicationRoles.Approver,
        ApplicationRoles.Finance,
        ApplicationRoles.Auditor,
    };

    /// <summary>Maps user registration and administration endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder with user routes mapped.</returns>
    internal static IEndpointRouteBuilder MapUserEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/register", RegisterAsync)
            .AllowAnonymous();

        endpoints.MapGet("/api/admin/users", ListUsersAsync)
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Admin));

        endpoints.MapPut("/api/admin/users/{id}/roles", UpdateRolesAsync)
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Admin));

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<AppUser> userManager)
    {
        Dictionary<string, string[]> validationErrors = RequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        string email = request.Email.Trim();
        AppUser? existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "An account with this email already exists.");
        }

        AppUser user = new AppUser
        {
            Email = email,
            UserName = email,
        };

        IdentityResult result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code.StartsWith("Duplicate", StringComparison.Ordinal)))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "An account with this email already exists.");
            }

            return Results.ValidationProblem(ToErrorDictionary(result.Errors));
        }

        return Results.Created(
            $"/api/admin/users/{user.Id}",
            new RegisteredUserResponse(user.Id, user.Email));
    }

    private static async Task<IResult> ListUsersAsync(
        UserManager<AppUser> userManager,
        CancellationToken cancellationToken)
    {
        // The query is materialized asynchronously before the per-user role lookups run.
        List<AppUser> appUsers = await userManager.Users
            .OrderBy(user => user.Email)
            .ToListAsync(cancellationToken);

        List<UserResponse> users = new List<UserResponse>();
        foreach (AppUser user in appUsers)
        {
            IList<string> roles = await userManager.GetRolesAsync(user);
            users.Add(new UserResponse(user.Id, user.Email, roles));
        }

        return Results.Ok(users);
    }

    private static async Task<IResult> UpdateRolesAsync(
        string id,
        UpdateRolesRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        Dictionary<string, string[]> validationErrors = RequestValidation.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        List<string> requestedRoles = request.Roles!;
        if (requestedRoles.Any(string.IsNullOrWhiteSpace))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Role names cannot be empty.");
        }

        string[] desiredRoles = requestedRoles
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string? invalidRole = desiredRoles.FirstOrDefault(role => !_allowedRoles.Contains(role));
        if (invalidRole is not null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The requested role is not allowed.",
                detail: $"'{invalidRole}' is not an assignable role.");
        }

        foreach (string role in desiredRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "The requested role does not exist.",
                    detail: $"The role '{role}' must exist before it can be assigned.");
            }
        }

        AppUser? user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "User not found.");
        }

        IList<string> currentRoles = await userManager.GetRolesAsync(user);
        string? currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        bool isSelf = string.Equals(currentUserId, user.Id, StringComparison.Ordinal);
        bool currentlyAdmin = currentRoles.Contains(ApplicationRoles.Admin, StringComparer.Ordinal);
        bool remainsAdmin = desiredRoles.Contains(ApplicationRoles.Admin, StringComparer.Ordinal);

        if (isSelf && currentlyAdmin && !remainsAdmin)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The Admin role cannot be removed from the current user.");
        }

        string[] rolesToRemove = currentRoles
            .Where(role => !desiredRoles.Contains(role, StringComparer.Ordinal))
            .ToArray();
        string[] rolesToAdd = desiredRoles
            .Where(role => !currentRoles.Contains(role, StringComparer.Ordinal))
            .ToArray();

        if (rolesToRemove.Length > 0)
        {
            IdentityResult removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                return Results.ValidationProblem(ToErrorDictionary(removeResult.Errors));
            }
        }

        if (rolesToAdd.Length > 0)
        {
            IdentityResult addResult = await userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                return Results.ValidationProblem(ToErrorDictionary(addResult.Errors));
            }
        }

        IList<string> updatedRoles = await userManager.GetRolesAsync(user);
        return Results.Ok(new UserResponse(user.Id, user.Email, updatedRoles));
    }

    private static Dictionary<string, string[]> ToErrorDictionary(IEnumerable<IdentityError> errors)
    {
        return errors
            .GroupBy(error => error.Code)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray());
    }

    private sealed class RegisterRequest
    {
        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; init; } = string.Empty;

        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string Password { get; init; } = string.Empty;
    }

    private sealed class UpdateRolesRequest
    {
        [Required]
        public List<string>? Roles { get; init; }
    }

    private sealed class RegisteredUserResponse
    {
        /// <summary>Initializes a new instance of the <see cref="RegisteredUserResponse"/> class.</summary>
        /// <param name="id">The new user's identifier.</param>
        /// <param name="email">The new user's email address.</param>
        internal RegisteredUserResponse(string id, string? email)
        {
            Id = id;
            Email = email;
        }

        /// <summary>Gets the new user's identifier.</summary>
        public string Id { get; }

        /// <summary>Gets the user's email address.</summary>
        public string? Email { get; }
    }

    private sealed class UserResponse
    {
        /// <summary>Initializes a new instance of the <see cref="UserResponse"/> class.</summary>
        /// <param name="id">The user's identifier.</param>
        /// <param name="email">The user's email address.</param>
        /// <param name="roles">The user's current roles.</param>
        internal UserResponse(string id, string? email, IList<string> roles)
        {
            Id = id;
            Email = email;
            Roles = roles;
        }

        /// <summary>Gets the user's identifier.</summary>
        public string Id { get; }

        /// <summary>Gets the user's email address.</summary>
        public string? Email { get; }

        /// <summary>Gets the user's current roles.</summary>
        public IList<string> Roles { get; }
    }
}
