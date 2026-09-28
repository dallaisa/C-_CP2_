namespace ExpenseHub.Api.Persistence;

using ExpenseHub.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// Provides Entity Framework access to ExpenseHub data.
/// </summary>
internal sealed class ExpenseHubDbContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="ExpenseHubDbContext"/> class.</summary>
    /// <param name="options">The options for this context.</param>
    public ExpenseHubDbContext(DbContextOptions<ExpenseHubDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the expenses in the database.</summary>
    public DbSet<Expense> Expenses => this.Set<Expense>();

    /// <summary>Gets the expense categories in the database.</summary>
    public DbSet<ExpenseCategory> ExpenseCategories => this.Set<ExpenseCategory>();

    /// <summary>Gets the expense history entries in the database.</summary>
    public DbSet<ExpenseHistory> ExpenseHistories => this.Set<ExpenseHistory>();

    /// <summary>Gets the payment records in the database.</summary>
    public DbSet<PaymentRecord> PaymentRecords => this.Set<PaymentRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureExpenseCategory(modelBuilder.Entity<ExpenseCategory>());
        ConfigureExpense(modelBuilder.Entity<Expense>());
        ConfigureExpenseHistory(modelBuilder.Entity<ExpenseHistory>());
        ConfigurePaymentRecord(modelBuilder.Entity<PaymentRecord>());
    }

    private static void ConfigureExpenseCategory(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("ExpenseCategories");
        builder.HasKey(category => category.Id);

        builder.Property(category => category.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(category => category.Name)
            .IsUnique();
    }

    private static void ConfigureExpense(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses");
        builder.HasKey(expense => expense.Id);

        builder.Property(expense => expense.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(expense => expense.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(expense => expense.Amount)
            .HasPrecision(18, 2);

        builder.Property(expense => expense.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasOne(expense => expense.Category)
            .WithMany(category => category.Expenses)
            .HasForeignKey(expense => expense.ExpenseCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureExpenseHistory(EntityTypeBuilder<ExpenseHistory> builder)
    {
        builder.ToTable("ExpenseHistory");
        builder.HasKey(history => history.Id);

        builder.Property(history => history.Action)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(history => history.ActorId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(history => history.RejectionReason)
            .HasMaxLength(500);

        builder.Property(history => history.Changes)
            .HasMaxLength(2000);

        builder.HasOne(history => history.Expense)
            .WithMany(expense => expense.History)
            .HasForeignKey(history => history.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigurePaymentRecord(EntityTypeBuilder<PaymentRecord> builder)
    {
        builder.ToTable("PaymentRecords");
        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.ActorId)
            .IsRequired()
            .HasMaxLength(450);

        builder.HasOne(payment => payment.Expense)
            .WithOne(expense => expense.Payment)
            .HasForeignKey<PaymentRecord>(payment => payment.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(payment => payment.ExpenseId)
            .IsUnique();
    }
}
