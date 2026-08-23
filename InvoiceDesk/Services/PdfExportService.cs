using System.IO;
using System.Windows;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Controls;
using System.Globalization;
using InvoiceDesk.Data;
using InvoiceDesk.Helpers;
using InvoiceDesk.Models;
using InvoiceDesk.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace InvoiceDesk.Services;

/// <summary>
/// Generates issued-invoice PDFs using WebView2 and persists the binary to the database for reuse.
/// </summary>
public class PdfExportService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly InvoiceHtmlRenderer _renderer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PdfExportService> _logger;

    public PdfExportService(IDbContextFactory<AppDbContext> dbFactory, InvoiceHtmlRenderer renderer, IConfiguration configuration, ILogger<PdfExportService> logger)
    {
        _dbFactory = dbFactory;
        _renderer = renderer;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Generates (or rehydrates) the PDF for an issued invoice, writing to disk and storing bytes in the DB.
    /// </summary>
    public async Task<string> ExportPdfAsync(int invoiceId, string? targetPath = null, bool regenerate = false, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var invoice = await db.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Company)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
        {
            throw new InvalidOperationException("Invoice not found");
        }

        if (invoice.IssuedAtUtc == null)
        {
            throw new InvalidOperationException("Only issued invoices can be exported");
        }

        _logger.LogInformation("Export PDF requested for invoice {InvoiceId}", invoiceId);

        // Resolve output path (allow override), ensure directory exists.
        var outputDir = targetPath != null ? Path.GetDirectoryName(targetPath)! : GetOutputDirectory();
        Directory.CreateDirectory(outputDir);

        var fileName = invoice.IssuedPdfFileName ?? $"Invoice-{invoice.InvoiceNumber}.pdf";
        var outputPath = targetPath ?? Path.Combine(outputDir, fileName);

        if (invoice.IssuedPdf != null && !regenerate)
        {
            // Reuse stored bytes instead of regenerating when available.
            await File.WriteAllBytesAsync(outputPath, invoice.IssuedPdf, cancellationToken);
            return outputPath;
        }

        try
        {
            var invoiceCulture = new CultureInfo(string.IsNullOrWhiteSpace(invoice.InvoiceLanguage) ? "en" : invoice.InvoiceLanguage);
            var html = _renderer.RenderHtml(invoice.Company!, invoice, invoice.Lines.ToList(), invoiceCulture);
            var bytes = await GeneratePdfBytesAsync(html, outputPath, cancellationToken);

            invoice.IssuedPdf = bytes;
            invoice.IssuedPdfFileName = fileName;
            invoice.IssuedPdfSha256 = HashHelper.ComputeSha256(bytes);
            invoice.IssuedPdfCreatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Exported PDF for invoice {InvoiceId} to {Path}", invoiceId, outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export PDF failed for invoice {InvoiceId}", invoiceId);
            throw;
        }
    }

    /// <summary>
    /// Regenerates the issued PDF and stores it; used right after issuing to guarantee a binary copy exists.
    /// </summary>
    public Task<string> GenerateAndStoreIssuedPdfAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        return ExportPdfAsync(invoiceId, null, true, cancellationToken);
    }

    private string GetOutputDirectory()
    {
        var configured = _configuration.GetSection("Pdf")?["OutputDirectory"];
        var targetDir = !string.IsNullOrWhiteSpace(configured) ? configured : "Exports";
        return AppPaths.ResolvePath(targetDir);
    }

    /// <summary>
    /// Renders HTML to PDF via a hidden WebView2 control, enforcing timeouts to avoid UI hangs.
    /// </summary>
    private async Task<byte[]> GeneratePdfBytesAsync(string html, string filePath, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher == null)
        {
            throw new InvalidOperationException("No WPF dispatcher available");
        }

        return await dispatcher.InvokeAsync(async () =>
        {
            _logger.LogInformation("PDF export: initializing WebView2");
            var diag = BuildWebView2Diagnostics();
            var availableVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(availableVersion))
            {
                var message = $"WebView2 runtime not available. Install the Microsoft Edge WebView2 runtime for this architecture. Diagnostics: {diag}";
                _logger.LogError(message);
                throw new InvalidOperationException(message);
            }

            _logger.LogInformation("PDF export: WebView2 runtime version {Version}. {Diag}", availableVersion, diag);
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvoiceDesk", "WebView2");
            Directory.CreateDirectory(userData);
            CoreWebView2Environment? environment;
            try
            {
                // Keep a dedicated user data folder so the runtime can initialize in a service-like context.
                environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF export: failed to create WebView2 environment (userData: {UserData})", userData);
                throw;
            }

			// Hidden host window to give WebView2 a dispatcher/visual root without flashing UI.
            var hostWindow = new Window
            {
                Width = 1,
                Height = 1,
                Visibility = Visibility.Hidden,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Content = new Grid()
            };

            var webView = new WebView2
            {
                Visibility = Visibility.Collapsed,
                Width = 1240,
                Height = 1754
            };

            ((Grid)hostWindow.Content!).Children.Add(webView);
            hostWindow.Show();
            webView.CoreWebView2InitializationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    _logger.LogInformation("PDF export: CoreWebView2InitializationCompleted success");
                }
                else
                {
                    _logger.LogError(args.InitializationException, "PDF export: CoreWebView2InitializationCompleted failed");
                }
            };

            _logger.LogInformation("PDF export: calling EnsureCoreWebView2Async");
            try
            {
                // Bound initialization to 10s to avoid hanging if runtime is unhealthy.
                var ensureCoreTask = webView.EnsureCoreWebView2Async(environment);
                var ensureCompleted = await Task.WhenAny(ensureCoreTask, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
                if (ensureCompleted != ensureCoreTask)
                {
                    throw new TimeoutException("PDF export: WebView2 initialization timed out after 10s");
                }
                await ensureCoreTask.WaitAsync(cancellationToken);
                _logger.LogInformation("PDF export: WebView2 ready (userData: {UserData})", userData);
            }
            catch (Exception ex)
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                _logger.LogError(ex, "PDF export: EnsureCoreWebView2Async failed (available runtime {Version})", version);
                throw;
            }

            if (webView.CoreWebView2 == null)
            {
                var state = $"CoreWebView2 null after EnsureCoreWebView2Async. Environment: {environment?.BrowserVersionString}; userData: {userData}";
                _logger.LogError("PDF export: {State}", state);
                throw new InvalidOperationException(state);
            }
            // Track navigation so we only print once HTML is fully loaded.
            var navigationCompleted = new TaskCompletionSource<bool>();
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult(true);
                }
                else
                {
                    navigationCompleted.TrySetException(new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}"));
                }
            };

            webView.NavigateToString(html);
            _logger.LogInformation("PDF export: waiting for HTML navigation");
            var navTask = navigationCompleted.Task;
            // Guard navigation with a timeout to fail fast on broken HTML/runtime.
            var navCompleted = await Task.WhenAny(navTask, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
            if (navCompleted != navTask)
            {
                throw new TimeoutException("PDF export navigation timed out after 10s");
            }
            await navTask.WaitAsync(cancellationToken);
            _logger.LogInformation("PDF export: navigation complete");

            var settings = webView.CoreWebView2.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintSelectionOnly = false;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.MarginBottom = 0.5;
            settings.MarginTop = 0.5;
            settings.MarginLeft = 0.5;
            settings.MarginRight = 0.5;

            _logger.LogInformation("PDF export: printing to {Path}", filePath);
            var printTask = webView.CoreWebView2.PrintToPdfAsync(filePath, settings);
            // Print can hang if WebView2 crashes; enforce a 10s ceiling.
            var printCompleted = await Task.WhenAny(printTask, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
            if (printCompleted != printTask)
            {
                throw new TimeoutException("PDF export print timed out after 10s");
            }
            await printTask.WaitAsync(cancellationToken);
            _logger.LogInformation("PDF export: print complete");

            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var exists = File.Exists(filePath);
            _logger.LogInformation("PDF export: read back {Length} bytes (exists on disk: {Exists})", bytes.Length, exists);
            hostWindow.Close();
            return bytes;
        }).Task.Unwrap();
    }

    private static string BuildWebView2Diagnostics()
    {
        var procArch = RuntimeInformation.ProcessArchitecture;
        var osArch = RuntimeInformation.OSArchitecture;
        return $"ProcessArch={procArch}, OSArch={osArch}, Is64BitProcess={Environment.Is64BitProcess}";
    }
}
