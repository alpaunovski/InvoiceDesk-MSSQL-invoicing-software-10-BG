using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using InvoiceDesk.Data;
using InvoiceDesk.Helpers;
using InvoiceDesk.Rendering;
using InvoiceDesk.Models;
using InvoiceDesk.Services.Auth;
using InvoiceDesk.Services;
using InvoiceDesk.ViewModels;
using InvoiceDesk.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceDesk;

/// <summary>
/// WPF application bootstrapper: wires DI host, logging, culture, and company context before showing UI.
/// </summary>
public partial class App : Application
{
	private IHost? _host;
	private IConfigurationRoot _bootstrapConfig;
	private string? _logPath;
	private bool _loggingAttached;
	private const string FallbackLogFileName = "app-fallback.log";

	public App()
	{
		// Bootstrap config and file logging before anything else so early failures are captured.
		_bootstrapConfig = SafeBuildBootstrapConfiguration();
		_logPath = SafeResolveLogPath(_bootstrapConfig);
		SafeAttachLogging(_logPath);
		SafeAppend(_logPath, $"INFO {DateTime.UtcNow:u} App ctor initialized logging at {_logPath}\n");
	}

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		ILogger<App>? logger = null;
		_bootstrapConfig ??= SafeBuildBootstrapConfiguration();
		_logPath ??= SafeResolveLogPath(_bootstrapConfig);
		SafeAttachLogging(_logPath);
		var bootstrapConfig = _bootstrapConfig;
		var logPath = _logPath;
		try
		{
			SafeAppend(logPath, $"INFO {DateTime.UtcNow:u} Starting InvoiceDesk host build (log at {logPath})\n");
			_host = CreateHostBuilder(bootstrapConfig).Build();
			logger = _host.Services.GetRequiredService<ILogger<App>>();
			logger.LogInformation("Host built");
			await _host.StartAsync();
			logger.LogInformation("Host started");

			var authWorkflow = _host.Services.GetRequiredService<AuthWorkflow>();
			var authenticated = await authWorkflow.EnsureAuthenticatedAsync();
			if (!authenticated)
			{
				logger.LogWarning("Authentication failed or was cancelled");
				Shutdown();
				return;
			}

			Resources["Loc"] = _host.Services.GetRequiredService<LocalizedStrings>();

			// Database and seed data must exist before any UI is shown.
			var initializer = _host.Services.GetRequiredService<AppDbInitializer>();
			await initializer.InitializeAsync();
			logger.LogInformation("Database initialized");

			var settingsService = _host.Services.GetRequiredService<UserSettingsService>();
			var settings = await settingsService.LoadAsync();
			if (settings.WindowWidth <= 0)
			{
				settings.WindowWidth = 1200;
			}
			if (settings.WindowHeight <= 0)
			{
				settings.WindowHeight = 800;
			}
			logger.LogInformation("Settings loaded: culture {Culture} company {CompanyId}", settings.Culture, settings.CompanyId);

			var languageService = _host.Services.GetRequiredService<ILanguageService>();
			await languageService.SetCultureAsync(settings.Culture);
			logger.LogInformation("Culture set to {Culture}", settings.Culture);

			// Establish the active company context before loading the main window.
			var companyService = _host.Services.GetRequiredService<CompanyService>();
			var companyContext = _host.Services.GetRequiredService<ICompanyContext>();
			var companies = await companyService.GetCompaniesAsync();
			var defaultCompany = companies.FirstOrDefault(c => c.Id == settings.CompanyId) ?? companies.FirstOrDefault();
			if (defaultCompany != null)
			{
				await companyContext.SetCompanyAsync(defaultCompany.Id);
				settings.CompanyId = defaultCompany.Id;
				await settingsService.SaveAsync(settings);
				logger.LogInformation("Company context set to {CompanyId}", defaultCompany.Id);
			}
			else
			{
				logger.LogWarning("No companies available to set as context");
			}

			var mainWindow = _host.Services.GetRequiredService<MainWindow>();
			if (settings.WindowWidth > 0)
			{
				mainWindow.Width = settings.WindowWidth;
			}
			if (settings.WindowHeight > 0)
			{
				mainWindow.Height = settings.WindowHeight;
			}
			mainWindow.Closing += async (_, _) =>
			{
				var s = await settingsService.LoadAsync();
				s.WindowWidth = mainWindow.Width;
				s.WindowHeight = mainWindow.Height;
				await settingsService.SaveAsync(s);
			};
			logger.LogInformation("Showing MainWindow");
			// Restore normal shutdown behavior now that the main window is ready (login used explicit shutdown).
			ShutdownMode = ShutdownMode.OnMainWindowClose;
			mainWindow.Show();
		}
		catch (Exception ex)
		{
			// Last-chance fallback so startup failures are visible to the user and preserved on disk.
			logger?.LogCritical(ex, "Application failed to start");
			SafeAppend(logPath, $"CRITICAL {DateTime.UtcNow:u} {ex}\n");
			MessageBox.Show(ex.ToString(), "InvoiceDesk startup error", MessageBoxButton.OK, MessageBoxImage.Error);
			Shutdown();
		}
	}

	protected override async void OnExit(ExitEventArgs e)
	{
		if (_host != null)
		{
			var logger = _host.Services.GetService<ILogger<App>>();
			logger?.LogInformation("Stopping host");
			await _host.StopAsync();
			_host.Dispose();
		}
		base.OnExit(e);
	}

	private static IHostBuilder CreateHostBuilder(IConfiguration bootstrapConfig) => Host.CreateDefaultBuilder()
		.ConfigureAppConfiguration((context, config) =>
		{
			config.SetBasePath(AppContext.BaseDirectory);
			config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
		})
		.ConfigureLogging((context, logging) =>
		{
			logging.ClearProviders();
			logging.AddSimpleConsole(options =>
			{
				options.SingleLine = true;
				options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
			});
			logging.AddProvider(new FileLoggerProvider(GetLogPath(context.Configuration ?? bootstrapConfig)));
			logging.SetMinimumLevel(LogLevel.Information);
		})
		.ConfigureServices((context, services) =>
		{
			var connectionString = context.Configuration.GetConnectionString("Default")
								   ?? throw new InvalidOperationException("Missing connection string");

			services.Configure<AuthOptions>(context.Configuration.GetSection("AuthApi"));
			services.AddSingleton<ITokenStore, TokenStore>();
			services.AddHttpClient<IAuthService, AuthService>();
			services.AddSingleton<AuthWorkflow>();
			services.AddTransient<LoginViewModel>();
			services.AddTransient<LoginWindow>();

			services.AddSingleton<LocalizedStrings>();
			services.Configure<CurrencyDisplayOptions>(context.Configuration.GetSection("CurrencyDisplay"));
			services.AddSingleton(sp => sp.GetRequiredService<IOptions<CurrencyDisplayOptions>>().Value);
			services.AddSingleton<UserSettingsService>();
			services.AddSingleton<ILanguageService, LanguageService>();
			services.AddSingleton<ICompanyContext, CompanyContext>();
			services.AddHttpClient<IViesClient, ViesClient>(client =>
			{
				client.Timeout = TimeSpan.FromSeconds(20);
			});

			services.AddDbContextFactory<AppDbContext>(options =>
			{
				options.UseSqlServer(connectionString, sql =>
				{
					sql.EnableRetryOnFailure();
					sql.CommandTimeout(60);
				});
				options.EnableDetailedErrors();
			});

			services.AddSingleton<AppDbInitializer>();
			services.AddTransient<CompanyService>();
			services.AddTransient<CustomerService>();
			services.AddTransient<InvoiceQueryService>();
			services.AddTransient<InvoiceService>();
			services.AddTransient<PdfExportService>();
			services.AddTransient<PdfSigningService>();
			services.AddTransient<DatabaseBackupService>();
			services.AddSingleton<InvoiceHtmlRenderer>();

			services.AddTransient<MainViewModel>();
			services.AddTransient<MainWindow>();
			services.AddTransient<CompanyManagementViewModel>();
			services.AddTransient<CustomerManagementViewModel>();
			services.AddTransient<CompanyManagementWindow>();
			services.AddTransient<CustomerManagementWindow>();
		});

	private static string GetLogPath(IConfiguration? configuration = null)
	{
		var configuredPath = configuration?["Logging:FilePath"];
		var targetPath = !string.IsNullOrWhiteSpace(configuredPath) ? configuredPath : "logs/app.log";
		return AppPaths.ResolvePath(targetPath);
	}

	private void AttachGlobalExceptionLogging(string logPath)
	{
		if (_loggingAttached)
		{
			return;
		}

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			try
			{
				var msg = args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString());
				SafeAppend(logPath, $"UNHANDLED {DateTime.UtcNow:u} {msg}\n");
			}
			catch
			{
				// Never throw from global logging hooks; we are already in an error path.
			}
		};

		// Capture UI thread exceptions so users see a dialog instead of silent crashes.
		DispatcherUnhandledException += (_, args) =>
		{
			try
			{
				SafeAppend(logPath, $"DISPATCHER {DateTime.UtcNow:u} {args.Exception}\n");
				args.Handled = true;
				MessageBox.Show(args.Exception.ToString(), "Unhandled UI error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			catch
			{
				// Never throw from global logging hooks; we are already in an error path.
			}
		};

		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			try
			{
				SafeAppend(logPath, $"UNOBSERVED_TASK {DateTime.UtcNow:u} {args.Exception}\n");
				args.SetObserved();
			}
			catch
			{
				// Never throw from global logging hooks; we are already in an error path.
			}
		};

		AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
		{
			try
			{
				SafeAppend(logPath, $"FIRST_CHANCE {DateTime.UtcNow:u} {args.Exception.GetType().FullName}: {args.Exception.Message}\n");
			}
			catch
			{
				// Never throw from global logging hooks; we are already in an error path.
			}
		};

		_loggingAttached = true;
	}

	private static void EnableBindingTrace(string logPath)
	{
		try
		{
			var listener = new TextWriterTraceListener(File.Open(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), "BindingTrace")
			{
				TraceOutputOptions = TraceOptions.DateTime
			};
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			PresentationTraceSources.Refresh();
		}
		catch
		{
			// Do not block startup if binding trace setup fails.
		}
	}

	private IConfigurationRoot SafeBuildBootstrapConfiguration()
	{
		try
		{
			return BuildBootstrapConfiguration();
		}
		catch (Exception ex)
		{
			var fallback = SafeResolveLogPath(null);
			SafeAppend(fallback, $"BOOTSTRAP_CONFIG_FAIL {DateTime.UtcNow:u} {ex}\n");
			return new ConfigurationBuilder().Build();
		}
	}

	private string SafeResolveLogPath(IConfiguration? configuration)
	{
		try
		{
			return GetLogPath(configuration);
		}
		catch (Exception ex)
		{
			var fallback = Path.Combine(Path.GetTempPath(), "InvoiceDesk", FallbackLogFileName);
			SafeAppend(fallback, $"LOGPATH_FAIL {DateTime.UtcNow:u} {ex}\n");
			return fallback;
		}
	}

	private void SafeAttachLogging(string logPath)
	{
		try
		{
			AttachGlobalExceptionLogging(logPath);
			EnableBindingTrace(logPath);
		}
		catch (Exception ex)
		{
			SafeAppend(logPath, $"ATTACH_FAIL {DateTime.UtcNow:u} {ex}\n");
		}
	}

	private static void SafeAppend(string? path, string message)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		try
		{
			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}
			using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
			using var writer = new StreamWriter(stream);
			writer.Write(message);
			writer.Flush();
		}
		catch
		{
			// Best-effort logging only; never throw from the fallback logger path.
		}
	}

	private static IConfigurationRoot BuildBootstrapConfiguration() => new ConfigurationBuilder()
		.SetBasePath(AppContext.BaseDirectory)
		.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
		.Build();
}

