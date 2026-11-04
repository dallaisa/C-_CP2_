using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpenseHub.Api.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Hosting;

/// <summary>CP3-I05: authentication keys must survive recreating the web container.</summary>
[TestClass]
public sealed class DataProtectionExtensionsTests
{
    /// <summary>A payload protected by one provider is readable by a new provider using the same keys directory.</summary>
    [TestMethod]
    public void ConfiguredKeysDirectoryPreservesProtectedPayloadAcrossProviders()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"expensehub-keys-{Guid.NewGuid():N}");
        try
        {
            IConfiguration configuration = Configuration(directory);
            string protectedValue;
            using (ServiceProvider first = Build(configuration))
            {
                protectedValue = first.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("bearer").Protect("token-payload");
            }

            using ServiceProvider recreated = Build(configuration);
            string original = recreated.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("bearer").Unprotect(protectedValue);

            Assert.AreEqual("token-payload", original);
            Assert.IsTrue(Directory.EnumerateFiles(directory, "key-*.xml").Any());
        }
        finally
        {
            // Only the randomly named directory created above is removed.
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Without the setting nothing is registered, so local development keeps the framework default.</summary>
    [TestMethod]
    public void MissingSettingDoesNotRegisterDataProtection()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddExpenseHubDataProtection(new ConfigurationBuilder().Build());

        Assert.IsEmpty(services);
    }

    private static ServiceProvider Build(IConfiguration configuration)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddExpenseHubDataProtection(configuration);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(string keysPath) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeysPath"] = keysPath })
        .Build();
}
