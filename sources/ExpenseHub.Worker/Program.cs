using System;
using System.IO;
using System.Threading.Tasks;
using ExpenseHub.Api.Messaging;
using ExpenseHub.Api.Persistence;
using ExpenseHub.Api.Reports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExpenseHub.Worker;

/// <summary>Independent report worker entry point.</summary>
internal static class Program
{
    /// <summary>Builds and runs the worker using externally configured broker and shared database.</summary>
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        // Do not inherit the referenced API's relative database path or configuration file.
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Worker.json"), optional: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
        string connectionString = builder.Configuration.GetConnectionString("ExpenseHub")
            ?? throw new InvalidOperationException("Configure ConnectionStrings__ExpenseHub with the API database path.");
        SqliteConnectionStringBuilder sqlite = new SqliteConnectionStringBuilder(connectionString);
        if (!Path.IsPathFullyQualified(sqlite.DataSource))
        {
            throw new InvalidOperationException("The worker requires an absolute path to the existing API database.");
        }

        sqlite.Mode = SqliteOpenMode.ReadWrite;
        builder.Services.AddDbContext<ExpenseHubDbContext>(options => options.UseSqlite(sqlite.ToString()));
        builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
        builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ReportCalculator>();
        builder.Services.AddScoped<IReportStore, EfReportStore>();
        builder.Services.AddScoped<ReportProcessor>();
        builder.Services.AddSingleton<ReportDeliveryHandler>();
        builder.Services.AddHostedService<ReportWorker>();
        using IHost host = builder.Build();
        // The API initializes the schema first. Never create a separate empty database silently.
        await host.RunAsync();
    }
}
