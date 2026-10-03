using System;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Expenses;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

/// <summary>
/// Tests the expense data exposed by the API contract.
/// </summary>
[TestClass]
public sealed class ExpenseResponseTests
{
    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 9, 20, 15, 0, 0, TimeSpan.Zero);

    /// <summary>An unpaid expense exposes its status by name and no payment data.</summary>
    [TestMethod]
    public void UnpaidExpenseExposesStatusNameWithoutPayment()
    {
        Expense expense = new Expense { Id = Guid.NewGuid(), OwnerId = "owner", Status = ExpenseStatus.Approved };

        ExpenseResponse response = ExpenseResponse.FromExpense(expense);

        Assert.AreEqual("Approved", response.Status);
        Assert.IsNull(response.PaidByUserId);
        Assert.IsNull(response.PaidAtUtc);
    }

    /// <summary>A paid expense exposes who paid it and when, as recorded by the server.</summary>
    [TestMethod]
    public void PaidExpenseExposesPaymentActorAndTime()
    {
        Expense expense = new Expense { Id = Guid.NewGuid(), OwnerId = "owner", Status = ExpenseStatus.Approved };
        ExpensePaymentRules.Pay(expense, "finance", _now);

        ExpenseResponse response = ExpenseResponse.FromExpense(expense);

        Assert.AreEqual("Paid", response.Status);
        Assert.AreEqual("finance", response.PaidByUserId);
        Assert.AreEqual(_now, response.PaidAtUtc);
        Assert.AreEqual("owner", response.OwnerId);
    }
}
