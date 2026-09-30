namespace ExpenseHub.UnitTests.Expenses;

using System;
using System.Collections.Generic;
using System.Globalization;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Expenses;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the draft expense input contract.
/// </summary>
[TestClass]
public sealed class ExpenseRequestValidatorTests
{
    private static readonly DateOnly Today = new DateOnly(2026, 9, 30);

    /// <summary>A request that respects every field rule is accepted.</summary>
    [TestMethod]
    public void ValidateAcceptsValidRequest()
    {
        Dictionary<string, string[]> errors = ExpenseRequestValidator.Validate(CreateRequest(), Today);

        Assert.IsEmpty(errors);
    }

    /// <summary>Descriptions outside the 10 to 500 character range are rejected.</summary>
    /// <param name="length">The description length.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(9)]
    [DataRow(501)]
    public void ValidateRejectsDescriptionOutsideLimits(int length)
    {
        ExpenseRequest request = CreateRequest(description: new string('a', length));

        Dictionary<string, string[]> errors = ExpenseRequestValidator.Validate(request, Today);

        Assert.IsTrue(errors.ContainsKey(nameof(ExpenseRequest.Description)));
    }

    /// <summary>Descriptions at the limits are accepted.</summary>
    /// <param name="length">The description length.</param>
    [TestMethod]
    [DataRow(10)]
    [DataRow(500)]
    public void ValidateAcceptsDescriptionAtLimits(int length)
    {
        ExpenseRequest request = CreateRequest(description: new string('a', length));

        Dictionary<string, string[]> errors = ExpenseRequestValidator.Validate(request, Today);

        Assert.IsEmpty(errors);
    }

    /// <summary>Surrounding spaces do not count towards the minimum description length.</summary>
    [TestMethod]
    public void ValidateRejectsDescriptionPaddedWithSpaces()
    {
        ExpenseRequest request = CreateRequest(description: "   curta      ");

        Dictionary<string, string[]> errors = ExpenseRequestValidator.Validate(request, Today);

        Assert.IsTrue(errors.ContainsKey(nameof(ExpenseRequest.Description)));
    }

    /// <summary>A missing description is rejected.</summary>
    [TestMethod]
    public void ValidateRejectsMissingDescription()
    {
        ExpenseRequest request = new ExpenseRequest
        {
            Amount = 10m,
            ExpenseDate = Today,
            CategoryId = 1,
        };

        Dictionary<string, string[]> errors = ExpenseRequestValidator.Validate(request, Today);

        Assert.IsTrue(errors.ContainsKey(nameof(ExpenseRequest.Description)));
    }

    /// <summary>Amounts outside R$ 0,01 and Int32.MaxValue, or with fractions of cents, are rejected.</summary>
    /// <param name="amount">The amount in invariant culture.</param>
    [TestMethod]
    [DataRow("0")]
    [DataRow("0.009")]
    [DataRow("-1")]
    [DataRow("2147483647.01")]
    [DataRow("10.555")]
    public void ValidateRejectsInvalidAmount(string amount)
    {
        ExpenseRequest request = CreateRequest(amount: decimal.Parse(amount, CultureInfo.InvariantCulture));

        Dictionary<string, string[]> errors = ExpenseRequestValidator.Validate(request, Today);

        Assert.IsTrue(errors.ContainsKey(nameof(ExpenseRequest.Amount)));
    }

    /// <summary>Amounts at the limits are accepted.</summary>
    /// <param name="amount">The amount in invariant culture.</param>
    [TestMethod]
    [DataRow("0.01")]
    [DataRow("2147483647")]
    public void ValidateAcceptsAmountAtLimits(string amount)
    {
        ExpenseRequest request = CreateRequest(amount: decimal.Parse(amount, CultureInfo.InvariantCulture));

        Dictionary<string, string[]> errors = ExpenseRequestValidator.Validate(request, Today);

        Assert.IsEmpty(errors);
    }

    /// <summary>A future expense date is rejected.</summary>
    [TestMethod]
    public void ValidateRejectsFutureDate()
    {
        ExpenseRequest request = CreateRequest(expenseDate: Today.AddDays(1));

        Dictionary<string, string[]> errors = ExpenseRequestValidator.Validate(request, Today);

        Assert.IsTrue(errors.ContainsKey(nameof(ExpenseRequest.ExpenseDate)));
    }

    /// <summary>An expense dated today is accepted.</summary>
    [TestMethod]
    public void ValidateAcceptsTodayAsExpenseDate()
    {
        Dictionary<string, string[]> errors =
            ExpenseRequestValidator.Validate(CreateRequest(expenseDate: Today), Today);

        Assert.IsEmpty(errors);
    }

    /// <summary>A missing or non-positive category is rejected.</summary>
    /// <param name="categoryId">The category identifier; zero means missing.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void ValidateRejectsInvalidCategory(int categoryId)
    {
        ExpenseRequest request = CreateRequest(categoryId: categoryId == 0 ? null : categoryId);

        Dictionary<string, string[]> errors = ExpenseRequestValidator.Validate(request, Today);

        Assert.IsTrue(errors.ContainsKey(nameof(ExpenseRequest.CategoryId)));
    }

    /// <summary>Conversion trims the description and keeps the other values.</summary>
    [TestMethod]
    public void ToDraftValuesTrimsDescription()
    {
        ExpenseRequest request = CreateRequest(description: "  Almoço com cliente  ");

        ExpenseDraftValues values = ExpenseRequestValidator.ToDraftValues(request);

        Assert.AreEqual("Almoço com cliente", values.Description);
        Assert.AreEqual(150.75m, values.Amount);
        Assert.AreEqual(Today, values.ExpenseDate);
        Assert.AreEqual(1, values.ExpenseCategoryId);
    }

    private static ExpenseRequest CreateRequest(
        string description = "Almoço com cliente",
        decimal amount = 150.75m,
        DateOnly? expenseDate = null,
        int? categoryId = 1)
    {
        return new ExpenseRequest
        {
            Description = description,
            Amount = amount,
            ExpenseDate = expenseDate ?? Today,
            CategoryId = categoryId,
        };
    }
}
