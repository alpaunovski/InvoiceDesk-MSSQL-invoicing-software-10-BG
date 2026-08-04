# InvoiceDesk — Comprehensive Technical Documentation

## Table of Contents
1. [Executive Summary & System Overview](#1-executive-summary--system-overview)
2. [Technology Stack & Key Dependencies](#2-technology-stack--key-dependencies)
3. [Architecture & Design Patterns](#3-architecture--design-patterns)
4. [Application Bootstrapping & Lifecycle](#4-application-bootstrapping--lifecycle)
5. [Authentication & Licensing Stack](#5-authentication--licensing-stack)
   - [Desktop Client Security & Interop](#desktop-client-security--interop)
   - [WordPress Backend Plugin (`invoicedesk-auth`)](#wordpress-backend-plugin-invoicedesk-auth)
6. [Data Layer & Entity Framework Core Architecture](#6-data-layer--entity-framework-core-architecture)
   - [Database Context & Schema Configuration](#database-context--schema-configuration)
   - [Domain Entities & Immutability Snapshots](#domain-entities--immutability-snapshots)
   - [Database Initializer & Auto-Healing](#database-initializer--auto-healing)
7. [Business Logic Services & System Workflows](#7-business-logic-services--system-workflows)
   - [Invoice Processing Engine (`InvoiceService`)](#invoice-processing-engine-invoiceservice)
   - [Multi-Tenant Scoping & Queries (`InvoiceQueryService`, `CompanyContext`)](#multi-tenant-scoping--queries-invoicequeryservice-companycontext)
   - [Customer Management & VIES Integration (`CustomerService`, `ViesClient`)](#customer-management--vies-integration-customerservice-viesclient)
   - [Company Service & Validation Logic (`CompanyService`)](#company-service--validation-logic-companyservice)
   - [Database Backup & Restore Pipeline (`DatabaseBackupService`)](#database-backup--restore-pipeline-databasebackupservice)
8. [PDF Rendering, Export, & Digital Signing Engine](#8-pdf-rendering-export--digital-signing-engine)
   - [HTML Rendering Engine & Dual-Currency (`InvoiceHtmlRenderer`)](#html-rendering-engine--dual-currency-invoicehtmlrenderer)
   - [WebView2 PDF Export Pipeline (`PdfExportService`)](#webview2-pdf-export-pipeline-pdfexportservice)
   - [KEP/QES Smart Card Signing (`PdfSigningService`)](#kepqes-smart-card-signing-pdfsigningservice)
9. [UI Layer, MVVM Integration, & Localization](#9-ui-layer-mvvm-integration--localization)
   - [MVVM Framework & Data Binding](#mvvm-framework--data-binding)
   - [Dynamic Multi-Language Engine (RESX)](#dynamic-multi-language-engine-resx)
10. [Diagnostics, Logging, & Error Recovery](#10-diagnostics-logging--error-recovery)
11. [Configuration Reference & Deployment Guidelines](#11-configuration-reference--deployment-guidelines)

---

## 1. Executive Summary & System Overview

**InvoiceDesk** is an enterprise-grade desktop invoicing application designed for multi-company invoicing, compliance with Bulgarian and EU tax legislation, automatic EU VIES VAT validation, dual BGN/EUR currency compliance, deterministic HTML-to-PDF rendering, and Qualified Electronic Signature (KEP/QES) digital signing via smart cards.

The system comprises two core components:
1. **Desktop Client App (`InvoiceDesk`)**: A Windows desktop application built on **.NET 8 WPF**, using Entity Framework Core 8 with SQL Server, CommunityToolkit.Mvvm, Microsoft Edge WebView2, and iText7 / BouncyCastle.
2. **Authentication & Licensing Backend (`invoicedesk-auth`)**: A custom **WordPress REST API plugin** providing session management, device tracking, multi-session limiting, and user account status enforcement (Active/Suspended/Disabled).

---

## 2. Technology Stack & Key Dependencies

### Desktop Application (.NET 8 WPF)
| Framework / Component | Version / Library | Purpose |
| :--- | :--- | :--- |
| **Runtime** | .NET 8.0 (Windows WPF) | Core runtime environment |
| **ORM** | `Microsoft.EntityFrameworkCore.SqlServer` 8.x | Database persistence & migrations |
| **MVVM Tooling** | `CommunityToolkit.Mvvm` 8.x | Observable properties, RelayCommands, messaging |
| **Dependency Injection** | `Microsoft.Extensions.Hosting` & `DependencyInjection` | Service container, host lifecycle |
| **PDF Generation Engine** | `Microsoft.Web.WebView2` | Chrome-based offscreen HTML-to-PDF rendering |
| **PDF Manipulation & Signatures** | `iText7` & `BouncyCastle.Cryptography` | CAdES-BES PDF digital signatures |
| **HTTP & Web Requests** | `System.Net.Http.Json` | REST communication & SOAP VIES queries |
| **Security Interop** | `Advapi32.dll` (`CredWrite`/`CredRead`) & DPAPI | Windows Credential Manager token storage |

### Licensing Backend (WordPress Plugin)
| Layer | Specification | Details |
| :--- | :--- | :--- |
| **Platform** | WordPress (PHP 7.4+ / 8.x) | Extends `wp-json` REST API namespace |
| **Database** | MySQL / MariaDB via `$wpdb` | Custom table `wp_invoicedesk_sessions` |
| **Security** | HTTPS Enforced, Bearer Tokens, Nonces | 256-bit cryptographically secure token generation |

---

## 3. Architecture & Design Patterns

InvoiceDesk follows clean MVVM architecture with explicit separation of concerns:

```
[ View Layer (WPF XAML) ]
       │  ▲ (Data Binding, Commands, LocalizedStrings)
       ▼  │
[ ViewModel Layer (CommunityToolkit.Mvvm) ]
       │  ▲ (Async Execution, Property Change Notification)
       ▼  │
[ Business Service Layer ] ──► [ External Integrations: VIES SOAP, WP REST Auth ]
       │  ▲
       ▼  │
[ Data Access Layer (EF Core 8 / DbContextFactory) ]
       │  ▲
       ▼  │
[ Storage Layer: SQL Server / WinCred / Local File System ]
```

### Key Architectural Characteristics:
1. **Thread-Safe DbContext Scoping**: Uses `IDbContextFactory<AppDbContext>` across transient services to prevent thread concurrency issues in async WPF operations.
2. **Multi-Tenant Context Scoping**: All invoice, customer, and query operations are strictly scoped per company using `ICompanyContext.CurrentCompanyId`.
3. **Data Snapshot Immutability**: Historical customer information (Name, Address, VAT/EIK) is frozen in snapshot fields when an invoice is issued, ensuring retroactively changed customer details do not modify past tax documents.
4. **Deterministic Document Rendering**: Invoices are rendered via HTML/CSS template to PDF using headless WebView2 printing, guaranteeing pixel-perfect alignment across different printers and machines.

---

## 4. Application Bootstrapping & Lifecycle

The entry point of the desktop application is [InvoiceDesk/App.xaml.cs](InvoiceDesk/App.xaml.cs). Bootstrapping occurs in the following deterministic sequence:

```
App Ctor -> SafeBuildBootstrapConfiguration() -> SafeAttachLogging()
    │
    ▼
OnStartup() -> Build Generic Host (IHost) & Register Services
    │
    ▼
AuthWorkflow.EnsureAuthenticatedAsync()
  ├── Check Local Token (WinCred / DPAPI)
  ├── Call WP REST `/validate`
  └── Prompt LoginWindow if invalid
    │
    ▼
AppDbInitializer.InitializeAsync()
  ├── Ensure Master SQL DB Exists
  ├── Apply EF Core Migrations
  ├── Repair/Backfill Missing Invoice Numbers
  └── Seed Default Company if database empty
    │
    ▼
UserSettingsService.LoadAsync() & LanguageService.SetCultureAsync()
    │
    ▼
Set Initial Company Context (ICompanyContext)
    │
    ▼
Show MainWindow -> Set ShutdownMode = OnMainWindowClose
```

---

## 5. Authentication & Licensing Stack

### Desktop Client Security & Interop

Authentication state is managed by `AuthWorkflow`, `AuthService`, and `TokenStore` ([InvoiceDesk/Services/Auth/](InvoiceDesk/Services/Auth/)):

- **Token Storage Hierarchy**:
  1. **Primary**: Windows Credential Manager (`Advapi32.dll` native interop via `CredWrite`/`CredRead`, target `InvoiceDeskAuthToken`).
  2. **Fallback**: DPAPI-encrypted file located at `%LOCALAPPDATA%\InvoiceDesk\token.dat` (`ProtectedData.Protect` scoped to `DataProtectionScope.CurrentUser`).
- **Session Verification**: At startup, `AuthWorkflow` validates any stored token via `POST /wp-json/invoicedesk/v1/validate`. If invalid or expired, the local credential is deleted and `LoginWindow` is presented.

### WordPress Backend Plugin (`invoicedesk-auth`)

Located in [wordpress-plugin/invoicedesk-auth/invoicedesk-auth.php](wordpress-plugin/invoicedesk-auth/invoicedesk-auth.php):

- **Database Table (`wp_invoicedesk_sessions`)**:
  - `id`: Auto-incrementing primary key.
  - `user_id`: WordPress User ID.
  - `token`: 64-character hex string (32 random bytes).
  - `device_name`: Client machine name (`Environment.MachineName`).
  - `ip_address`: Client IP address.
  - `created_at` / `last_seen` / `expires_at`: Timestamps (default 24h expiration).
  - `is_active`: Session active flag (`1` or `0`).
- **User Meta Keys**:
  - `max_sessions`: Integer limit on active concurrent desktop instances (default `1`).
  - `account_status`: Account access status (`active`, `suspended`, `disabled`).
- **REST API Routes**:
  - `POST /invoicedesk/v1/login`: Validates credentials, checks account status, prunes oldest session if `max_sessions` is reached, issues bearer token.
  - `POST /invoicedesk/v1/validate`: Validates bearer token and refreshes `last_seen`.
  - `POST /invoicedesk/v1/logout`: Revokes token session.
  - `POST /invoicedesk/v1/sessions/list` & `/revoke`: Admin endpoints for session management.

---

## 6. Data Layer & Entity Framework Core Architecture

### Database Context & Schema Configuration

Data access is managed by `AppDbContext` ([InvoiceDesk/Data/AppDbContext.cs](InvoiceDesk/Data/AppDbContext.cs)):

- **Database Engine**: Microsoft SQL Server (supports LocalDB, Express, Standard, Enterprise).
- **Entity Configurations**:
  - `Company`: Primary business entity. Holds bank details, EIK, VAT, invoice prefix, and `NextInvoiceNumber` sequence counter.
  - `Customer`: Client profiles scoped to a company via `CompanyId`. Indexed on `(CompanyId, Name)`.
  - `Invoice`: Financial records scoped to a company via `CompanyId`. Contains `DocumentType` (Invoice, Debit Note, Credit Note), status, totals, snapshot fields, and stored PDF blobs. Unique index on `(CompanyId, InvoiceNumber)`.
  - `InvoiceLine`: Individual line items linked to an invoice via `InvoiceId`. Precision defined as `(18, 3)` for quantity and `(18, 2)` for amounts.

### Domain Entities & Immutability Snapshots

```
                  ┌──────────────────┐
                  │     Company      │
                  └────────┬─────────┘
                           │ 1
                           │
                           │ *
                  ┌────────┴─────────┐
                  │     Customer     │
                  └────────┬─────────┘
                           │ 1
                           │
                           │ *
                  ┌────────┴─────────┐
                  │     Invoice      │
                  │ (Snapshots Host) │
                  └────────┬─────────┘
                           │ 1
                           │
                           │ *
                  ┌────────┴─────────┐
                  │   InvoiceLine    │
                  └──────────────────┘
```

#### Snapshot Mechanism
When an invoice is issued, `InvoiceService` captures:
- `CustomerNameSnapshot` = `Customer.Name`
- `CustomerAddressSnapshot` = `Customer.Address`
- `CustomerVatSnapshot` = `Customer.VatNumber` or `Customer.Eik`

These snapshots are permanently rendered on PDF exports, guaranteeing historical legal immutability regardless of subsequent edits to the customer profile.

### Database Initializer & Auto-Healing

`AppDbInitializer` ([InvoiceDesk/Data/AppDbInitializer.cs](InvoiceDesk/Data/AppDbInitializer.cs)) handles database creation and startup auto-healing:
1. **Database Creation**: Connects to `master` to execute `CREATE DATABASE [{db}]` if absent.
2. **Migrations**: Executes `db.Database.MigrateAsync()`.
3. **Data Backfilling**: Scans for invoices missing numbers and assigns `DRAFTFIX-{companyId}-{invoiceId}-{timestamp}` to satisfy unique index constraints.
4. **Company Seeding**: Seeds a default company if no companies exist.

---

## 7. Business Logic Services & System Workflows

### Invoice Processing Engine (`InvoiceService`)

Implemented in [InvoiceDesk/Services/InvoiceService.cs](InvoiceDesk/Services/InvoiceService.cs):

1. **Draft Creation (`CreateDraftAsync`)**:
   - Generates temporary draft number `DRAFT-{timestamp}`.
   - Captures current UI culture language and default currency (`BGN`).
   - Copies customer snapshot data.
2. **Draft Saving (`SaveInvoiceAsync`)**:
   - Validates that the invoice is still in `Draft` status (`IssuedAtUtc == null`).
   - Updates header details, lines, and recalculates line totals (`UnitPrice * Qty + VAT`).
3. **Atomic Issuance (`IssueInvoiceAsync`)**:
   - Uses EF Core `CreateExecutionStrategy()` to retry on SQL transient errors.
   - Opens a `Serializable` transaction to eliminate race conditions in invoice number allocation.
   - Increments `Company.NextInvoiceNumber` and builds final invoice number: `{Prefix}{NextInvoiceNumber}`.
   - Marks status as `Issued`, sets `IssuedAtUtc = DateTime.UtcNow`.
   - Commits transaction and triggers automatic PDF rendering & database caching via `PdfExportService`.

### Multi-Tenant Scoping & Queries (`InvoiceQueryService`, `CompanyContext`)

- `CompanyContext` maintains the application-wide active company context (`CurrentCompanyId`).
- `InvoiceQueryService` enforces `.Where(i => i.CompanyId == _companyContext.CurrentCompanyId)` on all queries, preventing cross-tenant data leaks.
- Supports filtering by full-text search string, date ranges (`from`/`to`), and specific customer IDs.

### Customer Management & VIES Integration (`CustomerService`, `ViesClient`)

- **VIES Validation Client ([InvoiceDesk/Services/ViesClient.cs](InvoiceDesk/Services/ViesClient.cs))**:
  - Communicates directly with the official EU VIES SOAP service (`https://ec.europa.eu/taxation_customs/vies/services/checkVatService`).
  - Supports 28 EU member state country codes (`AT`, `BE`, `BG`, `DE`, `FR`, etc.).
  - Builds SOAP XML envelope, posts request, and parses `checkVatResponse` XML.
- **Auto Validation Workflow**:
  - On saving a customer, if `CountryCode` and `VatNumber` belong to a supported EU state, `CustomerService` queries VIES.
  - On valid response: sets `IsVatRegistered = true` and auto-populates missing address data.

### Company Service & Validation Logic (`CompanyService`)

Enforces Bulgarian legal validation rules for registered companies:
- Bulgarian companies (`CountryCode == "BG"`) must provide a valid **EIK/BULSTAT** number.
- EIK must consist exclusively of digits and be exactly 9 or 13 characters long.
- Safe deletion guards prevent deleting companies that have issued invoices.

### Database Backup & Restore Pipeline (`DatabaseBackupService`)

Implemented in [InvoiceDesk/Services/DatabaseBackupService.cs](InvoiceDesk/Services/DatabaseBackupService.cs):
- **Backup (`BackupToZipAsync`)**: Executes T-SQL `BACKUP DATABASE [{db}] TO DISK=@path WITH INIT, COPY_ONLY, FORMAT, COMPRESSION` on the SQL Server instance, compresses the resulting `.bak` into a `.zip` archive, and cleans up temporary files.
- **Restore (`RestoreFromZipAsync`)**:
  1. Extracts `.bak` from `.zip`.
  2. Inspects backup header using `RESTORE HEADERONLY` to verify database target name matching.
  3. Sets target database to `SINGLE_USER WITH ROLLBACK IMMEDIATE` to terminate existing active connections.
  4. Executes `RESTORE DATABASE [{db}] FROM DISK=@path WITH REPLACE, RECOVERY`.
  5. Resets database to `MULTI_USER`.

---

## 8. PDF Rendering, Export, & Digital Signing Engine

### HTML Rendering Engine & Dual-Currency (`InvoiceHtmlRenderer`)

[InvoiceDesk/Rendering/InvoiceHtmlRenderer.cs](InvoiceDesk/Rendering/InvoiceHtmlRenderer.cs) generates strict HTML5/CSS print layouts formatted for A4 paper:
- **Dual-Currency Legal Compliance**:
  - Centralized in `CurrencyHelper.cs`.
  - Conversion rate is hardcoded to Bulgarian legal specification: **`1 EUR = 1.95583 BGN`**.
  - When primary currency is `BGN` and dual currency display is enabled, secondary EUR amounts are computed and displayed alongside a mandatory legal note: *"Сумите в евро са изчислени по фиксиран курс 1 EUR = 1.95583 лв."*.
- **VAT Aggregation Summaries**: Groups tax bases and totals per `VatType` (Domestic, Intra-EU Reverse Charge, Export outside EU, VAT Exempt) to provide required legal tax breakdowns.
- **Company Logo**: Encodes local logo files as Base64 Data URLs (`data:image/png;base64,...`) for inline self-contained rendering.

### WebView2 PDF Export Pipeline (`PdfExportService`)

[InvoiceDesk/Services/PdfExportService.cs](InvoiceDesk/Services/PdfExportService.cs):
1. Instantiates a hidden WPF `Window` containing an offscreen `WebView2` control.
2. Initializes `CoreWebView2Environment` targeting local user data folder `%LOCALAPPDATA%\InvoiceDesk\WebView2`.
3. Navigates `WebView2` to the in-memory rendered HTML string via `NavigateToString()`.
4. Executes `PrintToPdfAsync()` with A4 dimensions (`8.27 x 11.69` inches) and zero margins.
5. Computes SHA-256 checksum of generated PDF bytes (`HashHelper.ComputeSha256`).
6. Saves PDF byte array, file name, creation timestamp, and SHA-256 hash into the `Invoice` entity in SQL Server.

### KEP/QES Smart Card Signing (`PdfSigningService`)

Implemented in [InvoiceDesk/Services/PdfSigningService.cs](InvoiceDesk/Services/PdfSigningService.cs) using **iText7** and **BouncyCastle**:

```
Issued PDF -> Select Windows Certificate (Smart Card / Token)
   │
   ▼
X509Store Filter (Has Private Key, Key Usage: DigitalSignature/NonRepudiation, DocSign EKU)
   │
   ▼
iText7 PdfSigner (Append Mode -> CERTIFIED_NO_CHANGES_ALLOWED)
   │
   ▼
CAdES-BES Detached Signature Generation
   │
   ▼
Write `*-signed.pdf` & Persist Signed Bytes + SHA-256 Hash to SQL Database
```

---

## 9. UI Layer, MVVM Integration, & Localization

### MVVM Framework & Data Binding

Driven by `CommunityToolkit.Mvvm`:
- **ViewModels**: `MainViewModel`, `InvoiceViewModel`, `InvoiceLineViewModel`, `CompanyManagementViewModel`, `CustomerManagementViewModel`, `LoginViewModel`.
- **Observable Properties**: Auto-generated via `[ObservableProperty]`.
- **Commands**: Commands generated via `[RelayCommand]` handle asynchronous UI operations with `IsBusy` visual feedback states.

### Dynamic Multi-Language Engine (RESX)

- **Resource Files**: Located in `Resources/Strings.resx` (English default) and `Resources/Strings.bg.resx` (Bulgarian).
- **Binding Engine**: `LocalizedStrings` helper exposed via WPF Application resources as `{Binding Source={StaticResource Loc}, Path=Strings.KeyName}`.
- **Language Switcher**: `LanguageService.SetCultureAsync(cultureCode)` updates `CultureInfo.CurrentCulture`, `CultureInfo.CurrentUICulture`, and `Strings.Culture`, then triggers `PropertyChanged` events to instantaneously refresh UI text without restarting the application.

---

## 10. Diagnostics, Logging, & Error Recovery

InvoiceDesk features multi-tier logging implemented via `FileLoggerProvider` ([InvoiceDesk/Helpers/FileLogger.cs](InvoiceDesk/Helpers/FileLogger.cs)):

1. **Bootstrap Logging**: Early initialization errors prior to DI container setup are written directly to `logs/app.log` or fallback log `app-fallback.log`.
2. **Runtime File Logger**: High-performance thread-safe file logging writing to `%WORKSPACE%/logs/app.log`.
3. **Global Exception Traps** in `App.xaml.cs`:
   - `AppDomain.CurrentDomain.UnhandledException`: Logs fatal unhandled app domain exceptions.
   - `DispatcherUnhandledException`: Captures WPF UI thread exceptions, logs them, and displays an error dialog without crashing the application.
   - `TaskScheduler.UnobservedTaskException`: Intercepts unobserved async task failures.

---

## 11. Configuration Reference & Deployment Guidelines

### `appsettings.json` Schema

```json
{
  "ConnectionStrings": {
    "Default": "Server=(localdb)\\MSSQLLocalDB;Database=InvoiceDeskDb;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Culture": "en",
  "Pdf": {
    "OutputDirectory": "exports"
  },
  "Logging": {
    "FilePath": "logs/app.log",
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "AuthApi": {
    "BaseUrl": "https://your-wordpress-site.com/wp-json/invoicedesk/v1/",
    "RequestTimeoutSeconds": 20
  },
  "CurrencyDisplay": {
    "DualCurrencyEnabled": true,
    "EurOnlyMode": false
  }
}
```

### Prerequisites for Production Deployment
1. **Operating System**: Windows 10 / 11 or Windows Server 2019+ (x64).
2. **Runtime Dependencies**:
   - .NET 8.0 Desktop Runtime.
   - Microsoft Edge WebView2 Evergreen Runtime.
3. **Database**: Microsoft SQL Server 2016+ (LocalDB, Express, or Full Instance).
4. **Smart Card Drivers**: PKCS#11 / CSP drivers installed for KEP/QES certificate tokens (if digital signing is used).
