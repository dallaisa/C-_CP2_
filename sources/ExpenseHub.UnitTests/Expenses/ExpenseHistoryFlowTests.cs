namespace ExpenseHub.UnitTests.Expenses;

using System;
using System.Collections.Generic;
using System.Linq;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Expenses;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the history produced by the complete expense workflow.
/// </summary>
[TestClass]
public sealed class ExpenseHistoryFlowTests
{
    private const string EmployeeId = "employee";
    private const string ApproverId = "approver";
    private const string FinanceId = "finance";

    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Create, edit, submit, approve and pay produce one ordered entry each.</summary>
    [TestMethod]
    public void PaidFlowRecordsEveryStep()
    {
        Expense expense = ExpenseDraftRules.CreateDraft(EmployeeId, CreateValues("Almoço com cliente"), Start);
        ExpenseDraftRules.EditDraft(expense, EmployeeId, CreateValues("Jantar com cliente"), Start.AddHours(1));
        ExpenseDraftRules.Submit(expense, EmployeeId, Start.AddHours(2));
        ExpenseDecisionRules.Approve(expense, ApproverId, Start.AddHours(3));
        ExpensePaymentRules.Pay(expense, FinanceId, Start.AddHours(4));

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
        Expense expense = ExpenseDraftRules.CreateDraft(EmployeeId, CreateValues("Almoço com cliente"), Start);
        ExpenseDraftRules.Submit(expense, EmployeeId, Start.AddHours(1));
        ExpenseDecisionRules.Reject(expense, ApproverId, "Comprovante ilegível", Start.AddHours(2));

        ExpensePaymentOutcome payment = ExpensePaymentRules.Pay(expense, FinanceId, Start.AddHours(3));

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
        Expense expense = ExpenseDraftRules.CreateDraft(EmployeeId, CreateValues("Almoço com cliente"), Start);
        ExpenseDraftRules.EditDraft(expense, EmployeeId, CreateValues("Jantar com cliente"), Start.AddHours(1));

        ExpenseHistoryResponse response = ExpenseHistoryResponse.FromHistory(expense.History.Last());

        Assert.AreEqual(ExpenseHistoryActions.Updated, response.Action);
        Assert.AreEqual("Draft", response.PreviousStatus);
        Assert.AreEqual("Draft", response.NewStatus);
        Assert.IsNotNull(response.Changes);
        Assert.AreEqual("Jantar com cliente", (string?)response.Changes["description"]?["to"]);
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
