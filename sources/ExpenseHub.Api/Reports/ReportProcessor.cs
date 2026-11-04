using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api.Reports;

/// <summary>Processes persisted requests; invoked by the future I03 worker, never by HTTP endpoints.</summary>
internal sealed class ReportProcessor(
    IReportStore store, ReportCalculator calculator, TimeProvider clock, ILogger<ReportProcessor> logger)
{
    /// <summary>Claims Pending, computes once and persists a terminal snapshot or sanitized error.</summary>
    /// <remarks>False means unclaimed or unsaved work; I03 must define retry and recovery handling.</remarks>
    internal async Task<bool> ProcessAsync(Guid reportId, CancellationToken cancellationToken)
    {
        Report? report = await store.FindAsync(reportId, cancellationToken);
        if (report is null)
        {
            return false;
        }

        if (report.Status is ReportStatus.Completed or ReportStatus.Failed)
        {
            return true;
        }

        if (report.Status != ReportStatus.Pending)
        {
            return false;
        }

        report.Status = ReportStatus.Processing;
        report.StartedAt = clock.GetUtcNow().ToUniversalTime();
        if (!await store.SaveAsync(cancellationToken))
        {
            return false;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
        return await store.SaveAsync(cancellationToken);
    }
}
