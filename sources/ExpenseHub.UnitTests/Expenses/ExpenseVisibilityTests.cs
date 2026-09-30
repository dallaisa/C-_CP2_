namespace ExpenseHub.UnitTests.Expenses;

using System;
using System.Collections.Generic;
using System.Linq;
using ExpenseHub.Api.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the expense visibility matrix for each profile.
/// </summary>
[TestClass]
public sealed class ExpenseVisibilityTests
{
    private const string ViewerId = "viewer";
    private const string OtherId = "other";

    /// <summary>An Employee sees only their own expenses, in every state.</summary>
    [TestMethod]
    public void EmployeeSeesOnlyOwnExpenses()
    {
        List<Expense> visible = Filter(new ExpenseViewer { UserId = ViewerId, IsEmployee = true });

        Assert.HasCount(5, visible);
        Assert.IsTrue(visible.All(expense => expense.OwnerId == ViewerId));
    }

    /// <summary>An Approver sees only submitted expenses, from any owner.</summary>
    [TestMethod]
    public void ApproverSeesOnlySubmittedExpenses()
    {
        List<Expense> visible = Filter(new ExpenseViewer { UserId = ViewerId, IsApprover = true });

        Assert.HasCount(2, visible);
        Assert.IsTrue(visible.All(expense => expense.Status == ExpenseStatus.Submitted));
    }

    /// <summary>Finance sees only approved and paid expenses.</summary>
    [TestMethod]
    public void FinanceSeesOnlyApprovedAndPaidExpenses()
    {
        List<Expense> visible = Filter(new ExpenseViewer { UserId = ViewerId, IsFinance = true });

        Assert.HasCount(4, visible);
        Assert.IsTrue(visible.All(
            expense => expense.Status is ExpenseStatus.Approved or ExpenseStatus.Paid));
    }

    /// <summary>An Auditor sees every expense.</summary>
    [TestMethod]
    public void AuditorSeesAllExpenses()
    {
        List<Expense> visible = Filter(new ExpenseViewer { UserId = ViewerId, IsAuditor = true });

        Assert.HasCount(CreateExpenses().Count, visible);
    }

    /// <summary>A user without functional roles, such as an Admin only, sees nothing.</summary>
    [TestMethod]
    public void ViewerWithoutFunctionalRoleSeesNothing()
    {
        List<Expense> visible = Filter(new ExpenseViewer { UserId = ViewerId });

        Assert.IsEmpty(visible);
    }

    /// <summary>Accumulated roles receive the union of their permissions.</summary>
    [TestMethod]
    public void EmployeeAndApproverSeeUnionOfPermissions()
    {
        List<Expense> visible = Filter(
            new ExpenseViewer { UserId = ViewerId, IsEmployee = true, IsApprover = true });

        // Five own expenses plus the other owner's submitted expense.
        Assert.HasCount(6, visible);
        Assert.IsTrue(visible.All(
            expense => expense.OwnerId == ViewerId || expense.Status == ExpenseStatus.Submitted));
    }

    /// <summary>An Employee cannot see another Employee's expense in any state.</summary>
    [TestMethod]
    public void EmployeeDoesNotSeeOtherOwnersExpenses()
    {
        List<Expense> visible = Filter(new ExpenseViewer { UserId = ViewerId, IsEmployee = true });

        Assert.IsFalse(visible.Any(expense => expense.OwnerId == OtherId));
    }

    private static List<Expense> Filter(ExpenseViewer viewer)
    {
        return CreateExpenses()
            .AsQueryable()
            .Where(ExpenseVisibility.VisibleTo(viewer))
            .ToList();
    }

    private static List<Expense> CreateExpenses()
    {
        List<Expense> expenses = new List<Expense>();
        foreach (string ownerId in new[] { ViewerId, OtherId })
        {
            foreach (ExpenseStatus status in Enum.GetValues<ExpenseStatus>())
            {
                expenses.Add(new Expense { Id = Guid.NewGuid(), OwnerId = ownerId, Status = status });
            }
        }

        return expenses;
    }
}
