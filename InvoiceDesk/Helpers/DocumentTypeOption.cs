using InvoiceDesk.Models;

namespace InvoiceDesk.Helpers;

public class DocumentTypeOption
{
    public InvoiceDocumentType Value { get; set; }
    public string Label { get; set; } = string.Empty;
}
