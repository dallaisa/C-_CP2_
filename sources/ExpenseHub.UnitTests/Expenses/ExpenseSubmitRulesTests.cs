namespace ExpenseHub.UnitTests.Expenses;

using System;
using System.Linq;
using ExpenseHub.Api.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the Draft to Submitted transition.
/// </summary>
[TestClass]
public sealed class ExpenseSubmitRulesTests
{
    private const string OwnerId = "owner-1";
    private const string OtherUserId = "owner-2";

    private static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SubmittedAt = new DateTimeOffset(2026, 9, 3, 9, 15, 0, TimeSpan.Zero);

    /// <summary>The owner submits a draft and the transition is recorded.</summary>
    [TestMethod]
    public void SubmitByOwnerMovesDraftToSubmitted()
    {
        Expense expense = CreateDraft();

        DraftSubmitOutcome outcome = ExpenseDraftRules.Submit(expense, OwnerId, SubmittedAt);

        Assert.AreEqual(DraftSubmitOutcome.Submitted, outcome);
        Assert.AreEqual(ExpenseStatus.Submitted, expense.Status);

        ExpenseHistory entry = expense.History.Last();
        Assert.AreEqual(ExpenseHistoryActions.Submitted, entry.Action);
        Assert.AreEqual(expense.Id, entry.ExpenseId);
        Assert.AreEqual(OwnerId, entry.ActorId);
        Assert.AreEqual(SubmittedAt, entry.OccurredAtUtc);
        Assert.AreEqual(ExpenseStatus.Draft, entry.PreviousStatus);
        Assert.AreEqual(ExpenseStatus.Submitted, entry.NewStatus);
    }

    /// <summary>A repeated submission is rejected and does not duplicate the history.</summary>
    [TestMethod]
    public void RepeatedSubmitIsRejectedWithoutDuplicatingHistory()
    {
        Expense expense = CreateDraft();
        ExpenseDraftRules.Submit(expense, OwnerId, SubmittedAt);

        DraftSubmitOutcome outcome = ExpenseDraftRules.Submit(expense, OwnerId, SubmittedAt.AddMinutes(1));

        Assert.AreEqual(DraftSubmitOutcome.NotDraft, outcome);
        Assert.AreEqual(ExpenseStatus.Submitted, expense.Status);
        Assert.AreEqual(1, expense.History.Count(entry => entry.Action == ExpenseHistoryActions.Submitted));
    }

    /// <summary>Another user cannot submit the draft.</summary>
    [TestMethod]
    public void SubmitByAnotherUserIsRejected()
    {
        Expense expense = CreateDraft();

        DraftSubmitOutcome outcome = ExpenseDraftRules.Submit(expense, OtherUserId, SubmittedAt);

        Assert.AreEqual(DraftSubmitOutcome.NotOwner, outcome);
        Assert.AreEqual(ExpenseStatus.Draft, expense.Status);
        Assert.HasCount(1, expense.History);
    }

    /// <summary>Only drafts can be submitted.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Submitted)]
    [DataRow((int)ExpenseStatus.Approved)]
    [DataRow((int)ExpenseStatus.Rejected)]
    [DataRow((int)ExpenseStatus.Paid)]
    public void SubmitOutsideDraftIsRejected(int status)
    {
        Expense expense = CreateDraft();
        expense.Status = (ExpenseStatus)status;

        DraftSubmitOutcome outcome = ExpenseDraftRules.Submit(expense, OwnerId, SubmittedAt);

        Assert.AreEqual(DraftSubmitOutcome.NotDraft, outcome);
        Assert.AreEqual((ExpenseStatus)status, expense.Status);
        Assert.HasCount(1, expense.History);
    }

    /// <summary>A submitted expense can no longer be edited.</summary>
    [TestMethod]
    public void SubmittedExpenseCannotBeEdited()
    {
        Expense expense = CreateDraft();
        ExpenseDraftRules.Submit(expense, OwnerId, SubmittedAt);

        DraftEditOutcome outcome = ExpenseDraftRules.EditDraft(
            expense,
            OwnerId,
            new ExpenseDraftValues
            {
                Description = "Descrição alterada após o envio",
                Amount = 1m,
                ExpenseDate = new DateOnly(2026, 8, 31),
                ExpenseCategoryId = 1,
            },
            SubmittedAt.AddMinutes(1));

        Assert.AreEqual(DraftEditOutcome.NotDraft, outcome);
    }

    private static Expense CreateDraft()
    {
        return ExpenseDraftRules.CreateDraft(
            OwnerId,
            new ExpenseDraftValues
            {
                Description = "Almoço com cliente",
                Amount = 150.75m,
                ExpenseDate = new DateOnly(2026, 8, 31),
                ExpenseCategoryId = 1,
            },
            CreatedAt);
    }
}
