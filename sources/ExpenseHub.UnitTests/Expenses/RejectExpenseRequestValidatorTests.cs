namespace ExpenseHub.UnitTests.Expenses;

using System.Collections.Generic;
using ExpenseHub.Api.Expenses;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the rejection justification contract.
/// </summary>
[TestClass]
public sealed class RejectExpenseRequestValidatorTests
{
    /// <summary>Justifications between 10 and 500 characters are accepted.</summary>
    /// <param name="length">The justification length.</param>
    [TestMethod]
    [DataRow(10)]
    [DataRow(500)]
    public void ValidateAcceptsReasonWithinLimits(int length)
    {
        Dictionary<string, string[]> errors =
            RejectExpenseRequestValidator.Validate(new RejectExpenseRequest { Reason = new string('a', length) });

        Assert.IsEmpty(errors);
    }

    /// <summary>Empty, short and long justifications are rejected.</summary>
    /// <param name="length">The justification length.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(9)]
    [DataRow(501)]
    public void ValidateRejectsReasonOutsideLimits(int length)
    {
        Dictionary<string, string[]> errors =
            RejectExpenseRequestValidator.Validate(new RejectExpenseRequest { Reason = new string('a', length) });

        Assert.IsTrue(errors.ContainsKey(nameof(RejectExpenseRequest.Reason)));
    }

    /// <summary>A missing justification is rejected.</summary>
    [TestMethod]
    public void ValidateRejectsMissingReason()
    {
        Dictionary<string, string[]> errors = RejectExpenseRequestValidator.Validate(new RejectExpenseRequest());

        Assert.IsTrue(errors.ContainsKey(nameof(RejectExpenseRequest.Reason)));
    }

    /// <summary>Surrounding spaces do not count towards the minimum length.</summary>
    [TestMethod]
    public void ValidateRejectsReasonPaddedWithSpaces()
    {
        Dictionary<string, string[]> errors =
            RejectExpenseRequestValidator.Validate(new RejectExpenseRequest { Reason = "    curta     " });

        Assert.IsTrue(errors.ContainsKey(nameof(RejectExpenseRequest.Reason)));
    }
}
