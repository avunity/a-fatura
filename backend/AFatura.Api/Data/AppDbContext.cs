using AFatura.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<AccountantAssignment> AccountantAssignments => Set<AccountantAssignment>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<EFaturaImport> EFaturaImports => Set<EFaturaImport>();
    public DbSet<EFaturaRecord> EFaturaRecords => Set<EFaturaRecord>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankStatementImport> BankStatementImports => Set<BankStatementImport>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageRead> MessageReads => Set<MessageRead>();
    public DbSet<DocumentRequest> DocumentRequests => Set<DocumentRequest>();
    public DbSet<PeriodClose> PeriodCloses => Set<PeriodClose>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
        });

        b.Entity<Organization>(e =>
        {
            e.HasIndex(x => x.Nif);
            e.Property(x => x.Nif).HasMaxLength(20);
        });

        b.Entity<Membership>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.OrganizationId }).IsUnique();
            e.HasIndex(x => x.MailboxToken).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.Memberships).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Organization).WithMany(o => o.Members).HasForeignKey(x => x.OrganizationId);
        });

        b.Entity<AccountantAssignment>(e =>
        {
            e.HasIndex(x => new { x.AccountantUserId, x.OrganizationId }).IsUnique();
            e.HasOne(x => x.AccountantUser).WithMany().HasForeignKey(x => x.AccountantUserId);
            e.HasOne(x => x.Organization).WithMany(o => o.Accountants).HasForeignKey(x => x.OrganizationId);
        });

        b.Entity<Category>(e => e.HasIndex(x => new { x.OrganizationId, x.Name }));
        b.Entity<Vehicle>(e => e.HasIndex(x => new { x.OrganizationId, x.Plate }).IsUnique());
        b.Entity<Supplier>(e => e.HasIndex(x => new { x.OrganizationId, x.Nif }).IsUnique());
        b.Entity<StoredFile>(e => e.HasIndex(x => new { x.OrganizationId, x.Sha256 }));

        b.Entity<Invoice>(e =>
        {
            e.Ignore(x => x.CollectedInEFatura);
            e.HasIndex(x => new { x.OrganizationId, x.IssueDate });
            e.HasIndex(x => new { x.OrganizationId, x.SupplierNif, x.DocumentNumber });
            e.HasIndex(x => new { x.OrganizationId, x.Atcud });
            e.HasIndex(x => x.Status);
            e.Property(x => x.NetAmount).HasPrecision(14, 2);
            e.Property(x => x.VatAmount).HasPrecision(14, 2);
            e.Property(x => x.TotalAmount).HasPrecision(14, 2);
            e.OwnsMany(x => x.VatLines, vl => vl.ToJson());
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedByUserId);
            e.HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Vehicle).WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.EFaturaRecord).WithMany().HasForeignKey(x => x.EFaturaRecordId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<EFaturaRecord>(e =>
        {
            e.HasIndex(x => new { x.OrganizationId, x.UniqueKey }).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.IssueDate });
            e.Property(x => x.Total).HasPrecision(14, 2);
            e.Property(x => x.Vat).HasPrecision(14, 2);
            e.Property(x => x.Base).HasPrecision(14, 2);
        });

        b.Entity<BankTransaction>(e =>
        {
            e.HasIndex(x => new { x.BankAccountId, x.Hash }).IsUnique();
            e.HasIndex(x => new { x.BankAccountId, x.Date });
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.Balance).HasPrecision(14, 2);
        });

        b.Entity<Message>(e =>
        {
            e.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderUserId);
        });
        b.Entity<MessageRead>(e => e.HasKey(x => new { x.MessageId, x.UserId }));
        b.Entity<PeriodClose>(e => e.HasIndex(x => new { x.OrganizationId, x.Year, x.Month }).IsUnique());
        b.Entity<AuditLog>(e => e.HasIndex(x => new { x.OrganizationId, x.CreatedAt }));
    }
}
