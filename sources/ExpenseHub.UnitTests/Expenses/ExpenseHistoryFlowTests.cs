using System;
using System.Collections.Generic;
using System.Linq;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Expenses;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

/// <summary>
/// Tests the history produced by the complete expense workflow.
/// </summary>
[TestClass]
public sealed class ExpenseHistoryFlowTests
{
    private const string EmployeeId = "employee";
    private const string ApproverId = "approver";
    private const string FinanceId = "finance";

    private static readonly DateTimeOffset _start = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Create, edit, submit, approve and pay produce one ordered entry each.</summary>
    [TestMethod]
    public void PaidFlowRecordsEveryStep()
    {
        Expense expense = ExpenseDraftRules.CreateDraft(EmployeeId, CreateValues("Almoço com cliente"), _start);
        ExpenseDraftRules.EditDraft(expense, EmployeeId, CreateValues("Jantar com cliente"), _start.AddHours(1));
        ExpenseDraftRules.Submit(expense, EmployeeId, _start.AddHours(2));
        ExpenseDecisionRules.Approve(expense, ApproverId, _start.AddHours(3));
        ExpensePaymentRules.Pay(expense, FinanceId, _start.AddHours(4));

        List<ExpenseHistory> history = expense.History.ToList();

        CollectionAssert.AreEqual(
            new[]
            {
                ExpenseHistoryActions.Created,
                ExpenseHistoryActions.Updated,
                ExpenseHistoryActions.Submitted,
                ExpenseHistoryActions.Approved,
                ExpenseHistoryActions.Paid,
            },
            history.Select(entry => entry.Action).ToArray());
        CollectionAssert.AreEqual(
            new[] { EmployeeId, EmployeeId, EmployeeId, ApproverId, FinanceId },
            history.Select(entry => entry.ActorId).ToArray());
        CollectionAssert.AreEqual(
            new ExpenseStatus?[] { null, ExpenseStatus.Draft, ExpenseStatus.Draft, ExpenseStatus.Submitted, ExpenseStatus.Approved },
            history.Select(entry => entry.PreviousStatus).ToArray());
        CollectionAssert.AreEqual(
            new ExpenseStatus?[] { ExpenseStatus.Draft, ExpenseStatus.Draft, ExpenseStatus.Submitted, ExpenseStatus.Approved, ExpenseStatus.Paid },
            history.Select(entry => entry.NewStatus).ToArray());
        Assert.IsTrue(history.All(entry => entry.OccurredAtUtc.Offset == TimeSpan.Zero));
        Assert.IsNotNull(history[1].Changes);
    }

    /// <summary>A rejection records its justification and ends the flow.</summary>
    [TestMethod]
    public void RejectedFlowRecordsJustificationAndCannotBePaid()
    {
        Expense expense = ExpenseDraftRules.CreateDraft(EmployeeId, CreateValues("Almoço com cliente"), _start);
        ExpenseDraftRules.Submit(expense, EmployeeId, _start.AddHours(1));
        ExpenseDecisionRules.Reject(expense, ApproverId, "Comprovante ilegível", _start.AddHours(2));

        ExpensePaymentOutcome payment = ExpensePaymentRules.Pay(expense, FinanceId, _start.AddHours(3));

        Assert.AreEqual(ExpensePaymentOutcome.NotApproved, payment);
        Assert.HasCount(3, expense.History);
        ExpenseHistory rejection = expense.History.Last();
        Assert.AreEqual(ExpenseHistoryActions.Rejected, rejection.Action);
        Assert.AreEqual("Comprovante ilegível", rejection.RejectionReason);
    }

    /// <summary>The history response exposes statuses as names and changes as JSON.</summary>
    [TestMethod]
    public void HistoryResponseMapsStatusesAndChanges()
    {
        Expense expense = ExpenseDraftRules.CreateDraft(EmployeeId, CreateValues("Almoço com cliente"), _start);
        ExpenseDraftRules.EditDraft(expense, EmployeeId, CreateValues("Jantar com cliente"), _start.AddHours(1));

        ExpenseHistoryResponse response = ExpenseHistoryResponse.FromHistory(expense.History.Last());

        Assert.AreEqual(ExpenseHistoryActions.Updated, response.Action);
        Assert.AreEqual("Draft", response.PreviousStatus);
        Assert.AreEqual("Draft", response.NewStatus);
        Assert.IsNotNull(response.Changes);
        Assert.AreEqual("Jantar com cliente", (string?)response.Changes["description"]?["to"]);
    }

    /// <summary>Every action records the instant in UTC, even when the server clock has a local offset.</summary>
    [TestMethod]
    public void EveryActionRecordsInstantInUtc()
    {
        DateTimeOffset localClock = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.FromHours(-3));

        Expense expense = ExpenseDraftRules.CreateDraft(EmployeeId, CreateValues("Almoço com cliente"), localClock);
        ExpenseDraftRules.EditDraft(expense, EmployeeId, CreateValues("Jantar com cliente"), localClock);
        ExpenseDraftRules.Submit(expense, EmployeeId, localClock);
        ExpenseDecisionRules.Approve(expense, ApproverId, localClock);
        ExpensePaymentRules.Pay(expense, FinanceId, localClock);

        Assert.HasCount(5, expense.History);
        foreach (ExpenseHistory entry in expense.History)
        {
            Assert.AreEqual(TimeSpan.Zero, entry.OccurredAtUtc.Offset);
            Assert.AreEqual(localClock.UtcDateTime, entry.OccurredAtUtc.UtcDateTime);
        }

        Assert.AreEqual(TimeSpan.Zero, expense.Payment!.PaidAtUtc.Offset);
        Assert.AreEqual(localClock.UtcDateTime, expense.Payment.PaidAtUtc.UtcDateTime);
    }

    private static ExpenseDraftValues CreateValues(string description) =>
        new ExpenseDraftValues
        {
            Description = description,
            Amount = 150.75m,
            ExpenseDate = new DateOnly(2026, 8, 31),
            ExpenseCategoryId = 1,
        };
}
