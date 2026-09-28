namespace ExpenseHub.Api;

using System.Threading.Tasks;
using ExpenseHub.Api.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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

        WebApplication app = builder.Build();

        using (IServiceScope scope = app.Services.CreateScope())
        {
            ExpenseHubDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ExpenseHubDbContext>();

            await dbContext.Database.EnsureCreatedAsync();
        }

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("GetHealth");

        await app.RunAsync();
    }
}
