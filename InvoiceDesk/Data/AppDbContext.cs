using InvoiceDesk.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceDesk.Data;

/// <summary>
/// EF Core context scoped per company, with snapshot-friendly invoice models.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Keep entity configuration split by type for readability and reuse.
        ConfigureCompany(modelBuilder);
        ConfigureCustomer(modelBuilder);
        ConfigureInvoice(modelBuilder);
        ConfigureInvoiceLine(modelBuilder);
    }

    private static void ConfigureCompany(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Company>();
        // Basic company metadata and invoice numbering settings.
        entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
        entity.Property(e => e.VatNumber).HasMaxLength(50).IsRequired();
        entity.Property(e => e.Eik).HasMaxLength(13);
        entity.Property(e => e.CountryCode).HasMaxLength(8).IsRequired();
        entity.Property(e => e.Address).HasMaxLength(400).IsRequired();
        entity.Property(e => e.BankIban).HasMaxLength(64).IsRequired();
        entity.Property(e => e.BankBic).HasMaxLength(32).IsRequired();
        entity.Property(e => e.InvoiceNumberPrefix).HasMaxLength(32);
        entity.Property(e => e.LogoPath).HasMaxLength(400);
        entity.Property(e => e.NextInvoiceNumber).HasDefaultValue(1);
    }

    private static void ConfigureCustomer(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Customer>();
        // Tenant-aware customer rows; enforce lengths and FK to company.
        entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
        entity.Property(e => e.VatNumber).HasMaxLength(50).IsRequired(false);
        entity.Property(e => e.Eik).HasMaxLength(13).IsRequired(false);
        entity.Property(e => e.CountryCode).HasMaxLength(8).IsRequired();
        entity.Property(e => e.Address).HasMaxLength(400).IsRequired();
        entity.Property(e => e.Email).HasMaxLength(200).IsRequired();
        entity.Property(e => e.Phone).HasMaxLength(50).IsRequired();

        entity.HasIndex(e => new { e.CompanyId, e.Name });

        entity.HasOne(e => e.Company)
            .WithMany(c => c.Customers)
            .HasForeignKey(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureInvoice(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Invoice>();
        // Snapshotted customer details and monetary precision per invoice.
        entity.Property(e => e.InvoiceNumber).HasMaxLength(64).IsRequired();
        entity.Property(e => e.DocumentType).HasDefaultValue(InvoiceDocumentType.Invoice);
        entity.Property(e => e.RefInvoiceNumber).HasMaxLength(64);
        entity.Property(e => e.Currency).HasMaxLength(8).IsRequired();
        entity.Property(e => e.InvoiceLanguage).HasMaxLength(8).IsRequired().HasDefaultValue("en");
        entity.Property(e => e.CustomerNameSnapshot).HasMaxLength(200).IsRequired();
        entity.Property(e => e.CustomerAddressSnapshot).HasMaxLength(400).IsRequired();
        entity.Property(e => e.CustomerVatSnapshot).HasMaxLength(64).IsRequired();
        entity.Property(e => e.Notes).HasMaxLength(1000);
        entity.Property(e => e.IssuedPdfFileName).HasMaxLength(255);
        entity.Property(e => e.IssuedPdfSha256).HasMaxLength(64);
        entity.Property(e => e.SignedPdfFileName).HasMaxLength(255);
        entity.Property(e => e.SignedPdfSha256).HasMaxLength(64);

        entity.Property(e => e.SubTotal).HasPrecision(18, 2);
        entity.Property(e => e.TaxTotal).HasPrecision(18, 2);
        entity.Property(e => e.Total).HasPrecision(18, 2);

        entity.HasIndex(e => new { e.CompanyId, e.InvoiceNumber }).IsUnique();
        entity.HasIndex(e => new { e.CompanyId, e.IssueDate });
        entity.HasIndex(e => new { e.CompanyId, e.CustomerId });

        entity.HasOne(e => e.Company)
            .WithMany(c => c.Invoices)
            .HasForeignKey(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Customer)
            .WithMany(c => c.Invoices)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasMany(e => e.Lines)
            .WithOne(l => l.Invoice)
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureInvoiceLine(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<InvoiceLine>();
        // Per-line monetary precision; tie back to invoice.
        entity.Property(e => e.Description).HasMaxLength(400).IsRequired();
        entity.Property(e => e.Qty).HasPrecision(18, 3);
        entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
        entity.Property(e => e.TaxRate).HasPrecision(5, 4);
        entity.Property(e => e.LineTotal).HasPrecision(18, 2);

        entity.HasOne(e => e.Invoice)
            .WithMany(i => i.Lines)
            .HasForeignKey(e => e.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
