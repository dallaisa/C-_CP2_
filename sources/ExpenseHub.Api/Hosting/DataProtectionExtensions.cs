using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseHub.Api.Hosting;

/// <summary>Keeps the authentication keys across restarts and container recreation when configured.</summary>
internal static class DataProtectionExtensions
{
    /// <summary>Persists Data Protection keys to <c>DataProtection:KeysPath</c> when that setting is present.</summary>
    /// <remarks>Without the setting, the framework default is kept, so local development is unchanged.</remarks>
    internal static IServiceCollection AddExpenseHubDataProtection(
        this IServiceCollection services, IConfiguration configuration)
    {
        string? keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            services.AddDataProtection()
                .SetApplicationName("ExpenseHub")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        return services;
    }
}
