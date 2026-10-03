namespace ExpenseHub.UnitTests.Expenses;

using System;
using System.Linq;
using ExpenseHub.Api.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Checks every action against every state, so that only the transitions in the contract are accepted.
/// </summary>
/// <remarks>
/// Contract: Draft → Draft (edit), Draft → Submitted, Submitted → Approved, Submitted → Rejected,
/// Approved → Paid. Rejected and Paid are final. A refused action must leave the state, the history
/// and the payment untouched.
/// </remarks>
[TestClass]
public sealed class ExpenseStateMachineTests
{
    private const string OwnerId = "owner";
    private const string ApproverId = "approver";
    private const string FinanceId = "finance";

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Only a draft can be edited, and editing keeps it as a draft.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    /// <param name="allowed">Whether the contract allows the action.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Draft, true)]
    [DataRow((int)ExpenseStatus.Submitted, false)]
    [DataRow((int)ExpenseStatus.Approved, false)]
    [DataRow((int)ExpenseStatus.Rejected, false)]
    [DataRow((int)ExpenseStatus.Paid, false)]
    public void EditIsAllowedOnlyInDraft(int status, bool allowed)
    {
        Expense expense = CreateExpense((ExpenseStatus)status);

        DraftEditOutcome outcome = ExpenseDraftRules.EditDraft(expense, OwnerId, ChangedValues(), Now);

        Assert.AreEqual(allowed ? DraftEditOutcome.Updated : DraftEditOutcome.NotDraft, outcome);
        AssertTransition(expense, (ExpenseStatus)status, allowed ? ExpenseStatus.Draft : (ExpenseStatus)status, allowed);
    }

    /// <summary>Only a draft can be submitted.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    /// <param name="allowed">Whether the contract allows the action.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Draft, true)]
    [DataRow((int)ExpenseStatus.Submitted, false)]
    [DataRow((int)ExpenseStatus.Approved, false)]
    [DataRow((int)ExpenseStatus.Rejected, false)]
    [DataRow((int)ExpenseStatus.Paid, false)]
    public void SubmitIsAllowedOnlyFromDraft(int status, bool allowed)
    {
        Expense expense = CreateExpense((ExpenseStatus)status);

        DraftSubmitOutcome outcome = ExpenseDraftRules.Submit(expense, OwnerId, Now);

        Assert.AreEqual(allowed ? DraftSubmitOutcome.Submitted : DraftSubmitOutcome.NotDraft, outcome);
        AssertTransition(expense, (ExpenseStatus)status, ExpenseStatus.Submitted, allowed);
    }

    /// <summary>Only a submitted expense can be approved.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    /// <param name="allowed">Whether the contract allows the action.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Draft, false)]
    [DataRow((int)ExpenseStatus.Submitted, true)]
    [DataRow((int)ExpenseStatus.Approved, false)]
    [DataRow((int)ExpenseStatus.Rejected, false)]
    [DataRow((int)ExpenseStatus.Paid, false)]
    public void ApproveIsAllowedOnlyFromSubmitted(int status, bool allowed)
    {
        Expense expense = CreateExpense((ExpenseStatus)status);

        ExpenseDecisionOutcome outcome = ExpenseDecisionRules.Approve(expense, ApproverId, Now);

        Assert.AreEqual(allowed ? ExpenseDecisionOutcome.Applied : ExpenseDecisionOutcome.NotSubmitted, outcome);
        AssertTransition(expense, (ExpenseStatus)status, ExpenseStatus.Approved, allowed);
    }

    /// <summary>Only a submitted expense can be rejected.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    /// <param name="allowed">Whether the contract allows the action.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Draft, false)]
    [DataRow((int)ExpenseStatus.Submitted, true)]
    [DataRow((int)ExpenseStatus.Approved, false)]
    [DataRow((int)ExpenseStatus.Rejected, false)]
    [DataRow((int)ExpenseStatus.Paid, false)]
    public void RejectIsAllowedOnlyFromSubmitted(int status, bool allowed)
    {
        Expense expense = CreateExpense((ExpenseStatus)status);

        ExpenseDecisionOutcome outcome =
            ExpenseDecisionRules.Reject(expense, ApproverId, "Comprovante ilegível", Now);

        Assert.AreEqual(allowed ? ExpenseDecisionOutcome.Applied : ExpenseDecisionOutcome.NotSubmitted, outcome);
        AssertTransition(expense, (ExpenseStatus)status, ExpenseStatus.Rejected, allowed);
    }

    /// <summary>Only an approved expense can be paid, and only a payment creates a payment record.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    /// <param name="allowed">Whether the contract allows the action.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Draft, false)]
    [DataRow((int)ExpenseStatus.Submitted, false)]
    [DataRow((int)ExpenseStatus.Approved, true)]
    [DataRow((int)ExpenseStatus.Rejected, false)]
    [DataRow((int)ExpenseStatus.Paid, false)]
    public void PayIsAllowedOnlyFromApproved(int status, bool allowed)
    {
        Expense expense = CreateExpense((ExpenseStatus)status);

        ExpensePaymentOutcome outcome = ExpensePaymentRules.Pay(expense, FinanceId, Now);

        Assert.AreEqual(allowed ? ExpensePaymentOutcome.Paid : ExpensePaymentOutcome.NotApproved, outcome);
        AssertTransition(expense, (ExpenseStatus)status, ExpenseStatus.Paid, allowed);
        Assert.AreEqual(allowed, expense.Payment is not null);
    }

    private static void AssertTransition(
        Expense expense,
        ExpenseStatus initial,
        ExpenseStatus expectedWhenAllowed,
        bool allowed)
    {
        if (allowed)
        {
            Assert.AreEqual(expectedWhenAllowed, expense.Status);
            Assert.HasCount(1, expense.History);
            Assert.AreEqual(initial, expense.History.Single().PreviousStatus);
            Assert.AreEqual(expectedWhenAllowed, expense.History.Single().NewStatus);
        }
        else
        {
            Assert.AreEqual(initial, expense.Status);
            Assert.IsEmpty(expense.History);
        }
    }

    private static Expense CreateExpense(ExpenseStatus status) =>
        new Expense
        {
            Id = Guid.NewGuid(),
            OwnerId = OwnerId,
            Status = status,
            Description = "Almoço com cliente",
            Amount = 150.75m,
            ExpenseDate = new DateOnly(2026, 9, 1),
            ExpenseCategoryId = 1,
        };

    private static ExpenseDraftValues ChangedValues() =>
        new ExpenseDraftValues
        {
            Description = "Jantar com cliente",
            Amount = 200m,
            ExpenseDate = new DateOnly(2026, 9, 1),
            ExpenseCategoryId = 1,
        };
}
