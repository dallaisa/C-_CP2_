using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Persistence;

/// <summary>Additively upgrades existing CP2 databases without replacing EnsureCreated or deleting data.</summary>
internal static class ReportSchemaInitializer
{
    /// <summary>Creates the I01 table and owner index if absent; safe to run on every startup.</summary>
    internal static async Task InitializeAsync(ExpenseHubDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Reports" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Reports" PRIMARY KEY,
                "RequestedByUserId" TEXT NOT NULL,
                "FromDate" TEXT NOT NULL,
                "ToDate" TEXT NOT NULL,
                "Scope" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "StartedAt" TEXT NULL,
                "CompletedAt" TEXT NULL,
                "ResultJson" TEXT NULL,
                "ErrorCode" TEXT NULL,
                "ErrorMessage" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_Reports_RequestedByUserId"
                ON "Reports" ("RequestedByUserId");
            """);
    }
}
