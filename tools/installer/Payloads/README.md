# Offline Dependency Payloads

Place installer packages in this folder to bundle them into `InvoiceDesk-setup.exe` for offline installation without downloading from the internet:

1. **SQL Server Express LocalDB (`SqlLocalDB.msi`)**:
   - Download `SqlLocalDB.msi` (SQL Server LocalDB 2019/2022 x64) from Microsoft.
   - Saves downloading during setup when LocalDB is not present on target machine.

2. **Microsoft Edge WebView2 Evergreen Bootstrapper (`MicrosoftEdgeWebview2Setup.exe`)**:
   - Download `MicrosoftEdgeWebview2Setup.exe` from Microsoft.
   - Used for offscreen HTML-to-PDF invoice rendering.

3. **Microsoft .NET 8.0 Desktop Runtime x64 (`windowsdesktop-runtime-8-win-x64.exe`)**:
   - Download `.NET 8 Desktop Runtime (x64)` installer executable from Microsoft.
   - Required for WPF desktop application execution.

Note: If any of these installers are omitted from this folder, the installer script will automatically download missing prerequisites on-demand during setup.
