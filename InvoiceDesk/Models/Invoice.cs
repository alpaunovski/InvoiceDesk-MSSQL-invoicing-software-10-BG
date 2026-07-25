using System;
using System.Collections.Generic;

namespace InvoiceDesk.Models;

/// <summary>
/// Domain invoice entity; stores customer snapshots and issued PDF metadata for immutability.
/// </summary>
public class Invoice
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int CustomerId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public InvoiceDocumentType DocumentType { get; set; } = InvoiceDocumentType.Invoice;
    public string? RefInvoiceNumber { get; set; }
    public DateTime? RefInvoiceDate { get; set; }
    public string InvoiceLanguage { get; set; } = "en";
    public string Currency { get; set; } = "BGN";
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public string CustomerNameSnapshot { get; set; } = string.Empty;
    public string CustomerAddressSnapshot { get; set; } = string.Empty;
    public string CustomerVatSnapshot { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public byte[]? IssuedPdf { get; set; }
    public string? IssuedPdfFileName { get; set; }
    public string? IssuedPdfSha256 { get; set; }
    public DateTime? IssuedPdfCreatedAtUtc { get; set; }

    public byte[]? SignedPdf { get; set; }
    public string? SignedPdfFileName { get; set; }
    public string? SignedPdfSha256 { get; set; }
    public DateTime? SignedPdfCreatedAtUtc { get; set; }

    public Company? Company { get; set; }
    public Customer? Customer { get; set; }
    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
}
