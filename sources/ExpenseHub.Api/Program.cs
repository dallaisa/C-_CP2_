namespace ExpenseHub.Api;

using System.Threading.Tasks;
using ExpenseHub.Api.Endpoints;
using ExpenseHub.Api.Identity;
using ExpenseHub.Api.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Defines the application entry point.
/// </summary>
internal static class Program
{
    /// <summary>Builds and runs the ExpenseHub API.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>A task that represents the lifetime of the API application.</returns>
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        string connectionString = builder.Configuration.GetConnectionString("ExpenseHub")
            ?? "Data Source=expensehub.db";

        builder.Services.AddOpenApi();

        builder.Services.AddDbContext<ExpenseHubDbContext>(
            options => options.UseSqlite(connectionString));

        builder.Services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ExpenseHubDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.BearerScheme;
                options.DefaultChallengeScheme = IdentityConstants.BearerScheme;
                options.DefaultForbidScheme = IdentityConstants.BearerScheme;
            })
            .AddBearerToken(IdentityConstants.BearerScheme);

        builder.Services.AddAuthorization();

        WebApplication app = builder.Build();

        await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("GetHealth");

        app.MapAuthEndpoints();
        app.MapUserEndpoints();

        app.MapGet(
                "/api/admin/health",
                () => Results.Ok(new { status = "authorized" }))
            .RequireAuthorization(
                policy => policy.RequireRole(ApplicationRoles.Admin));

        await app.RunAsync();
    }
}
