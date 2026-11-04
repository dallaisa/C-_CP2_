using System;
using System.Linq;
using System.Text.Json;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>Verifies the official example, decimal totals, scope and inclusive dates.</summary>
[TestClass]
public sealed class ReportCalculatorTests
{
    private static readonly DateOnly _from = new DateOnly(2026, 9, 1);
    private static readonly DateOnly _to = new DateOnly(2026, 9, 30);

    /// <summary>The official example produces 175 for Mine and 375 for Global.</summary>
    [TestMethod]
    [DataRow("Mine", 3, 175)]
    [DataRow("Global", 4, 375)]
    public void OfficialExampleAndInclusiveBoundaries(string scope, int count, int amount)
    {
        Expense[] expenses =
        [
            Expense("owner", ExpenseStatus.Approved, 100m, _from),
            Expense("owner", ExpenseStatus.Approved, 50m, _to),
            Expense("owner", ExpenseStatus.Paid, 25m, _from.AddDays(10)),
            Expense("other", ExpenseStatus.Approved, 200m, _from),
            Expense("owner", ExpenseStatus.Paid, 500m, _to.AddDays(1)),
            Expense("owner", ExpenseStatus.Draft, 700m, _from.AddDays(-1)),
        ];
        ReportResult result = new ReportCalculator(new ReportTestClock())
            .Calculate(expenses, _from, _to, Enum.Parse<ReportScope>(scope), "owner");

        Assert.AreEqual(count, result.TotalCount);
        Assert.AreEqual((decimal)amount, result.TotalAmount);
        Assert.HasCount(5, result.ByStatus);
        Assert.AreEqual(scope == "Mine" ? 150m : 350m, result.ByStatus.Single(row => row.Status == "Approved").Amount);
        Assert.AreEqual(scope == "Mine" ? 2 : 3, result.ByStatus.Single(row => row.Status == "Approved").Count);
        Assert.AreEqual(25m, result.ByStatus.Single(row => row.Status == "Paid").Amount);
        Assert.AreEqual(ReportTestClock.Now, result.GeneratedAt);
        Assert.AreEqual(TimeSpan.Zero, result.GeneratedAt.Offset);
        Assert.AreEqual(ExpenseStatus.Approved, expenses[0].Status);
    }

    /// <summary>Every state remains present with exact decimal arithmetic on a single day.</summary>
    [TestMethod]
    public void EveryStateHasCorrectCountAndDecimalAmount()
    {
        Expense[] expenses = Enum.GetValues<ExpenseStatus>()
            .SelectMany(status => new[] { Expense("owner", status, 0.1m, _from), Expense("owner", status, 0.2m, _from) })
            .ToArray();
        ReportResult result = new ReportCalculator(new ReportTestClock())
            .Calculate(expenses, _from, _from, ReportScope.Mine, "owner");

        Assert.AreEqual(10, result.TotalCount);
        Assert.AreEqual(1.5m, result.TotalAmount);
        CollectionAssert.AreEquivalent(Enum.GetNames<ExpenseStatus>(), result.ByStatus.Select(row => row.Status).ToArray());
        foreach (ReportStatusTotal row in result.ByStatus)
        {
            Assert.AreEqual(2, row.Count);
            Assert.AreEqual(0.3m, row.Amount);
        }
    }

    /// <summary>Empty populations include all five zero rows for both scopes.</summary>
    [TestMethod]
    [DataRow("Mine")]
    [DataRow("Global")]
    public void EmptySetHasFiveZeroRows(string scope)
    {
        ReportResult result = new ReportCalculator(new ReportTestClock())
            .Calculate([], _from, _to, Enum.Parse<ReportScope>(scope), "owner");
        Assert.AreEqual(0, result.TotalCount);
        Assert.AreEqual(0m, result.TotalAmount);
        Assert.HasCount(5, result.ByStatus);
        Assert.IsTrue(result.ByStatus.All(row => row.Count == 0 && row.Amount == 0m));
    }

    /// <summary>Invalid periods and unknown scopes cannot be calculated.</summary>
    [TestMethod]
    public void InvalidPeriodOrScopeThrows()
    {
        ReportCalculator calculator = new ReportCalculator(new ReportTestClock());
        Assert.ThrowsExactly<ArgumentException>(() => calculator.Calculate([], _to, _from, ReportScope.Mine, "owner"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => calculator.Calculate([], _from, _to, (ReportScope)99, "owner"));
    }

    /// <summary>Missing dates, inverted periods and noncanonical scopes are invalid.</summary>
    [TestMethod]
    [DataRow(null, "2026-09-30", "Mine")]
    [DataRow("2026-09-01", null, "Mine")]
    [DataRow("2026-09-30", "2026-09-01", "Mine")]
    [DataRow("2026-09-01", "2026-09-30", null)]
    [DataRow("2026-09-01", "2026-09-30", "mine")]
    [DataRow("2026-09-01", "2026-09-30", "0")]
    [DataRow("2026-09-01", "2026-09-30", "Other")]
    public void InvalidRequest(string? from, string? to, string? scope)
    {
        ReportRequest request = new ReportRequest(Parse(from), Parse(to), scope);
        Assert.IsFalse(request.IsValid);
    }

    /// <summary>Untrusted identity and extra fields are rejected at JSON binding.</summary>
    [TestMethod]
    [DataRow("ownerId")]
    [DataRow("requestedByUserId")]
    [DataRow("status")]
    public void RejectsExtraClientFields(string field)
    {
        string json = "{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\",\"scope\":\"Mine\",\"" + field + "\":\"spoofed\"}";
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ReportRequest>(json, JsonSerializerOptions.Web));
    }

    /// <summary>Malformed calendar dates cannot bind to the HTTP contract.</summary>
    [TestMethod]
    public void InvalidCalendarDateIsRejected()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ReportRequest>(
            "{\"fromDate\":\"2026-02-30\",\"toDate\":\"2026-09-30\",\"scope\":\"Mine\"}", JsonSerializerOptions.Web));
    }

    private static DateOnly? Parse(string? value) => value is null ? null
        : DateOnly.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static Expense Expense(string owner, ExpenseStatus status, decimal amount, DateOnly date)
        => new Expense { OwnerId = owner, Status = status, Amount = amount, ExpenseDate = date };
}
