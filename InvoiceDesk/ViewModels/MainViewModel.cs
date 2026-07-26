using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoiceDesk.Helpers;
using InvoiceDesk.Models;
using InvoiceDesk.Resources;
using InvoiceDesk.Services;
using Microsoft.Extensions.Logging;

namespace InvoiceDesk.ViewModels;

/// <summary>
/// Top-level dashboard view model that coordinates companies, customers, invoices, and commands exposed to the UI.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly CompanyService _companyService;
    private readonly CustomerService _customerService;
    private readonly InvoiceQueryService _invoiceQueryService;
    private readonly InvoiceService _invoiceService;
    private readonly PdfExportService _pdfExportService;
    private readonly PdfSigningService _pdfSigningService;
    private readonly DatabaseBackupService _backupService;
    private readonly ILanguageService _languageService;
    private readonly ICompanyContext _companyContext;
    private readonly UserSettingsService _settingsService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly CurrencyDisplayOptions _currencyOptions;
    private InvoiceViewModel? _selectedInvoiceListener;

    [ObservableProperty]
    private ObservableCollection<Company> companies = new();

    [ObservableProperty]
    private Company? selectedCompany;

    [ObservableProperty]
    private ObservableCollection<Customer> customers = new();

    [ObservableProperty]
    private ObservableCollection<Invoice> invoices = new();

    [ObservableProperty]
    private Invoice? selectedInvoiceSummary;

    [ObservableProperty]
    private InvoiceViewModel? selectedInvoice;

    [ObservableProperty]
    private ObservableCollection<Invoice> referenceInvoices = new();

    [ObservableProperty]
    private Invoice? selectedReferenceInvoice;

    private bool _isApplyingReferenceInvoice;

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private DateTime? fromDate;

    [ObservableProperty]
    private DateTime? toDate;

    [ObservableProperty]
    private Customer? selectedCustomerFilter;

    [ObservableProperty]
    private Customer? selectedCustomerForDraft;

    [ObservableProperty]
    private string selectedCulture = "en";

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string busyMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<VatTypeOption> vatTypes = new();

    [ObservableProperty]
    private ObservableCollection<DocumentTypeOption> documentTypes = new();

    public XmlLanguage UiLanguage => XmlLanguage.GetLanguage(_languageService.CurrentCulture.IetfLanguageTag);

    public ObservableCollection<CultureOption> Cultures { get; } = new()
    {
        new CultureOption("en", "English"),
        new CultureOption("bg", "Български")
    };

    public ObservableCollection<string> Currencies { get; } = new()
    {
        "BGN",
        "EUR"
    };

    public bool ShouldShowDualCurrency => SelectedInvoice != null && CurrencyHelper.ShouldShowDualCurrency(_currencyOptions, SelectedInvoice.Currency);

    public decimal SubTotalEur => ShouldShowDualCurrency && SelectedInvoice != null
        ? CurrencyHelper.ConvertBgnToEur(SelectedInvoice.SubTotal)
        : 0m;

    public decimal TaxTotalEur => ShouldShowDualCurrency && SelectedInvoice != null
        ? CurrencyHelper.ConvertBgnToEur(SelectedInvoice.TaxTotal)
        : 0m;

    public decimal TotalEur => ShouldShowDualCurrency && SelectedInvoice != null
        ? CurrencyHelper.ConvertBgnToEur(SelectedInvoice.Total)
        : 0m;

    public MainViewModel(
        CompanyService companyService,
        CustomerService customerService,
        InvoiceQueryService invoiceQueryService,
        InvoiceService invoiceService,
        PdfExportService pdfExportService,
        PdfSigningService pdfSigningService,
        DatabaseBackupService backupService,
        CurrencyDisplayOptions currencyOptions,
        ILanguageService languageService,
        ICompanyContext companyContext,
        UserSettingsService settingsService,
        ILogger<MainViewModel> logger)
    {
        _companyService = companyService;
        _customerService = customerService;
        _invoiceQueryService = invoiceQueryService;
        _invoiceService = invoiceService;
        _pdfExportService = pdfExportService;
        _pdfSigningService = pdfSigningService;
        _backupService = backupService;
        _currencyOptions = currencyOptions;
        _languageService = languageService;
        _companyContext = companyContext;
        _settingsService = settingsService;
        _logger = logger;
        // React when the active company or UI culture changes so dependent lists refresh.
        _companyContext.CompanyChanged += async (_, id) => await OnCompanyChangedAsync(id);
        _languageService.CultureChanged += (_, _) =>
        {
            RefreshVatTypes();
            RefreshDocumentTypes();
            OnPropertyChanged(nameof(UiLanguage));
        };

		// Ensure VAT and Document options exist even before initialization completes.
		RefreshVatTypes();
		RefreshDocumentTypes();
    }

    partial void OnSelectedInvoiceSummaryChanged(Invoice? value)
    {
        if (value != null)
        {
            _ = SelectInvoiceAsync(value.Id);
        }
    }

    partial void OnSelectedCompanyChanged(Company? value)
    {
        if (value != null && ChangeCompanyCommand.CanExecute(null))
        {
            ChangeCompanyCommand.Execute(null);
        }
    }

    partial void OnSelectedCultureChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && ChangeCultureCommand.CanExecute(null))
        {
            ChangeCultureCommand.Execute(null);
        }
    }

    partial void OnSelectedInvoiceChanged(InvoiceViewModel? value)
    {
        if (_selectedInvoiceListener != null)
        {
            _selectedInvoiceListener.PropertyChanged -= OnSelectedInvoicePropertyChanged;
        }

        _selectedInvoiceListener = value;

        if (value != null)
        {
            value.PropertyChanged += OnSelectedInvoicePropertyChanged;
        }

        OnPropertyChanged(nameof(ShouldShowDualCurrency));
        OnPropertyChanged(nameof(SubTotalEur));
        OnPropertyChanged(nameof(TaxTotalEur));
        OnPropertyChanged(nameof(TotalEur));

        RefreshReferenceInvoices();
    }

    partial void OnSelectedReferenceInvoiceChanged(Invoice? value)
    {
        if (_isApplyingReferenceInvoice)
        {
            return;
        }

        if (value != null && SelectedInvoice != null && SelectedInvoice.IsDraft && SelectedInvoice.RequiresRefInvoice)
        {
            if (SelectedInvoice.RefInvoiceNumber != value.InvoiceNumber)
            {
                _ = ApplyReferenceInvoiceAsync(value);
            }
        }
    }

    public async Task InitializeAsync()
    {
        // Load persisted settings, culture, and initial company scope before populating UI lists.
        var settings = await _settingsService.LoadAsync();
        SelectedCulture = settings.Culture;
        await _languageService.SetCultureAsync(SelectedCulture);
        RefreshVatTypes();

        var allCompanies = await _companyService.GetCompaniesAsync();
        Companies = new ObservableCollection<Company>(allCompanies);

        if (settings.CompanyId.HasValue)
        {
            SelectedCompany = Companies.FirstOrDefault(c => c.Id == settings.CompanyId.Value);
        }

        SelectedCompany ??= Companies.FirstOrDefault();
        if (SelectedCompany != null)
        {
            await _companyContext.SetCompanyAsync(SelectedCompany.Id);
            await LoadCustomersAsync();
            await LoadInvoicesAsync();
        }
    }

    private async Task OnCompanyChangedAsync(int companyId)
    {
        SelectedCompany = Companies.FirstOrDefault(c => c.Id == companyId);
        // Clear company-scoped selections so the UI refreshes with the new tenant's data.
        SelectedCustomerFilter = null;
        SelectedCustomerForDraft = null;
        SelectedInvoice = null;
        SelectedInvoiceSummary = null;
        SearchText = null;
        FromDate = null;
        ToDate = null;
        await LoadCustomersAsync();
        await LoadInvoicesAsync();
        var settings = await _settingsService.LoadAsync();
        settings.CompanyId = companyId;
        await _settingsService.SaveAsync(settings);
    }

    public async Task ReloadCompaniesAsync()
    {
        var currentId = SelectedCompany?.Id;
        var list = await _companyService.GetCompaniesAsync();
        Companies = new ObservableCollection<Company>(list);

        SelectedCompany = Companies.FirstOrDefault(c => c.Id == currentId) ?? Companies.FirstOrDefault();
        if (SelectedCompany != null)
        {
            await _companyContext.SetCompanyAsync(SelectedCompany.Id);
        }
    }

    public async Task ReloadCustomersAsync()
    {
        await LoadCustomersAsync();
        await LoadInvoicesAsync();
    }

    [RelayCommand]
    private async Task ChangeCompanyAsync()
    {
        if (SelectedCompany == null)
        {
            return;
        }

        await _companyContext.SetCompanyAsync(SelectedCompany.Id);
    }

    [RelayCommand]
    private async Task ChangeCultureAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedCulture))
        {
            return;
        }

        await _languageService.SetCultureAsync(SelectedCulture);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadInvoicesAsync();
    }

    [RelayCommand]
    private async Task NewDraftAsync()
    {
        if (SelectedCompany == null || !Customers.Any())
        {
            StatusMessage = Strings.MessageSelectCompanyCustomers;
            return;
        }

        var customerId = SelectedCustomerForDraft?.Id;
        if (customerId == null)
        {
            StatusMessage = Strings.MessageSelectCustomer;
            return;
        }
		// Create a draft tied to the selected company and pre-selected customer.
        var draft = await _invoiceService.CreateDraftAsync(SelectedCompany.Id, customerId.Value, InvoiceDocumentType.Invoice);
        await LoadInvoicesAsync();
        await SelectInvoiceAsync(draft.Id);
        StatusMessage = Strings.MessageDraftCreated;
    }

    [RelayCommand]
    private async Task NewDebitNoteAsync()
    {
        if (SelectedCompany == null || !Customers.Any())
        {
            StatusMessage = Strings.MessageSelectCompanyCustomers;
            return;
        }

        var refInvoice = SelectedInvoiceSummary ?? (SelectedInvoice != null ? Invoices.FirstOrDefault(i => i.Id == SelectedInvoice.Id) : null);

        int customerId;
        string? refInvoiceNum = null;
        DateTime? refInvoiceDt = null;

        if (refInvoice != null)
        {
            customerId = refInvoice.CustomerId;
            refInvoiceNum = refInvoice.InvoiceNumber;
            refInvoiceDt = refInvoice.IssueDate;
        }
        else
        {
            if (SelectedCustomerForDraft?.Id == null)
            {
                StatusMessage = Strings.MessageSelectCustomer;
                return;
            }
            customerId = SelectedCustomerForDraft.Id;
        }

        var draft = await _invoiceService.CreateDraftAsync(SelectedCompany.Id, customerId, InvoiceDocumentType.DebitNote, refInvoiceNum, refInvoiceDt);
        await LoadInvoicesAsync();
        await SelectInvoiceAsync(draft.Id);

        if (refInvoice != null && SelectedInvoice != null)
        {
            await ApplyReferenceInvoiceAsync(refInvoice);
        }

        StatusMessage = Strings.MessageDraftCreated;
    }

    [RelayCommand]
    private async Task NewCreditNoteAsync()
    {
        if (SelectedCompany == null || !Customers.Any())
        {
            StatusMessage = Strings.MessageSelectCompanyCustomers;
            return;
        }

        var refInvoice = SelectedInvoiceSummary ?? (SelectedInvoice != null ? Invoices.FirstOrDefault(i => i.Id == SelectedInvoice.Id) : null);

        int customerId;
        string? refInvoiceNum = null;
        DateTime? refInvoiceDt = null;

        if (refInvoice != null)
        {
            customerId = refInvoice.CustomerId;
            refInvoiceNum = refInvoice.InvoiceNumber;
            refInvoiceDt = refInvoice.IssueDate;
        }
        else
        {
            if (SelectedCustomerForDraft?.Id == null)
            {
                StatusMessage = Strings.MessageSelectCustomer;
                return;
            }
            customerId = SelectedCustomerForDraft.Id;
        }

        var draft = await _invoiceService.CreateDraftAsync(SelectedCompany.Id, customerId, InvoiceDocumentType.CreditNote, refInvoiceNum, refInvoiceDt);
        await LoadInvoicesAsync();
        await SelectInvoiceAsync(draft.Id);

        if (refInvoice != null && SelectedInvoice != null)
        {
            await ApplyReferenceInvoiceAsync(refInvoice);
        }

        StatusMessage = Strings.MessageDraftCreated;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedInvoice == null)
        {
            StatusMessage = "Select an invoice first";
            MessageBox.Show(StatusMessage, Strings.Save, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SelectedInvoice.IsDraft)
        {
            StatusMessage = Strings.MessageSaveDraftOnly;
            MessageBox.Show(StatusMessage, Strings.Save, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            SelectedInvoice.RecalculateTotals();
            var entity = SelectedInvoice.ToEntity();
			// Persist draft changes and then reload the projection list for the UI.
            await _invoiceService.SaveInvoiceAsync(entity, entity.Lines);
            await LoadInvoicesAsync();
            await SelectInvoiceAsync(entity.Id);
            StatusMessage = Strings.MessageInvoiceSaved;
            MessageBox.Show(StatusMessage, Strings.Save, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save invoice failed for {InvoiceId}", SelectedInvoice.Id);
            StatusMessage = $"Save failed: {ex.Message}";
            MessageBox.Show(StatusMessage, Strings.Save, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task IssueAsync()
    {
        if (SelectedInvoice == null)
        {
            return;
        }

        // Issue assigns the next number, locks the invoice, and generates the PDF.
        var invoice = await _invoiceService.IssueInvoiceAsync(SelectedInvoice.Id);
        await LoadInvoicesAsync();
        await SelectInvoiceAsync(invoice.Id);
        StatusMessage = string.Format(Strings.MessageInvoiceIssued, invoice.InvoiceNumber);
    }

    [RelayCommand]
    private async Task DeleteInvoiceAsync()
    {
        if (SelectedInvoice == null)
        {
            return;
        }

        if (!SelectedInvoice.IsDraft)
        {
            StatusMessage = Strings.MessageDeleteDraftOnly;
            MessageBox.Show(Strings.MessageDeleteDraftOnly, Strings.DeleteInvoice, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(Strings.MessageInvoiceDeleteConfirm, Strings.DeleteInvoice, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        // Only drafts are deletable; issued invoices remain immutable for auditability.
        var deleted = await _invoiceService.DeleteInvoiceAsync(SelectedInvoice.Id);
        if (!deleted)
        {
            StatusMessage = Strings.MessageInvoiceDeleteFailed;
            MessageBox.Show(Strings.MessageInvoiceDeleteFailed, Strings.DeleteInvoice, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await LoadInvoicesAsync();
        SelectedInvoice = null;
        SelectedInvoiceSummary = Invoices.FirstOrDefault();
        StatusMessage = Strings.MessageInvoiceDeleted;
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (SelectedInvoice == null || !SelectedInvoice.IsIssued)
        {
            StatusMessage = Strings.MessagePdfIssuedOnly;
            MessageBox.Show(StatusMessage, Strings.ExportPdf, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            StatusMessage = Strings.MessageExportingPdf;
			// PDF export reuses stored bytes when available; otherwise regenerates on demand.
            var path = await _pdfExportService.ExportPdfAsync(SelectedInvoice.Id);
            StatusMessage = string.Format(Strings.MessagePdfExported, path);
            MessageBox.Show(StatusMessage, Strings.ExportPdf, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF export failed for invoice {InvoiceId}", SelectedInvoice.Id);
            StatusMessage = $"PDF export failed: {ex.Message}";
            MessageBox.Show(StatusMessage, Strings.ExportPdf, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task SignPdfAsync()
    {
        if (SelectedInvoice == null || !SelectedInvoice.IsIssued)
        {
            StatusMessage = Strings.MessagePdfIssuedOnly;
            MessageBox.Show(StatusMessage, Strings.SignPdf, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            StatusMessage = Strings.MessageSigningPdf;
            var path = await _pdfSigningService.SignIssuedPdfAsync(SelectedInvoice.Id);
            StatusMessage = string.Format(Strings.MessagePdfSigned, path);
            MessageBox.Show(StatusMessage, Strings.SignPdf, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Strings.MessageSigningPdfCancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF signing failed for invoice {InvoiceId}", SelectedInvoice?.Id);
            StatusMessage = string.Format(Strings.MessagePdfSignFailed, ex.Message);
            MessageBox.Show(StatusMessage, Strings.SignPdf, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task BackupDatabaseAsync()
    {
        try
        {
            var defaultDir = _backupService.GetDefaultBackupDirectory();
            Directory.CreateDirectory(defaultDir);
            var fileName = Path.Combine(defaultDir, $"InvoiceDesk-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

            IsBusy = true;
            BusyMessage = Strings.MessageBackupRunning;
            var path = await _backupService.BackupToZipAsync(fileName);
            StatusMessage = string.Format(Strings.MessageBackupSuccess, path);
            MessageBox.Show(StatusMessage, Strings.BackupDatabase, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database backup failed");
            StatusMessage = string.Format(Strings.MessageBackupFailed, ex.Message);
            MessageBox.Show(StatusMessage, Strings.BackupDatabase, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task RestoreDatabaseAsync()
    {
        try
        {
            var defaultDir = _backupService.GetDefaultBackupDirectory();
            if (!Directory.Exists(defaultDir))
            {
                StatusMessage = string.Format(Strings.MessageNoBackupFound, defaultDir);
                MessageBox.Show(StatusMessage, Strings.RestoreDatabase, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var latest = Directory.GetFiles(defaultDir, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (latest == null)
            {
                StatusMessage = string.Format(Strings.MessageNoBackupFound, defaultDir);
                MessageBox.Show(StatusMessage, Strings.RestoreDatabase, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsBusy = true;
            BusyMessage = Strings.MessageRestoreRunning;
            await _backupService.RestoreFromZipAsync(latest);
            await ReloadDataAfterRestoreAsync();
            StatusMessage = string.Format(Strings.MessageRestoreSuccess, latest);
            MessageBox.Show(StatusMessage, Strings.RestoreDatabase, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database restore failed");
            StatusMessage = string.Format(Strings.MessageRestoreFailed, ex.Message);
            MessageBox.Show(StatusMessage, Strings.RestoreDatabase, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    [RelayCommand]
    private void AddLine()
    {
        if (SelectedInvoice == null)
        {
            return;
        }

        SelectedInvoice.Lines.Add(new InvoiceLineViewModel
        {
            VatType = VatType.Domestic,
            Qty = 1,
            UnitPrice = 0,
            TaxRate = 0.2m
        });
        SelectedInvoice.RecalculateTotals();
    }

    [RelayCommand]
    private void RemoveLine(object? line)
    {
        if (SelectedInvoice == null)
        {
            return;
        }

        var vm = line as InvoiceLineViewModel;
        if (vm == null)
        {
            return;
        }

        SelectedInvoice.Lines.Remove(vm);
        SelectedInvoice.RecalculateTotals();
    }

    [RelayCommand]
    private async Task SelectInvoiceAsync(Invoice? invoice)
    {
        if (invoice == null)
        {
            SelectedInvoice = null;
            return;
        }

        await SelectInvoiceAsync(invoice.Id);
    }

    private async Task SelectInvoiceAsync(int invoiceId)
    {
        var detailed = await _invoiceQueryService.GetInvoiceWithLinesAsync(invoiceId);
        if (detailed == null)
        {
            return;
        }

        SelectedInvoice = InvoiceViewModel.FromEntity(detailed);
    }

    private async Task LoadInvoicesAsync()
    {
        var results = await _invoiceQueryService.SearchAsync(SearchText, FromDate, ToDate, SelectedCustomerFilter?.Id);
        Invoices = new ObservableCollection<Invoice>(results);
        RefreshReferenceInvoices();
        if (SelectedInvoice != null)
        {
            SelectedInvoiceSummary = Invoices.FirstOrDefault(i => i.Id == SelectedInvoice.Id); // Preserve selection after refresh when possible.
        }
        else
        {
            SelectedInvoiceSummary = Invoices.FirstOrDefault();
        }
    }

    private async Task LoadCustomersAsync()
    {
        var list = await _customerService.GetCustomersAsync();
        Customers = new ObservableCollection<Customer>(list);
        SelectedCustomerForDraft = Customers.FirstOrDefault();
    }

    private async Task ReloadDataAfterRestoreAsync()
    {
        var allCompanies = await _companyService.GetCompaniesAsync();
        Companies = new ObservableCollection<Company>(allCompanies);
        SelectedCompany = Companies.FirstOrDefault();

        if (SelectedCompany == null)
        {
            Customers.Clear();
            Invoices.Clear();
            SelectedInvoice = null;
            SelectedInvoiceSummary = null;
        }
        else
        {
            await _companyContext.SetCompanyAsync(SelectedCompany.Id);
            await LoadCustomersAsync();
            await LoadInvoicesAsync();
        }
    }

    private void RefreshVatTypes()
    {
        VatTypes = new ObservableCollection<VatTypeOption>(new[]
        {
            new VatTypeOption { Value = VatType.Domestic, Label = Strings.VatTypeDomestic },
            new VatTypeOption { Value = VatType.IntraEuReverseCharge, Label = Strings.VatTypeIntraEuReverseCharge },
            new VatTypeOption { Value = VatType.ExportOutsideEu, Label = Strings.VatTypeExportOutsideEu },
            new VatTypeOption { Value = VatType.VatExempt, Label = Strings.VatTypeExempt }
        });
    }

    private void RefreshDocumentTypes()
    {
        DocumentTypes = new ObservableCollection<DocumentTypeOption>(new[]
        {
            new DocumentTypeOption { Value = InvoiceDocumentType.Invoice, Label = Strings.DocTypeInvoice },
            new DocumentTypeOption { Value = InvoiceDocumentType.DebitNote, Label = Strings.DocTypeDebitNote },
            new DocumentTypeOption { Value = InvoiceDocumentType.CreditNote, Label = Strings.DocTypeCreditNote }
        });
    }

    private void OnSelectedInvoicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InvoiceViewModel.SubTotal)
            or nameof(InvoiceViewModel.TaxTotal)
            or nameof(InvoiceViewModel.Total)
            or nameof(InvoiceViewModel.Currency))
        {
            OnPropertyChanged(nameof(ShouldShowDualCurrency));
            OnPropertyChanged(nameof(SubTotalEur));
            OnPropertyChanged(nameof(TaxTotalEur));
            OnPropertyChanged(nameof(TotalEur));
        }

        if (e.PropertyName is nameof(InvoiceViewModel.RefInvoiceNumber)
            or nameof(InvoiceViewModel.DocumentType))
        {
            RefreshReferenceInvoices();
        }
    }

    private async Task ApplyReferenceInvoiceAsync(Invoice refInvoiceSummary)
    {
        if (SelectedInvoice == null || !SelectedInvoice.IsDraft)
        {
            return;
        }

        _isApplyingReferenceInvoice = true;
        try
        {
            var fullInvoice = await _invoiceQueryService.GetInvoiceWithLinesAsync(refInvoiceSummary.Id);
            if (fullInvoice == null)
            {
                return;
            }

            SelectedInvoice.RefInvoiceNumber = fullInvoice.InvoiceNumber;
            SelectedInvoice.RefInvoiceDate = fullInvoice.IssueDate;
            SelectedInvoice.CustomerId = fullInvoice.CustomerId;
            SelectedInvoice.CustomerNameSnapshot = fullInvoice.CustomerNameSnapshot;
            SelectedInvoice.CustomerAddressSnapshot = fullInvoice.CustomerAddressSnapshot;
            SelectedInvoice.CustomerVatSnapshot = fullInvoice.CustomerVatSnapshot;
            SelectedInvoice.Currency = CurrencyHelper.NormalizeCurrencyOrDefault(fullInvoice.Currency);
            SelectedInvoice.InvoiceLanguage = string.IsNullOrWhiteSpace(fullInvoice.InvoiceLanguage) ? "en" : fullInvoice.InvoiceLanguage;

            SelectedInvoice.Lines.Clear();
            if (fullInvoice.Lines != null)
            {
                foreach (var line in fullInvoice.Lines)
                {
                    SelectedInvoice.Lines.Add(new InvoiceLineViewModel
                    {
                        Description = line.Description,
                        Qty = line.Qty,
                        UnitPrice = line.UnitPrice,
                        TaxRate = line.TaxRate,
                        VatType = line.VatType,
                        LineTotal = line.LineTotal
                    });
                }
            }

            SelectedInvoice.RecalculateTotals();
            SyncSelectedReferenceInvoice();
            StatusMessage = string.Format(Strings.MessageReferenceInvoiceApplied, fullInvoice.InvoiceNumber);
        }
        finally
        {
            _isApplyingReferenceInvoice = false;
        }
    }

    private void RefreshReferenceInvoices()
    {
        var currentInvoiceId = SelectedInvoice?.Id;
        var candidates = Invoices
            .Where(i => i.Id != currentInvoiceId && (i.Status == InvoiceStatus.Issued || (!string.IsNullOrWhiteSpace(i.InvoiceNumber) && !i.InvoiceNumber.StartsWith("DRAFT"))))
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.InvoiceNumber)
            .ToList();

        if (!candidates.Any() && Invoices.Any())
        {
            candidates = Invoices.Where(i => i.Id != currentInvoiceId).OrderByDescending(i => i.IssueDate).ToList();
        }

        ReferenceInvoices = new ObservableCollection<Invoice>(candidates);
        SyncSelectedReferenceInvoice();
    }

    private void SyncSelectedReferenceInvoice()
    {
        if (_isApplyingReferenceInvoice)
        {
            return;
        }

        if (SelectedInvoice != null && SelectedInvoice.RequiresRefInvoice && !string.IsNullOrWhiteSpace(SelectedInvoice.RefInvoiceNumber))
        {
            SelectedReferenceInvoice = ReferenceInvoices.FirstOrDefault(r => r.InvoiceNumber == SelectedInvoice.RefInvoiceNumber);
        }
        else
        {
            SelectedReferenceInvoice = null;
        }
    }
}
