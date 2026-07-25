using System.Globalization;
using InvoiceDesk.Data;
using InvoiceDesk.Helpers;
using InvoiceDesk.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvoiceDesk.Services;

public class InvoiceService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICompanyContext _companyContext;
    private readonly PdfExportService _pdfExportService;
    private readonly ILogger<InvoiceService> _logger;
    private const string DraftPrefix = "DRAFT";

    public InvoiceService(IDbContextFactory<AppDbContext> dbFactory, ICompanyContext companyContext, PdfExportService pdfExportService, ILogger<InvoiceService> logger)
    {
        _dbFactory = dbFactory;
        _companyContext = companyContext;
        _pdfExportService = pdfExportService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a draft invoice for the specified company/customer, snapshotting customer data for immutability.
    /// </summary>
    public async Task<Invoice> CreateDraftAsync(int companyId, int customerId, InvoiceDocumentType documentType = InvoiceDocumentType.Invoice, string? refInvoiceNumber = null, DateTime? refInvoiceDate = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, cancellationToken);
        if (customer == null)
        {
            throw new InvalidOperationException("Customer not found for company");
        }

        // Draft invoices get a temporary number and capture customer snapshot for immutability.
        var invoice = new Invoice
        {
            CompanyId = companyId,
            CustomerId = customerId,
            InvoiceNumber = GenerateDraftNumber(companyId),
            Status = InvoiceStatus.Draft,
            DocumentType = documentType,
            RefInvoiceNumber = refInvoiceNumber,
            RefInvoiceDate = refInvoiceDate,
            IssueDate = DateTime.Today,
            InvoiceLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            Currency = CurrencyHelper.NormalizeCurrencyOrDefault("BGN"),
            CustomerNameSnapshot = customer.Name,
            CustomerAddressSnapshot = customer.Address,
            CustomerVatSnapshot = SelectCustomerTaxIdentifier(customer)
        };

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    /// <summary>
    /// Persists edits to a draft invoice and its lines while keeping historical customer snapshots up to date.
    /// </summary>
    public async Task<Invoice> SaveInvoiceAsync(Invoice invoice, IEnumerable<InvoiceLine> lines, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Invoices.Include(i => i.Lines).Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == invoice.Id && i.CompanyId == _companyContext.CurrentCompanyId, cancellationToken);

        if (existing == null)
        {
            throw new InvalidOperationException("Invoice not found");
        }

        if (existing.Status != InvoiceStatus.Draft || existing.IssuedAtUtc != null)
        {
            throw new InvalidOperationException("Issued invoices cannot be edited");
        }

        existing.CustomerId = invoice.CustomerId;
        existing.IssueDate = invoice.IssueDate;
        existing.DocumentType = invoice.DocumentType;
        existing.RefInvoiceNumber = invoice.RefInvoiceNumber;
        existing.RefInvoiceDate = invoice.RefInvoiceDate;
        existing.InvoiceLanguage = string.IsNullOrWhiteSpace(invoice.InvoiceLanguage)
            ? existing.InvoiceLanguage
            : invoice.InvoiceLanguage;
        existing.Currency = CurrencyHelper.NormalizeCurrencyOrDefault(invoice.Currency);
        existing.Notes = invoice.Notes;

        if (string.IsNullOrWhiteSpace(existing.InvoiceNumber))
        {
            existing.InvoiceNumber = GenerateDraftNumber(existing.CompanyId);
        }

        var customerValid = await db.Customers.AnyAsync(c => c.Id == existing.CustomerId && c.CompanyId == existing.CompanyId, cancellationToken);
        if (!customerValid)
        {
            throw new InvalidOperationException("Customer not found for this company");
        }

        await db.Entry(existing).Reference(i => i.Customer).LoadAsync(cancellationToken);
        if (existing.Customer != null)
        {
            // Persist customer details so later changes do not affect historical invoices.
            existing.CustomerNameSnapshot = existing.Customer.Name;
            existing.CustomerAddressSnapshot = existing.Customer.Address;
            existing.CustomerVatSnapshot = SelectCustomerTaxIdentifier(existing.Customer);
        }

        UpdateLines(existing, lines);
        ApplyTotals(existing);

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    /// <summary>
    /// Issues a draft invoice atomically, assigning the next number and generating the issued PDF.
    /// </summary>
    public async Task<Invoice> IssueInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        await using var strategyDb = await _dbFactory.CreateDbContextAsync(cancellationToken);
        // Execution strategy retries transient SQL errors during issuance (e.g., deadlocks/timeouts).
        var strategy = strategyDb.Database.CreateExecutionStrategy();

        var issuedInvoice = await strategy.ExecuteAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
			// Serializable isolation to avoid races when multiple invoices are issued concurrently.
			await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

            var invoice = await db.Invoices
                .Include(i => i.Lines)
                .Include(i => i.Customer)
                .Include(i => i.Company)
                .FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == _companyContext.CurrentCompanyId, cancellationToken);

            if (invoice == null)
            {
                throw new InvalidOperationException("Invoice not found");
            }

            if (invoice.Status != InvoiceStatus.Draft || invoice.IssuedAtUtc != null)
            {
                throw new InvalidOperationException("Only draft invoices can be issued");
            }

            var company = invoice.Company ?? throw new InvalidOperationException("Invoice company missing");
            var customer = invoice.Customer ?? throw new InvalidOperationException("Invoice customer missing");
            invoice.Currency = CurrencyHelper.NormalizeCurrencyOrDefault(invoice.Currency);

            var invoiceNumber = string.IsNullOrWhiteSpace(company.InvoiceNumberPrefix)
                ? company.NextInvoiceNumber.ToString()
                : $"{company.InvoiceNumberPrefix}{company.NextInvoiceNumber}";
            company.NextInvoiceNumber += 1;

            invoice.InvoiceNumber = invoiceNumber;
            invoice.CustomerNameSnapshot = customer.Name;
            invoice.CustomerAddressSnapshot = customer.Address;
            invoice.CustomerVatSnapshot = SelectCustomerTaxIdentifier(customer);
            if (string.IsNullOrWhiteSpace(invoice.InvoiceLanguage))
            {
                invoice.InvoiceLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            }
            invoice.IssuedAtUtc = DateTime.UtcNow;
            invoice.Status = InvoiceStatus.Issued;
            ApplyTotals(invoice);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Invoice {InvoiceId} issued with number {InvoiceNumber}", invoice.Id, invoice.InvoiceNumber);
            return invoice;
        });

        await _pdfExportService.GenerateAndStoreIssuedPdfAsync(issuedInvoice.Id, cancellationToken);
        return issuedInvoice;
    }

    /// <summary>
    /// Deletes a draft invoice and its lines; issued invoices remain immutable.
    /// </summary>
    public async Task<bool> DeleteInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        await using var strategyDb = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyDb.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

            var invoice = await db.Invoices
                .Include(i => i.Lines)
                .FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == _companyContext.CurrentCompanyId, cancellationToken);

            if (invoice == null)
            {
                return false;
            }

            if (invoice.Status != InvoiceStatus.Draft || invoice.IssuedAtUtc != null)
            {
                // Issued/locked invoices should not be deleted; keep data intact.
                return false;
            }

            db.InvoiceLines.RemoveRange(invoice.Lines);
            db.Invoices.Remove(invoice);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    private static string GenerateDraftNumber(int companyId)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var rand = Random.Shared.Next(1000, 9999);
        return $"{DraftPrefix}-{companyId}-{stamp}-{rand}";
    }

    private static string SelectCustomerTaxIdentifier(Customer customer)
    {
        // Prefer EIK when provided (typically for BG), otherwise fall back to VAT.
        if (!string.IsNullOrWhiteSpace(customer.Eik))
        {
            return customer.Eik;
        }

        return customer.VatNumber;
    }

    private static void UpdateLines(Invoice invoice, IEnumerable<InvoiceLine> incoming)
    {
        var incomingList = incoming.ToList();
        // Delete orphaned lines that were removed in the UI.
        var toRemove = invoice.Lines.Where(l => incomingList.All(i => i.Id != l.Id)).ToList();
        foreach (var remove in toRemove)
        {
            invoice.Lines.Remove(remove);
        }

        foreach (var incomingLine in incomingList)
        {
            if (incomingLine.Id == 0)
            {
                // Unsaved lines have Id == 0; add each one so multiple new rows do not collapse into a single entry.
                incomingLine.CompanyId = invoice.CompanyId;
                incomingLine.InvoiceId = invoice.Id;
                invoice.Lines.Add(incomingLine);
                continue;
            }

            var target = invoice.Lines.FirstOrDefault(l => l.Id == incomingLine.Id);
            if (target == null)
            {
                // New line: attach to invoice and company to keep FK consistency.
                incomingLine.CompanyId = invoice.CompanyId;
                incomingLine.InvoiceId = invoice.Id;
                invoice.Lines.Add(incomingLine);
            }
            else
            {
                // Existing line: update mutable fields only.
                target.CompanyId = invoice.CompanyId;
                target.InvoiceId = invoice.Id;
                target.Description = incomingLine.Description;
                target.Qty = incomingLine.Qty;
                target.UnitPrice = incomingLine.UnitPrice;
                target.TaxRate = incomingLine.TaxRate;
                target.VatType = incomingLine.VatType;
            }
        }
    }

    private static void ApplyTotals(Invoice invoice)
    {
        decimal subtotal = 0m;
        decimal taxTotal = 0m;

        foreach (var line in invoice.Lines)
        {
            // Calculate base per line and VAT depending on regime; round per line to avoid drift.
            var lineBase = Math.Round(line.Qty * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
            decimal vat = 0m;
            switch (line.VatType)
            {
                case VatType.Domestic:
                    vat = Math.Round(lineBase * line.TaxRate, 2, MidpointRounding.AwayFromZero);
                    break;
                case VatType.IntraEuReverseCharge:
                case VatType.ExportOutsideEu:
                case VatType.VatExempt:
                    vat = 0m;
                    break;
            }

            line.LineTotal = lineBase + vat;
            subtotal += lineBase;
            taxTotal += vat;
        }

        invoice.SubTotal = subtotal;
        invoice.TaxTotal = taxTotal;
        invoice.Total = subtotal + taxTotal;
    }
}
