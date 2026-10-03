using System;
using System.Linq;
using ExpenseHub.Api.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

/// <summary>
/// Tests the Approved to Paid transition and the payment record.
/// </summary>
[TestClass]
public sealed class ExpensePaymentRulesTests
{
    private const string OwnerId = "owner";
    private const string FinanceId = "finance";

    private static readonly DateTimeOffset _paidAt = new DateTimeOffset(2026, 9, 10, 11, 30, 0, TimeSpan.Zero);

    /// <summary>Finance pays another user's approved expense; status, payment record and history are created together.</summary>
    [TestMethod]
    public void PayMovesApprovedToPaidWithRecordAndHistory()
    {
        Expense expense = CreateExpense(ExpenseStatus.Approved);

        ExpensePaymentOutcome outcome = ExpensePaymentRules.Pay(expense, FinanceId, _paidAt);

        Assert.AreEqual(ExpensePaymentOutcome.Paid, outcome);
        Assert.AreEqual(ExpenseStatus.Paid, expense.Status);

        Assert.IsNotNull(expense.Payment);
        Assert.AreEqual(expense.Id, expense.Payment.ExpenseId);
        Assert.AreEqual(FinanceId, expense.Payment.ActorId);
        Assert.AreEqual(_paidAt, expense.Payment.PaidAtUtc);

        ExpenseHistory entry = expense.History.Single();
        Assert.AreEqual(ExpenseHistoryActions.Paid, entry.Action);
        Assert.AreEqual(FinanceId, entry.ActorId);
        Assert.AreEqual(_paidAt, entry.OccurredAtUtc);
        Assert.AreEqual(ExpenseStatus.Approved, entry.PreviousStatus);
        Assert.AreEqual(ExpenseStatus.Paid, entry.NewStatus);
    }

    /// <summary>The owner cannot pay their own expense; nothing changes.</summary>
    [TestMethod]
    public void OwnerCannotPayOwnExpense()
    {
        Expense expense = CreateExpense(ExpenseStatus.Approved);

        ExpensePaymentOutcome outcome = ExpensePaymentRules.Pay(expense, OwnerId, _paidAt);

        Assert.AreEqual(ExpensePaymentOutcome.SelfPayment, outcome);
        Assert.AreEqual(ExpenseStatus.Approved, expense.Status);
        Assert.IsNull(expense.Payment);
        Assert.IsEmpty(expense.History);
    }

    /// <summary>Only approved expenses can be paid; nothing changes otherwise.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Draft)]
    [DataRow((int)ExpenseStatus.Submitted)]
    [DataRow((int)ExpenseStatus.Rejected)]
    [DataRow((int)ExpenseStatus.Paid)]
    public void PayOutsideApprovedIsRejected(int status)
    {
        Expense expense = CreateExpense((ExpenseStatus)status);

        ExpensePaymentOutcome outcome = ExpensePaymentRules.Pay(expense, FinanceId, _paidAt);

        Assert.AreEqual(ExpensePaymentOutcome.NotApproved, outcome);
        Assert.AreEqual((ExpenseStatus)status, expense.Status);
        Assert.IsNull(expense.Payment);
        Assert.IsEmpty(expense.History);
    }

    /// <summary>A repeated payment keeps the first record and does not duplicate the history.</summary>
    [TestMethod]
    public void RepeatedPaymentDoesNotDuplicateRecords()
    {
        Expense expense = CreateExpense(ExpenseStatus.Approved);
        ExpensePaymentRules.Pay(expense, FinanceId, _paidAt);
        PaymentRecord firstPayment = expense.Payment!;

        ExpensePaymentOutcome outcome = ExpensePaymentRules.Pay(expense, "other-finance", _paidAt.AddMinutes(5));

        Assert.AreEqual(ExpensePaymentOutcome.NotApproved, outcome);
        Assert.AreSame(firstPayment, expense.Payment);
        Assert.AreEqual(FinanceId, expense.Payment!.ActorId);
        Assert.HasCount(1, expense.History);
    }

    private static Expense CreateExpense(ExpenseStatus status) =>
        new Expense { Id = Guid.NewGuid(), OwnerId = OwnerId, Status = status };
}
