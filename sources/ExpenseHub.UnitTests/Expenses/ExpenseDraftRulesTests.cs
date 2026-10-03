using System;
using System.Linq;
using ExpenseHub.Api.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

/// <summary>
/// Tests the creation and edition rules of draft expenses.
/// </summary>
[TestClass]
public sealed class ExpenseDraftRulesTests
{
    private const string OwnerId = "owner-1";
    private const string OtherUserId = "owner-2";

    private static readonly DateTimeOffset _createdAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _editedAt = new DateTimeOffset(2026, 9, 2, 8, 30, 0, TimeSpan.Zero);

    /// <summary>A new expense is a draft owned by the authenticated user with a server generated id.</summary>
    [TestMethod]
    public void CreateDraftUsesServerControlledValues()
    {
        Expense expense = ExpenseDraftRules.CreateDraft(OwnerId, CreateValues(), _createdAt);

        Assert.AreNotEqual(Guid.Empty, expense.Id);
        Assert.AreEqual(OwnerId, expense.OwnerId);
        Assert.AreEqual(ExpenseStatus.Draft, expense.Status);
        Assert.AreEqual("Almoço com cliente", expense.Description);
        Assert.AreEqual(150.75m, expense.Amount);
    }

    /// <summary>Each created draft receives a distinct identifier.</summary>
    [TestMethod]
    public void CreateDraftGeneratesDistinctIds()
    {
        Expense first = ExpenseDraftRules.CreateDraft(OwnerId, CreateValues(), _createdAt);
        Expense second = ExpenseDraftRules.CreateDraft(OwnerId, CreateValues(), _createdAt);

        Assert.AreNotEqual(first.Id, second.Id);
    }

    /// <summary>Creation is recorded in the history with actor, UTC time and states.</summary>
    [TestMethod]
    public void CreateDraftRecordsCreationHistory()
    {
        Expense expense = ExpenseDraftRules.CreateDraft(OwnerId, CreateValues(), _createdAt);

        ExpenseHistory entry = expense.History.Single();
        Assert.AreEqual(ExpenseHistoryActions.Created, entry.Action);
        Assert.AreEqual(OwnerId, entry.ActorId);
        Assert.AreEqual(_createdAt, entry.OccurredAtUtc);
        Assert.IsNull(entry.PreviousStatus);
        Assert.AreEqual(ExpenseStatus.Draft, entry.NewStatus);
    }

    /// <summary>The owner can edit a draft, and the changes are recorded.</summary>
    [TestMethod]
    public void EditDraftByOwnerUpdatesFieldsAndRecordsChanges()
    {
        Expense expense = ExpenseDraftRules.CreateDraft(OwnerId, CreateValues(), _createdAt);
        ExpenseDraftValues newValues = CreateValues(description: "Jantar com cliente", amount: 200m, categoryId: 2);

        DraftEditOutcome outcome = ExpenseDraftRules.EditDraft(expense, OwnerId, newValues, _editedAt);

        Assert.AreEqual(DraftEditOutcome.Updated, outcome);
        Assert.AreEqual("Jantar com cliente", expense.Description);
        Assert.AreEqual(200m, expense.Amount);
        Assert.AreEqual(2, expense.ExpenseCategoryId);
        Assert.AreEqual(OwnerId, expense.OwnerId);
        Assert.AreEqual(ExpenseStatus.Draft, expense.Status);

        ExpenseHistory entry = expense.History.Last();
        Assert.AreEqual(ExpenseHistoryActions.Updated, entry.Action);
        Assert.AreEqual(OwnerId, entry.ActorId);
        Assert.AreEqual(_editedAt, entry.OccurredAtUtc);
        Assert.AreEqual(ExpenseStatus.Draft, entry.PreviousStatus);
        Assert.AreEqual(ExpenseStatus.Draft, entry.NewStatus);
        Assert.IsNotNull(entry.Changes);
        StringAssert.Contains(
            entry.Changes,
            "\"description\":{\"from\":\"Almoço com cliente\",\"to\":\"Jantar com cliente\"}");
        StringAssert.Contains(entry.Changes, "\"amount\"");
        StringAssert.Contains(entry.Changes, "\"categoryId\"");
        Assert.DoesNotContain("expenseDate", entry.Changes);
    }

    /// <summary>Another user cannot edit the draft, and nothing changes.</summary>
    [TestMethod]
    public void EditDraftByAnotherUserIsRejected()
    {
        Expense expense = ExpenseDraftRules.CreateDraft(OwnerId, CreateValues(), _createdAt);

        DraftEditOutcome outcome = ExpenseDraftRules.EditDraft(
            expense,
            OtherUserId,
            CreateValues(description: "Descrição alterada indevidamente"),
            _editedAt);

        Assert.AreEqual(DraftEditOutcome.NotOwner, outcome);
        Assert.AreEqual("Almoço com cliente", expense.Description);
        Assert.HasCount(1, expense.History);
    }

    /// <summary>An expense outside Draft cannot be edited, and nothing changes.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Submitted)]
    [DataRow((int)ExpenseStatus.Approved)]
    [DataRow((int)ExpenseStatus.Rejected)]
    [DataRow((int)ExpenseStatus.Paid)]
    public void EditDraftOutsideDraftIsRejected(int status)
    {
        Expense expense = ExpenseDraftRules.CreateDraft(OwnerId, CreateValues(), _createdAt);
        expense.Status = (ExpenseStatus)status;

        DraftEditOutcome outcome = ExpenseDraftRules.EditDraft(
            expense,
            OwnerId,
            CreateValues(description: "Descrição alterada fora do rascunho"),
            _editedAt);

        Assert.AreEqual(DraftEditOutcome.NotDraft, outcome);
        Assert.AreEqual("Almoço com cliente", expense.Description);
        Assert.AreEqual((ExpenseStatus)status, expense.Status);
        Assert.HasCount(1, expense.History);
    }

    /// <summary>Submitting the same values does not create a history entry.</summary>
    [TestMethod]
    public void EditDraftWithSameValuesRecordsNothing()
    {
        Expense expense = ExpenseDraftRules.CreateDraft(OwnerId, CreateValues(), _createdAt);

        DraftEditOutcome outcome = ExpenseDraftRules.EditDraft(expense, OwnerId, CreateValues(), _editedAt);

        Assert.AreEqual(DraftEditOutcome.Unchanged, outcome);
        Assert.HasCount(1, expense.History);
    }

    private static ExpenseDraftValues CreateValues(
        string description = "Almoço com cliente",
        decimal amount = 150.75m,
        int categoryId = 1)
    {
        return new ExpenseDraftValues
        {
            Description = description,
            Amount = amount,
            ExpenseDate = new DateOnly(2026, 8, 31),
            ExpenseCategoryId = categoryId,
        };
    }
}
