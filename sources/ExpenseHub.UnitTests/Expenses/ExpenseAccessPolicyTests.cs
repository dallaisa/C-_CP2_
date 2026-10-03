namespace ExpenseHub.UnitTests.Expenses;

using System;
using ExpenseHub.Api.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the contextual decisions of the access matrix.
/// </summary>
[TestClass]
public sealed class ExpenseAccessPolicyTests
{
    private const string ActorId = "actor";
    private const string OtherId = "other";

    /// <summary>Only an Employee may change drafts.</summary>
    [TestMethod]
    public void DraftChangeRequiresEmployeeRole()
    {
        ExpenseViewer auditor = new ExpenseViewer { UserId = ActorId, IsAuditor = true };

        ExpenseAccessDecision decision =
            ExpenseAccessPolicy.EvaluateDraftChange(auditor, CreateExpense(ActorId, ExpenseStatus.Draft));

        Assert.AreEqual(ExpenseAccessDecision.Forbidden, decision);
    }

    /// <summary>The owner may change their own draft.</summary>
    [TestMethod]
    public void OwnerCanChangeOwnDraft()
    {
        ExpenseAccessDecision decision =
            ExpenseAccessPolicy.EvaluateDraftChange(Employee(), CreateExpense(ActorId, ExpenseStatus.Draft));

        Assert.AreEqual(ExpenseAccessDecision.Allowed, decision);
    }

    /// <summary>Another Employee's draft is reported as missing.</summary>
    [TestMethod]
    public void EmployeeCannotChangeAnotherEmployeesDraft()
    {
        ExpenseAccessDecision decision =
            ExpenseAccessPolicy.EvaluateDraftChange(Employee(), CreateExpense(OtherId, ExpenseStatus.Draft));

        Assert.AreEqual(ExpenseAccessDecision.NotFound, decision);
    }

    /// <summary>A missing expense is reported as missing.</summary>
    [TestMethod]
    public void DraftChangeOnMissingExpenseIsNotFound()
    {
        Assert.AreEqual(ExpenseAccessDecision.NotFound, ExpenseAccessPolicy.EvaluateDraftChange(Employee(), null));
    }

    /// <summary>The owner's expense outside Draft is a state conflict.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Submitted)]
    [DataRow((int)ExpenseStatus.Approved)]
    [DataRow((int)ExpenseStatus.Rejected)]
    [DataRow((int)ExpenseStatus.Paid)]
    public void OwnerCannotChangeExpenseOutsideDraft(int status)
    {
        ExpenseAccessDecision decision =
            ExpenseAccessPolicy.EvaluateDraftChange(Employee(), CreateExpense(ActorId, (ExpenseStatus)status));

        Assert.AreEqual(ExpenseAccessDecision.Conflict, decision);
    }

    /// <summary>An Approver may decide on another user's submitted expense.</summary>
    [TestMethod]
    public void ApproverCanDecideOnAnotherUsersSubmittedExpense()
    {
        ExpenseAccessDecision decision = ExpenseAccessPolicy.EvaluateApprovalDecision(
            Approver(),
            CreateExpense(OtherId, ExpenseStatus.Submitted));

        Assert.AreEqual(ExpenseAccessDecision.Allowed, decision);
    }

    /// <summary>An Approver never decides on their own expense, even with the Employee role.</summary>
    /// <param name="isEmployee">Whether the Approver also has the Employee role.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ApproverCannotDecideOnOwnExpense(bool isEmployee)
    {
        ExpenseViewer approver = new ExpenseViewer { UserId = ActorId, IsApprover = true, IsEmployee = isEmployee };

        ExpenseAccessDecision decision = ExpenseAccessPolicy.EvaluateApprovalDecision(
            approver,
            CreateExpense(ActorId, ExpenseStatus.Submitted));

        Assert.AreEqual(ExpenseAccessDecision.Forbidden, decision);
    }

    /// <summary>A decision on a missing expense is reported as missing.</summary>
    [TestMethod]
    public void ApprovalDecisionOnMissingExpenseIsNotFound()
    {
        Assert.AreEqual(
            ExpenseAccessDecision.NotFound,
            ExpenseAccessPolicy.EvaluateApprovalDecision(Approver(), null));
    }

    /// <summary>A decision on an expense that is not Submitted is a conflict.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Draft)]
    [DataRow((int)ExpenseStatus.Approved)]
    [DataRow((int)ExpenseStatus.Rejected)]
    [DataRow((int)ExpenseStatus.Paid)]
    public void ApprovalDecisionOutsideSubmittedIsConflict(int status)
    {
        ExpenseAccessDecision decision = ExpenseAccessPolicy.EvaluateApprovalDecision(
            Approver(),
            CreateExpense(OtherId, (ExpenseStatus)status));

        Assert.AreEqual(ExpenseAccessDecision.Conflict, decision);
    }

    /// <summary>Finance may pay another user's approved expense.</summary>
    [TestMethod]
    public void FinanceCanPayAnotherUsersApprovedExpense()
    {
        ExpenseAccessDecision decision =
            ExpenseAccessPolicy.EvaluatePayment(Finance(), CreateExpense(OtherId, ExpenseStatus.Approved));

        Assert.AreEqual(ExpenseAccessDecision.Allowed, decision);
    }

    /// <summary>Finance never pays their own expense, even with the Employee role.</summary>
    /// <param name="isEmployee">Whether the Finance user also has the Employee role.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FinanceCannotPayOwnExpense(bool isEmployee)
    {
        ExpenseViewer finance = new ExpenseViewer { UserId = ActorId, IsFinance = true, IsEmployee = isEmployee };

        ExpenseAccessDecision decision =
            ExpenseAccessPolicy.EvaluatePayment(finance, CreateExpense(ActorId, ExpenseStatus.Approved));

        Assert.AreEqual(ExpenseAccessDecision.Forbidden, decision);
    }

    /// <summary>Paying an already paid expense is a conflict.</summary>
    [TestMethod]
    public void RepeatedPaymentIsConflict()
    {
        ExpenseAccessDecision decision =
            ExpenseAccessPolicy.EvaluatePayment(Finance(), CreateExpense(OtherId, ExpenseStatus.Paid));

        Assert.AreEqual(ExpenseAccessDecision.Conflict, decision);
    }

    /// <summary>Expenses that are not Approved cannot be paid.</summary>
    /// <param name="status">The numeric value of the current status.</param>
    [TestMethod]
    [DataRow((int)ExpenseStatus.Draft)]
    [DataRow((int)ExpenseStatus.Submitted)]
    [DataRow((int)ExpenseStatus.Rejected)]
    public void PaymentOutsideApprovedIsConflict(int status)
    {
        ExpenseAccessDecision decision =
            ExpenseAccessPolicy.EvaluatePayment(Finance(), CreateExpense(OtherId, (ExpenseStatus)status));

        Assert.AreEqual(ExpenseAccessDecision.Conflict, decision);
    }

    /// <summary>A payment on a missing expense is reported as missing.</summary>
    [TestMethod]
    public void PaymentOnMissingExpenseIsNotFound()
    {
        Assert.AreEqual(ExpenseAccessDecision.NotFound, ExpenseAccessPolicy.EvaluatePayment(Finance(), null));
    }

    /// <summary>An Auditor reads everything but performs no write operation.</summary>
    [TestMethod]
    public void AuditorReadsButNeverWrites()
    {
        ExpenseViewer auditor = new ExpenseViewer { UserId = ActorId, IsAuditor = true };

        Assert.IsTrue(ExpenseAccessPolicy.CanRead(auditor, CreateExpense(OtherId, ExpenseStatus.Draft)));
        Assert.AreEqual(
            ExpenseAccessDecision.Forbidden,
            ExpenseAccessPolicy.EvaluateDraftChange(auditor, CreateExpense(ActorId, ExpenseStatus.Draft)));
        Assert.AreEqual(
            ExpenseAccessDecision.Forbidden,
            ExpenseAccessPolicy.EvaluateApprovalDecision(auditor, CreateExpense(OtherId, ExpenseStatus.Submitted)));
        Assert.AreEqual(
            ExpenseAccessDecision.Forbidden,
            ExpenseAccessPolicy.EvaluatePayment(auditor, CreateExpense(OtherId, ExpenseStatus.Approved)));
    }

    /// <summary>A user without functional roles, such as an Admin only, has no access to expenses.</summary>
    [TestMethod]
    public void AdminWithoutFunctionalRoleHasNoAccess()
    {
        ExpenseViewer admin = new ExpenseViewer { UserId = ActorId };

        Assert.IsFalse(ExpenseAccessPolicy.CanRead(admin, CreateExpense(ActorId, ExpenseStatus.Draft)));
        Assert.AreEqual(
            ExpenseAccessDecision.Forbidden,
            ExpenseAccessPolicy.EvaluateDraftChange(admin, CreateExpense(ActorId, ExpenseStatus.Draft)));
        Assert.AreEqual(
            ExpenseAccessDecision.Forbidden,
            ExpenseAccessPolicy.EvaluateApprovalDecision(admin, CreateExpense(OtherId, ExpenseStatus.Submitted)));
        Assert.AreEqual(
            ExpenseAccessDecision.Forbidden,
            ExpenseAccessPolicy.EvaluatePayment(admin, CreateExpense(OtherId, ExpenseStatus.Approved)));
    }

    /// <summary>Changing the identifier in the URL does not expose another Employee's expense.</summary>
    [TestMethod]
    public void EmployeeCannotReadAnotherEmployeesExpense()
    {
        foreach (ExpenseStatus status in Enum.GetValues<ExpenseStatus>())
        {
            Assert.IsFalse(ExpenseAccessPolicy.CanRead(Employee(), CreateExpense(OtherId, status)));
        }
    }

    /// <summary>Approver and Finance together still cannot approve or pay their own expense.</summary>
    [TestMethod]
    public void ApproverAndFinanceTogetherCannotActOnOwnExpense()
    {
        ExpenseViewer actor = new ExpenseViewer { UserId = ActorId, IsApprover = true, IsFinance = true };

        Assert.AreEqual(
            ExpenseAccessDecision.Forbidden,
            ExpenseAccessPolicy.EvaluateApprovalDecision(actor, CreateExpense(ActorId, ExpenseStatus.Submitted)));
        Assert.AreEqual(
            ExpenseAccessDecision.Forbidden,
            ExpenseAccessPolicy.EvaluatePayment(actor, CreateExpense(ActorId, ExpenseStatus.Approved)));
    }

    /// <summary>Seeing an expense through another role does not allow paying it outside Approved.</summary>
    [TestMethod]
    public void ApproverAndFinanceCannotPayAnotherUsersSubmittedExpense()
    {
        ExpenseViewer actor = new ExpenseViewer { UserId = ActorId, IsApprover = true, IsFinance = true };
        Expense submitted = CreateExpense(OtherId, ExpenseStatus.Submitted);

        Assert.IsTrue(ExpenseAccessPolicy.CanRead(actor, submitted));
        Assert.AreEqual(ExpenseAccessDecision.Conflict, ExpenseAccessPolicy.EvaluatePayment(actor, submitted));
        Assert.AreEqual(ExpenseAccessDecision.Allowed, ExpenseAccessPolicy.EvaluateApprovalDecision(actor, submitted));
    }

    /// <summary>A user without an identifier is never treated as the owner of an expense.</summary>
    [TestMethod]
    public void ViewerWithoutIdentifierOwnsNothing()
    {
        ExpenseViewer anonymousEmployee = new ExpenseViewer { UserId = string.Empty, IsEmployee = true };

        Assert.IsFalse(ExpenseAccessPolicy.CanRead(anonymousEmployee, CreateExpense(string.Empty, ExpenseStatus.Draft)));
        Assert.AreEqual(
            ExpenseAccessDecision.NotFound,
            ExpenseAccessPolicy.EvaluateDraftChange(anonymousEmployee, CreateExpense(string.Empty, ExpenseStatus.Draft)));
    }

    private static ExpenseViewer Employee() => new ExpenseViewer { UserId = ActorId, IsEmployee = true };

    private static ExpenseViewer Approver() => new ExpenseViewer { UserId = ActorId, IsApprover = true };

    private static ExpenseViewer Finance() => new ExpenseViewer { UserId = ActorId, IsFinance = true };

    private static Expense CreateExpense(string ownerId, ExpenseStatus status) =>
        new Expense { Id = Guid.NewGuid(), OwnerId = ownerId, Status = status };
}
