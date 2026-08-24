# InvoiceDesk (WPF, .NET 8)

Multi-company invoice manager built with WPF, EF Core (SQL Server), WebView2 PDF export, MVVM, and RESX localization (English/Bulgarian).

## Disclaimer

Course project; not production-hardened. No warranty. Educational use only.

## Stack & Key Bits
- .NET 8 WPF, MVVM via CommunityToolkit.Mvvm
- EF Core 8 (SQL Server) with IDbContextFactory
- WebView2 for HTML→PDF (PrintToPdfAsync)
- RESX localization: Strings.resx (en) and Strings.bg.resx (bg); SatelliteResourceLanguages limited to en;bg
- Authentication/licensing: WordPress plugin backend (`invoicedesk-auth`) with token sessions and device limits

## Architecture Highlights
- Layered WPF client: Views + ViewModels (async commands) over services; `ICompanyContext` scopes queries per company.
- Data access via `AppDbContext`/`IDbContextFactory`; `AppDbInitializer` ensures DB exists, applies migrations, backfills missing invoice numbers, seeds a default company.
- Business logic in Services: `InvoiceService` (draft→issue, totals, immutability), `PdfExportService` (WebView2 export), `PdfSigningService` (KEP/QES signing), `DatabaseBackupService` (compressed .bak to .zip), `InvoiceQueryService` (filters/search).
- Rendering: `InvoiceHtmlRenderer` builds deterministic HTML; WebView2 prints to PDF; issued PDFs are stored on disk and in DB with SHA-256 metadata.
- Helpers: currency dual-display (BGN/EUR), localized strings/converters, file logging with safe append.
- VAT validation: VIES checks run automatically on customer save for supported EU country codes; `IsVatRegistered` and address are updated when data is returned.

## Prerequisites
- .NET 8 SDK
- SQL Server instance reachable with create/alter rights (LocalDB/Express/remote ok)
- Microsoft Edge WebView2 Runtime (Evergreen)

## Configure
1. Copy/update [InvoiceDesk/appsettings.json](InvoiceDesk/appsettings.json): set ConnectionStrings:Default (default now targets LocalDB: `(localdb)\\MSSQLLocalDB;Database=InvoiceDesk;Trusted_Connection=True;MultipleActiveResultSets=true;Connect Timeout=30`).
2. Set `AuthApi:BaseUrl` to your WordPress site REST root (e.g., `https://example.com/wp-json/invoicedesk/v1/`).
3. Deploy/activate the WordPress plugin under `wordpress-plugin/invoicedesk-auth` on the same site (HTTPS required).
4. Ensure the SQL login (LocalDB user) can create/alter the target database; the app will create it if missing.
5. Optional: change Logging:FilePath (defaults to logs/app.log under workspace) and Pdf:OutputDirectory (defaults to exports under workspace).

## Database
```
cd InvoiceDesk
dotnet ef migrations add InitialCreate
dotnet ef database update
```
- Uses migrations (no EnsureCreated). AppDbInitializer creates the database if absent and seeds one default Company when empty.

## Build / Clean / Run
```
cd InvoiceDesk
dotnet build InvoiceDesk.sln
dotnet clean InvoiceDesk.sln   # to clear bin/obj
dotnet run --project InvoiceDesk
```

## Installer (Inno Setup)
The setup installer automatically detects, downloads, and installs missing prerequisites (.NET 8.0 Desktop Runtime, WebView2 Runtime, and SQL Server LocalDB 2022) on clean Windows 10/11 machines, and configures the default `MSSQLLocalDB` instance.

1. Build & compile automatically via PowerShell:
	- `powershell -ExecutionPolicy Bypass -File tools/installer/build-installer.ps1`
	- Optional offline payload bundling: `powershell -ExecutionPolicy Bypass -File tools/installer/build-installer.ps1 -DownloadPayloads`
2. Or build manually:
	- `dotnet publish InvoiceDesk/InvoiceDesk.csproj -c Release -r win-x64 --self-contained false -o InvoiceDesk/bin/Release/net8.0-windows/win-x64/publish`
	- Compile [tools/installer/InvoiceDesk.iss](tools/installer/InvoiceDesk.iss) with Inno Setup Compiler 6+.

## Runtime Behavior
- Multi-company isolation: services use `ICompanyContext` to scope queries.
- Invoice issuing is transactional; numbering per company; issued invoices/PDFs are immutable.
- PDF exports go to InvoiceDesk/exports (relative to workspace) and are also stored in DB as bytes/metadata.
- User culture preference is persisted; UI binds `DataGrid.Language` to the selected culture to avoid validation issues when switching languages.

## Troubleshooting
- Missing WebView2: install Evergreen runtime (x64/ARM as appropriate).
- Binding issues: binding trace is enabled to logs/app.log; check for BindingExpression entries.
- SQL Server permissions: ensure the configured user can create/alter the database and target catalog exists or is creatable.

## Notes for Development
- Logs: `logs/app.log` (file logger + binding trace).
- PDF renderer: [InvoiceDesk/Rendering/InvoiceHtmlRenderer.cs](InvoiceDesk/Rendering/InvoiceHtmlRenderer.cs) (embed logo as data URL; invariant formatting).
- PDF pipeline: [InvoiceDesk/Services/PdfExportService.cs](InvoiceDesk/Services/PdfExportService.cs) (WebView2 headless host, timeouts, diagnostics).
- Domain rules: [InvoiceDesk/Services/InvoiceService.cs](InvoiceDesk/Services/InvoiceService.cs) (draft creation, issuing, totals).
- Localization helper: [InvoiceDesk/Helpers/LocalizedStrings.cs](InvoiceDesk/Helpers/LocalizedStrings.cs) and RESX files in `InvoiceDesk/Resources`.

## Feature Highlights
- Invoice list/search, draft editor, issue/export actions
- Company and customer management windows
- Per-line VAT type selection (domestic, intra-EU reverse charge, export, exempt)
- Runtime culture switching with persisted preference
- Automatic VIES VAT validation on customer save (EU member states and XI only)
- Authentication with token-based sessions, device tracking, and admin revocation (see [docs/AUTH.md](docs/AUTH.md))
