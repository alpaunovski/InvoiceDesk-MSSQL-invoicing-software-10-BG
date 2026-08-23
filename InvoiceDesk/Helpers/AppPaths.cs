using System;
using System.IO;

namespace InvoiceDesk.Helpers;

public static class AppPaths
{
    private static string? _appRoot;

    public static string GetAppRoot()
    {
        if (_appRoot != null)
        {
            return _appRoot;
        }

        var baseDir = AppContext.BaseDirectory;
        var devCandidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));

        // Check if running in dev environment (e.g., bin/Debug/net8.0-windows where project file or sln exists 3 levels up)
        if (File.Exists(Path.Combine(devCandidate, "InvoiceDesk.csproj")) ||
            File.Exists(Path.Combine(devCandidate, "InvoiceDesk.sln")) ||
            Directory.Exists(Path.Combine(devCandidate, "InvoiceDesk")))
        {
            _appRoot = devCandidate;
        }
        else
        {
            // Published, standalone executable, or installed application mode.
            _appRoot = baseDir;
        }

        return _appRoot;
    }

    public static string ResolvePath(string relativeOrAbsolutePath)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
        {
            return GetAppRoot();
        }

        var expanded = Environment.ExpandEnvironmentVariables(relativeOrAbsolutePath);
        if (Path.IsPathRooted(expanded))
        {
            return expanded;
        }

        var primaryPath = Path.GetFullPath(Path.Combine(GetAppRoot(), expanded));
        var primaryDirectory = Path.GetDirectoryName(primaryPath);

        if (!string.IsNullOrWhiteSpace(primaryDirectory))
        {
            try
            {
                Directory.CreateDirectory(primaryDirectory);
                return primaryPath;
            }
            catch (UnauthorizedAccessException)
            {
                var localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvoiceDesk");
                var fallbackPath = Path.GetFullPath(Path.Combine(localAppData, expanded));
                var fallbackDirectory = Path.GetDirectoryName(fallbackPath);
                if (!string.IsNullOrWhiteSpace(fallbackDirectory))
                {
                    Directory.CreateDirectory(fallbackDirectory);
                }
                return fallbackPath;
            }
        }

        return primaryPath;
    }
}
