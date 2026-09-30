namespace ExpenseHub.Api.Persistence;

using System;
using System.Linq;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Initializes the database and required identity records.
/// </summary>
internal static class DatabaseInitializer
{
    private static readonly ExpenseCategory[] DefaultCategories =
    [
        new ExpenseCategory { Id = 1, Name = "Alimentação" },
        new ExpenseCategory { Id = 2, Name = "Transporte" },
        new ExpenseCategory { Id = 3, Name = "Hospedagem" },
        new ExpenseCategory { Id = 4, Name = "Material de escritório" },
        new ExpenseCategory { Id = 5, Name = "Outros" },
    ];

    /// <summary>Creates the database and seeds configured identity data.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>A task that represents the asynchronous initialization operation.</returns>
    internal static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration)
    {
        using IServiceScope scope = services.CreateScope();

        ExpenseHubDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ExpenseHubDbContext>();

        await dbContext.Database.EnsureCreatedAsync();
        await SeedCategoriesAsync(dbContext);

        RoleManager<IdentityRole> roleManager =
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        string[] roleNames =
        [
            ApplicationRoles.Admin,
            ApplicationRoles.Employee,
            ApplicationRoles.Approver,
            ApplicationRoles.Finance,
            ApplicationRoles.Auditor,
        ];

        foreach (string roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                IdentityResult roleResult =
                    await roleManager.CreateAsync(new IdentityRole(roleName));

                EnsureSucceeded(roleResult, $"create role {roleName}");
            }
        }

        string? adminEmail = configuration["SeedAdmin:Email"];
        string? adminPassword = configuration["SeedAdmin:Password"];

        if (string.IsNullOrWhiteSpace(adminEmail)
            || string.IsNullOrWhiteSpace(adminPassword))
        {
            return;
        }

        UserManager<AppUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        AppUser? admin = await userManager.FindByEmailAsync(adminEmail);

        if (admin is null)
        {
            admin = new AppUser
            {
                Email = adminEmail,
                UserName = adminEmail,
                EmailConfirmed = true,
            };

            IdentityResult createResult =
                await userManager.CreateAsync(admin, adminPassword);

            EnsureSucceeded(createResult, "create initial Admin");
        }

        if (!await userManager.IsInRoleAsync(admin, ApplicationRoles.Admin))
        {
            IdentityResult addRoleResult =
                await userManager.AddToRoleAsync(admin, ApplicationRoles.Admin);

            EnsureSucceeded(addRoleResult, "assign Admin role");
        }
    }

    private static async Task SeedCategoriesAsync(ExpenseHubDbContext dbContext)
    {
        foreach (ExpenseCategory category in DefaultCategories)
        {
            bool exists = await dbContext.ExpenseCategories.AnyAsync(
                existing => existing.Id == category.Id || existing.Name == category.Name);

            if (!exists)
            {
                dbContext.ExpenseCategories.Add(
                    new ExpenseCategory { Id = category.Id, Name = category.Name });
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        string errors = string.Join(
            "; ",
            result.Errors.Select(error => error.Description));

        throw new InvalidOperationException(
            $"Could not {operation}: {errors}");
    }
}
