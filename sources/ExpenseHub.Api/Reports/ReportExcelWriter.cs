using System;
using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ExpenseHub.Api.Reports;

/// <summary>Creates a real Open XML workbook exclusively from a saved report snapshot.</summary>
internal static class ReportExcelWriter
{
    /// <summary>The registered XLSX media type.</summary>
    internal const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Writes metadata, five state rows and totals with numeric cells.</summary>
    internal static byte[] Write(ReportResponse report)
    {
        if (report.Status != nameof(ReportStatus.Completed) || report.Result is null)
        {
            throw new InvalidOperationException("Only completed reports have a workbook.");
        }

        using MemoryStream stream = new MemoryStream();
        using (SpreadsheetDocument document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            WorksheetPart worksheet = workbook.AddNewPart<WorksheetPart>();
            SheetData data = new SheetData();
            worksheet.Worksheet = new Worksheet(data);
            Sheets sheets = workbook.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = "Reembolsos" });

            data.Append(new Row(Text("Id"), Text(report.Id.ToString())));
            data.Append(new Row(Text("Scope"), Text(report.Scope)));
            data.Append(new Row(Text("FromDate"), Text(report.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
            data.Append(new Row(Text("ToDate"), Text(report.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
            data.Append(new Row(Text("GeneratedAt"), Text(report.Result.GeneratedAt.ToString("O", CultureInfo.InvariantCulture))));
            data.Append(new Row(Text("Status"), Text("Count"), Text("Amount")));
            foreach (ReportStatusTotal total in report.Result.ByStatus)
            {
                data.Append(new Row(Text(total.Status), Number(total.Count), Number(total.Amount)));
            }

            data.Append(new Row(Text("Total"), Number(report.Result.TotalCount), Number(report.Result.TotalAmount)));
            worksheet.Worksheet.Save();
            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static Cell Text(string value) => new Cell
    {
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value)),
    };

    private static Cell Number(decimal value) => new Cell
    {
        DataType = CellValues.Number,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)),
    };
}
