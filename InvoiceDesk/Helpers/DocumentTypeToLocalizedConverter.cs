using System;
using System.Globalization;
using System.Windows.Data;
using InvoiceDesk.Models;
using InvoiceDesk.Resources;

namespace InvoiceDesk.Helpers;

public class DocumentTypeToLocalizedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not InvoiceDocumentType docType)
        {
            return value;
        }

        return docType switch
        {
            InvoiceDocumentType.Invoice => Strings.DocTypeInvoice,
            InvoiceDocumentType.DebitNote => Strings.DocTypeDebitNote,
            InvoiceDocumentType.CreditNote => Strings.DocTypeCreditNote,
            _ => docType.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
