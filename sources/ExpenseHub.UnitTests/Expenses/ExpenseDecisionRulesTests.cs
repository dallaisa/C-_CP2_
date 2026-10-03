using System;
using System.Linq;
using ExpenseHub.Api.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

/// <summary>
/// Tests the approval and rejection transitions.
/// </summary>
[TestClass]
public sealed class ExpenseDecisionRulesTests
{
    private const string OwnerId = "owner";
    private const string ApproverId = "approver";
    private const string Reason = "Comprovante ilegível";

    private static readonly DateTimeOffset _decidedAt = new DateTimeOffset(2026, 9, 5, 14, 0, 0, TimeSpan.Zero);

    /// <summary>An Approver approves another user's submitted expense and the transition is recorded.</summary>
    [TestMethod]
    public void ApproveMovesSubmittedToApproved()
    {
        Expense expense = CreateExpense(ExpenseStatus.Submitted);

        ExpenseDecisionOutcome outcome = ExpenseDecisionRules.Approve(expense, ApproverId, _decidedAt);

        Assert.AreEqual(ExpenseDecisionOutcome.Applied, outcome);
        Assert.AreEqual(ExpenseStatus.Approved, expense.Status);

        ExpenseHistory entry = expense.History.Single();
        Assert.AreEqual(ExpenseHistoryActions.Approved, entry.Action);
        Assert.AreEqual(expense.Id, entry.ExpenseId);
        Assert.AreEqual(ApproverId, entry.ActorId);
        Assert.AreEqual(_decidedAt, entry.OccurredAtUtc);
        Assert.AreEqual(ExpenseStatus.Submitted, entry.PreviousStatus);
        Assert.AreEqual(ExpenseStatus.Approved, entry.NewStatus);
        Assert.IsNull(entry.RejectionReason);
    }

    /// <summary>An Approver rejects another user's submitted expense and the justification is recorded.</summary>
    [TestMethod]
    public void RejectMovesSubmittedToRejectedWithReason()
    {
        Expense expense = CreateExpense(ExpenseStatus.Submitted);

        ExpenseDecisionOutcome outcome = ExpenseDecisionRules.Reject(expense, ApproverId, Reason, _decidedAt);

        Assert.AreEqual(ExpenseDecisionOutcome.Applied, outcome);
        Assert.AreEqual(ExpenseStatus.Rejected, expense.Status);

        ExpenseHistory entry = expense.History.Single();
        Assert.AreEqual(ExpenseHistoryActions.Rejected, entry.Action);
        Assert.AreEqual(ApproverId, entry.ActorId);
        Assert.AreEqual(_decidedAt, entry.OccurredAtUtc);
        Assert.AreEqual(ExpenseStatus.Submitted, entry.PreviousStatus);
        Assert.AreEqual(ExpenseStatus.Rejected, entry.NewStatus);
        Assert.AreEqual(Reason, entry.RejectionReason);
    }

    /// <summary>The owner cannot approve or reject their own expense.</summary>
    [TestMethod]
    public void OwnerCannotDecideOnOwnExpense()
    {
        Expense approveTarget = CreateExpense(ExpenseStatus.Submitted);
        Expense rejectTarget = CreateExpense(ExpenseStatus.Submitted);

        Assert.AreEqual(
            ExpenseDecisionOutcome.SelfDecision,
            ExpenseDecisionRules.Approve(approveTarget, OwnerId, _decidedAt));
        Assert.AreEqual(
            ExpenseDecisionOutcome.SelfDecision,
            ExpenseDecisionRules.Reject(rejectTarget, OwnerId, Reason, _decidedAt));
        Assert.AreEqual(ExpenseStatus.Submitted, approveTarget.Status);
        Assert.AreEqual(ExpenseStatus.Submitted, rejectTarget.Status);
        Assert.IsEmpty(approveTarget.History);
        Assert.IsEmpty(rejectTarget.History);
    }

    /// <summary>Only submitted expenses receive a decision; nothing changes otherwise.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Draft)]
    [DataRow((int)ExpenseStatus.Approved)]
    [DataRow((int)ExpenseStatus.Rejected)]
    [DataRow((int)ExpenseStatus.Paid)]
    public void DecisionOutsideSubmittedIsRejected(int status)
    {
        Expense approveTarget = CreateExpense((ExpenseStatus)status);
        Expense rejectTarget = CreateExpense((ExpenseStatus)status);

        Assert.AreEqual(
            ExpenseDecisionOutcome.NotSubmitted,
            ExpenseDecisionRules.Approve(approveTarget, ApproverId, _decidedAt));
        Assert.AreEqual(
            ExpenseDecisionOutcome.NotSubmitted,
            ExpenseDecisionRules.Reject(rejectTarget, ApproverId, Reason, _decidedAt));
        Assert.AreEqual((ExpenseStatus)status, approveTarget.Status);
        Assert.AreEqual((ExpenseStatus)status, rejectTarget.Status);
        Assert.IsEmpty(approveTarget.History);
        Assert.IsEmpty(rejectTarget.History);
    }

    /// <summary>Repeating an approval does not create a second history entry.</summary>
    [TestMethod]
    public void RepeatedApprovalDoesNotDuplicateHistory()
    {
        Expense expense = CreateExpense(ExpenseStatus.Submitted);
        ExpenseDecisionRules.Approve(expense, ApproverId, _decidedAt);

        ExpenseDecisionOutcome outcome = ExpenseDecisionRules.Approve(expense, ApproverId, _decidedAt.AddMinutes(1));

        Assert.AreEqual(ExpenseDecisionOutcome.NotSubmitted, outcome);
        Assert.HasCount(1, expense.History);
    }

    /// <summary>Rejected is final: it can be neither rejected again nor approved.</summary>
    [TestMethod]
    public void RejectedIsFinal()
    {
        Expense expense = CreateExpense(ExpenseStatus.Submitted);
        ExpenseDecisionRules.Reject(expense, ApproverId, Reason, _decidedAt);

        Assert.AreEqual(
            ExpenseDecisionOutcome.NotSubmitted,
            ExpenseDecisionRules.Reject(expense, ApproverId, Reason, _decidedAt.AddMinutes(1)));
        Assert.AreEqual(
            ExpenseDecisionOutcome.NotSubmitted,
            ExpenseDecisionRules.Approve(expense, ApproverId, _decidedAt.AddMinutes(2)));
        Assert.AreEqual(ExpenseStatus.Rejected, expense.Status);
        Assert.HasCount(1, expense.History);
    }

    private static Expense CreateExpense(ExpenseStatus status) =>
        new Expense { Id = Guid.NewGuid(), OwnerId = OwnerId, Status = status };
}
