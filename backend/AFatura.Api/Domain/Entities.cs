namespace AFatura.Api.Domain;

// ───────────── Enums ─────────────

public enum PlatformRole { User, Accountant, Admin }
public enum OrganizationType { Company, Individual }
public enum MemberRole { Collaborator, Manager }

public enum InvoiceSource { Upload, Mobile, Email, Manual }
public enum InvoiceStatus { Pending, Processing, Processed, NeedsReview, Failed }
public enum ExtractionMethod { None, Qr, Ai, QrAndAi, Manual }
public enum ReimbursementStatus { NotApplicable, Pending, Approved, Paid, Rejected }

public enum FuelType { Gasoline, Diesel, Electric, PlugInHybrid, Hybrid, Lpg, Other }
public enum DocumentRequestStatus { Open, Fulfilled, Dismissed }

// ───────────── Identity & tenancy ─────────────

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public PlatformRole PlatformRole { get; set; } = PlatformRole.User;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    public List<Membership> Memberships { get; set; } = [];
}

/// <summary>A company or an individual taxpayer. An individual is an organization with a single Manager.</summary>
public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public OrganizationType Type { get; set; }
    public string Name { get; set; } = "";
    public string? Nif { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Day of the month by which the accountant wants the previous month's documents.</summary>
    public int SubmissionDeadlineDay { get; set; } = 10;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Membership> Members { get; set; } = [];
    public List<AccountantAssignment> Accountants { get; set; } = [];
}

public class Membership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public MemberRole Role { get; set; }
    /// <summary>Local part of this member's private inbound mail address (token@inbound-domain).</summary>
    public string MailboxToken { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AccountantAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountantUserId { get; set; }
    public User AccountantUser { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public DateTime Since { get; set; } = DateTime.UtcNow;
}

// ───────────── Master data ─────────────

public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Null = global default category visible to all organizations.</summary>
    public Guid? OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#64748b";
    /// <summary>Comma separated keywords used by the rule-based classifier.</summary>
    public string Keywords { get; set; } = "";
    public bool IsVehicleRelated { get; set; }
    /// <summary>Informative default VAT deductibility (%) — to be validated by the accountant.</summary>
    public decimal? VatDeductiblePercent { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Plate { get; set; } = "";
    public string? Make { get; set; }
    public string? Model { get; set; }
    public FuelType FuelType { get; set; }
    public DateOnly? AcquiredAt { get; set; }
    public decimal? AcquisitionValue { get; set; }
    public DateOnly? InsuranceRenewal { get; set; }
    public DateOnly? InspectionDue { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A project (job, client engagement, cost centre) to which expenses can be allocated.</summary>
public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string? ClientName { get; set; }
    public string? Description { get; set; }
    public string Color { get; set; } = "#0f766e";
    public decimal? Budget { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Supplier memory per organization — learns default category / vehicle from user corrections.</summary>
public class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Nif { get; set; } = "";
    public string? Name { get; set; }
    public Guid? DefaultCategoryId { get; set; }
    public Guid? DefaultVehicleId { get; set; }
    public int InvoiceCount { get; set; }
}

// ───────────── Documents ─────────────

public class StoredFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class VatLine
{
    public string? Region { get; set; } // PT, PT-AC, PT-MA
    public decimal Rate { get; set; }   // 0, 6, 13, 23 (or regional)
    public decimal Base { get; set; }
    public decimal Vat { get; set; }
}

public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public Guid UploadedByUserId { get; set; }
    public User UploadedBy { get; set; } = null!;
    public InvoiceSource Source { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Pending;
    public ExtractionMethod ExtractionMethod { get; set; }

    public Guid? FileId { get; set; }
    public StoredFile? File { get; set; }

    // Fiscal data
    public string? SupplierNif { get; set; }
    public string? SupplierName { get; set; }
    public string? BuyerNif { get; set; }
    public string? DocumentType { get; set; } // FT, FS, FR, NC, ND, RC...
    public string? DocumentNumber { get; set; }
    public string? Atcud { get; set; }
    public DateOnly? IssueDate { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public string Currency { get; set; } = "EUR";
    public List<VatLine> VatLines { get; set; } = [];
    public string? Summary { get; set; }
    public string? LicensePlateDetected { get; set; }
    public decimal? FuelLiters { get; set; }
    public decimal? EnergyKwh { get; set; }

    // Classification
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public Guid? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public decimal? ClassificationConfidence { get; set; }
    public string? ClassificationReason { get; set; }

    // Cross-checks
    public Guid? EFaturaRecordId { get; set; }
    public EFaturaRecord? EFaturaRecord { get; set; }
    public bool CollectedInEFatura => EFaturaRecordId != null;
    public Guid? DuplicateOfId { get; set; }
    public bool HasValidQr { get; set; }
    public string? QrRaw { get; set; }
    public List<string> Flags { get; set; } = [];
    public string? ExtractionJson { get; set; }
    public string? ProcessingError { get; set; }

    // Expense workflow
    public bool PaidPersonally { get; set; }
    public ReimbursementStatus Reimbursement { get; set; } = ReimbursementStatus.NotApplicable;
    public string? Notes { get; set; }
    public List<string> Tags { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
}

// ───────────── e-Fatura (AT) ─────────────

public class EFaturaImport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ImportedByUserId { get; set; }
    public string FileName { get; set; } = "";
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int RecordsTotal { get; set; }
    public int RecordsNew { get; set; }
    public int RecordsAlreadyKnown { get; set; }
    public int RecordsMatched { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class EFaturaRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ImportId { get; set; }
    public string SupplierNif { get; set; } = "";
    public string? SupplierName { get; set; }
    public string DocumentNumber { get; set; } = "";
    public string? Atcud { get; set; }
    public string? DocumentType { get; set; }
    public DateOnly IssueDate { get; set; }
    public decimal Total { get; set; }
    public decimal? Vat { get; set; }
    public decimal? Base { get; set; }
    public string? Sector { get; set; }
    public string? AtStatus { get; set; }
    /// <summary>Normalized SupplierNif|DocumentNumber — unique per organization so re-imports never duplicate.</summary>
    public string UniqueKey { get; set; } = "";
    public Guid? MatchedInvoiceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ───────────── Banking ─────────────

public class BankAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string BankName { get; set; } = "";
    public string Alias { get; set; } = "";
    public string? Iban { get; set; }
    public string Currency { get; set; } = "EUR";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class BankStatementImport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BankAccountId { get; set; }
    public Guid ImportedByUserId { get; set; }
    public Guid? FileId { get; set; }
    public string FileName { get; set; } = "";
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int TransactionsTotal { get; set; }
    public int TransactionsNew { get; set; }
    public int TransactionsDuplicate { get; set; }
    public string? OverlapWarning { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class BankTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BankAccountId { get; set; }
    public Guid ImportId { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? ValueDate { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal? Balance { get; set; }
    public string Hash { get; set; } = "";
    public Guid? MatchedInvoiceId { get; set; }
}

// ───────────── Collaboration ─────────────

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid SenderUserId { get; set; }
    public User Sender { get; set; } = null!;
    public Guid? ParentId { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>true when sent by the accountant/backoffice to the client.</summary>
    public bool FromAccountant { get; set; }
    public Guid? RelatedInvoiceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MessageRead
{
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Accountant asks the client for a missing document (typically something in e-Fatura with no uploaded file).</summary>
public class DocumentRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? EFaturaRecordId { get; set; }
    public string Description { get; set; } = "";
    public DocumentRequestStatus Status { get; set; } = DocumentRequestStatus.Open;
    public Guid? FulfilledByInvoiceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

/// <summary>A closed month cannot be edited by the client (accountant already processed it).</summary>
public class PeriodClose
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public Guid ClosedByUserId { get; set; }
    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public long Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Data { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
