using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using InvoiceDesk.Helpers;
using InvoiceDesk.Models;

namespace InvoiceDesk.ViewModels;

public partial class InvoiceViewModel : ObservableObject
{
    [ObservableProperty]
    private int id;

    [ObservableProperty]
    private int companyId;

    [ObservableProperty]
    private int customerId;

    [ObservableProperty]
    private string invoiceNumber = string.Empty;

    [ObservableProperty]
    private DateTime issueDate = DateTime.Today;

    [ObservableProperty]
    private InvoiceStatus status = InvoiceStatus.Draft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInvoice))]
    [NotifyPropertyChangedFor(nameof(IsDebitNote))]
    [NotifyPropertyChangedFor(nameof(IsCreditNote))]
    [NotifyPropertyChangedFor(nameof(RequiresRefInvoice))]
    private InvoiceDocumentType documentType = InvoiceDocumentType.Invoice;

    [ObservableProperty]
    private string? refInvoiceNumber;

    [ObservableProperty]
    private DateTime? refInvoiceDate;

    [ObservableProperty]
    private string invoiceLanguage = "en";

    [ObservableProperty]
    private string currency = "BGN";

    [ObservableProperty]
    private decimal subTotal;

    [ObservableProperty]
    private decimal taxTotal;

    [ObservableProperty]
    private decimal total;

    [ObservableProperty]
    private string customerNameSnapshot = string.Empty;

    [ObservableProperty]
    private string customerAddressSnapshot = string.Empty;

    [ObservableProperty]
    private string customerVatSnapshot = string.Empty;

    [ObservableProperty]
    private string? notes;

    [ObservableProperty]
    private ObservableCollection<InvoiceLineViewModel> lines = new();

    public bool IsDraft => Status == InvoiceStatus.Draft;
    public bool IsIssued => Status == InvoiceStatus.Issued;
    public bool IsInvoice => DocumentType == InvoiceDocumentType.Invoice;
    public bool IsDebitNote => DocumentType == InvoiceDocumentType.DebitNote;
    public bool IsCreditNote => DocumentType == InvoiceDocumentType.CreditNote;
    public bool RequiresRefInvoice => DocumentType == InvoiceDocumentType.DebitNote || DocumentType == InvoiceDocumentType.CreditNote;

    public void RecalculateTotals()
    {
        decimal subtotal = 0m;
        decimal tax = 0m;
        foreach (var line in Lines)
        {
            var normalizedRate = NormalizeTaxRate(line);
            var baseAmount = Math.Round(line.Qty * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
            decimal vat = 0m;
            if (line.VatType == VatType.Domestic)
            {
                vat = Math.Round(baseAmount * normalizedRate, 2, MidpointRounding.AwayFromZero);
            }

            line.LineTotal = baseAmount + vat;
            subtotal += baseAmount;
            tax += vat;
        }

        SubTotal = subtotal;
        TaxTotal = tax;
        Total = subtotal + tax;
    }

    private static decimal NormalizeTaxRate(InvoiceLineViewModel line)
    {
        var rate = line.TaxRate;
        if (rate > 1m)
        {
            rate = Math.Round(rate / 100m, 4, MidpointRounding.AwayFromZero);
            line.TaxRate = rate;
        }
        else if (rate < 0m)
        {
            rate = 0m;
            line.TaxRate = rate;
        }

        return rate;
    }

    public static InvoiceViewModel FromEntity(Invoice invoice)
    {
        return new InvoiceViewModel
        {
            Id = invoice.Id,
            CompanyId = invoice.CompanyId,
            CustomerId = invoice.CustomerId,
            InvoiceNumber = invoice.InvoiceNumber,
            IssueDate = invoice.IssueDate,
            Status = invoice.Status,
            DocumentType = invoice.DocumentType,
            RefInvoiceNumber = invoice.RefInvoiceNumber,
            RefInvoiceDate = invoice.RefInvoiceDate,
            InvoiceLanguage = string.IsNullOrWhiteSpace(invoice.InvoiceLanguage) ? "en" : invoice.InvoiceLanguage,
            Currency = CurrencyHelper.NormalizeCurrencyOrDefault(invoice.Currency),
            SubTotal = invoice.SubTotal,
            TaxTotal = invoice.TaxTotal,
            Total = invoice.Total,
            CustomerNameSnapshot = invoice.CustomerNameSnapshot,
            CustomerAddressSnapshot = invoice.CustomerAddressSnapshot,
            CustomerVatSnapshot = invoice.CustomerVatSnapshot,
            Notes = invoice.Notes,
            Lines = new ObservableCollection<InvoiceLineViewModel>(invoice.Lines.Select(InvoiceLineViewModel.FromEntity))
        };
    }

    public Invoice ToEntity()
    {
        return new Invoice
        {
            Id = Id,
            CompanyId = CompanyId,
            CustomerId = CustomerId,
            InvoiceNumber = InvoiceNumber,
            IssueDate = IssueDate,
            Status = Status,
            DocumentType = DocumentType,
            RefInvoiceNumber = RefInvoiceNumber,
            RefInvoiceDate = RefInvoiceDate,
            InvoiceLanguage = string.IsNullOrWhiteSpace(InvoiceLanguage) ? "en" : InvoiceLanguage,
            Currency = CurrencyHelper.NormalizeCurrencyOrDefault(Currency),
            SubTotal = SubTotal,
            TaxTotal = TaxTotal,
            Total = Total,
            CustomerNameSnapshot = CustomerNameSnapshot,
            CustomerAddressSnapshot = CustomerAddressSnapshot,
            CustomerVatSnapshot = CustomerVatSnapshot,
            Notes = Notes,
            Lines = Lines.Select(l => l.ToEntity(CompanyId)).ToList()
        };
    }
}
