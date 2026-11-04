using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api.Reports;

/// <summary>Processes persisted requests from the independent worker, never from HTTP endpoints.</summary>
internal sealed class ReportProcessor(
    IReportStore store, ReportCalculator calculator, TimeProvider clock, ILogger<ReportProcessor> logger)
{
    /// <summary>Claims Pending or reclaims Processing and fences terminal writes by state and attempt timestamp.</summary>
    /// <remarks>False means missing, unclaimed or unsaved work; never acknowledge it as a saved terminal result.</remarks>
    internal async Task<bool> ProcessAsync(Guid reportId, CancellationToken cancellationToken)
    {
        Report? report = await store.FindAsync(reportId, cancellationToken);
        if (report is null)
        {
            return false;
        }

        if (report.Status is ReportStatus.Completed or ReportStatus.Failed)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Report {ReportId} is already {Status}; duplicate delivery ignored without new effect",
                    reportId, report.Status);
            }

            return true;
        }

        DateTimeOffset now = clock.GetUtcNow().ToUniversalTime();
        // Strictly advances even with clock skew or fixed clocks. EF compares the previous value
        // as well as Status, so an older attempt cannot overwrite a reclaimed or terminal report.
        report.StartedAt = report.StartedAt is { } previous && now <= previous ? previous.AddTicks(1) : now;
        report.Status = ReportStatus.Processing;
        if (!await store.SaveAsync(cancellationToken))
        {
            return false;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Processing report {ReportId}", reportId);
        }

        try
        {
            List<Expense> expenses = await store.ReadExpensesAsync(report, cancellationToken);
            ReportResult result = calculator.Calculate(expenses, report.FromDate, report.ToDate,
                report.Scope, report.RequestedByUserId);
            report.ResultJson = JsonSerializer.Serialize(result);
            report.Status = ReportStatus.Completed;
            report.ErrorCode = null;
            report.ErrorMessage = null;
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Report calculation failed for {ReportId}", reportId);
            report.Status = ReportStatus.Failed;
            report.ResultJson = null;
            report.ErrorCode = "REPORT_PROCESSING_FAILED";
            report.ErrorMessage = "Não foi possível gerar o relatório.";
        }

        report.CompletedAt = clock.GetUtcNow().ToUniversalTime();
        bool saved = await store.SaveAsync(cancellationToken);
        if (saved && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Report {ReportId} saved as {Status}", reportId, report.Status);
        }

        return saved;
    }
}
