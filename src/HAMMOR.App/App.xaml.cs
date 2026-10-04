using System.IO;
using System.Windows;
using System.Windows.Threading;
using HAMMOR.App.Localization;
using HAMMOR.App.Services;
using HAMMOR.App.ViewModels;
using HAMMOR.App.Views;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Localization;
using HAMMOR.Core.Permissions;
using HAMMOR.Core.Status;
using HAMMOR.Core.Storage;
using HAMMOR.Core.Tasks;
using HAMMOR.Infrastructure.DependencyInjection;
using HAMMOR.Infrastructure.Memory;
using HAMMOR.Infrastructure.Persistence;
using HAMMOR.Platform.Windows.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;

namespace HAMMOR.App;

/// <summary>
/// Application entry point. Builds the DI host, applies persisted language and
/// theme, runs startup housekeeping, then shows the shell or the first-run
/// wizard.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Surface anything that escapes a handler rather than letting WPF die
        // silently with no diagnostic.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        var paths = new HammorPaths();
        paths.EnsureCreated();

        ConfigureSerilog(paths);

        try
        {
            _host = BuildHost();
            await _host.StartAsync().ConfigureAwait(true);

            await InitialiseAsync(_host.Services).ConfigureAwait(true);

            ShowFirstWindow(_host.Services);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "HAMMOR failed to start.");

            MessageBox.Show(
                $"HAMMOR could not start.\n\n{ex.Message}\n\nSee the log at:\n{paths.LogsDirectory}",
                "HAMMOR",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    /// <summary>
    /// Serilog writes a rolling daily file plus the debug console. Secret
    /// redaction happens at the call site (<c>SecretRedactor</c>) rather than
    /// in a sink, so anything reaching a sink is already safe.
    /// </summary>
    private static void ConfigureSerilog(HammorPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "hammor-.log"),
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .WriteTo.Console()
            .CreateLogger();

        Log.Information("HAMMOR starting. Data root: {DataRoot}", paths.DataRoot);
    }

    private static IHost BuildHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // Routes Microsoft.Extensions.Logging through the Serilog
                // pipeline configured above. Done with AddSerilog from
                // Serilog.Extensions.Logging rather than the UseSerilog host
                // extension, which would pull in another package for no gain.
                // dispose: false because OnExit flushes the static logger.
                logging.ClearProviders();
                logging.AddSerilog(Log.Logger, dispose: false);
            })
            .ConfigureServices((_, services) =>
            {
                // Core + adapters (platform-neutral).
                services.AddHammorInfrastructure();

                // Windows-specific: DPAPI secrets, WASAPI audio.
                services.AddHammorWindowsPlatform();

                // UI-layer services.
                services.AddSingleton<ILocalizationService, ResxLocalizationService>();
                services.AddSingleton<IConfirmationService, DialogConfirmationService>();
                services.AddSingleton<IThemeService, WpfUiThemeService>();
                services.AddSingleton<INavigationViewPageProvider, DependencyInjectionPageProvider>();

                // View models.
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<ChatViewModel>();
                services.AddSingleton<ActivityViewModel>();
                services.AddSingleton<TasksViewModel>();
                services.AddSingleton<MemoryViewModel>();
                services.AddSingleton<ProjectsViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddTransient<FirstRunViewModel>();

                // Views. Pages are transient so navigating back to one gets a
                // fresh visual tree bound to the singleton view model.
                services.AddSingleton<MainWindow>();
                services.AddTransient<ChatPage>();
                services.AddTransient<ActivityPage>();
                services.AddTransient<TasksPage>();
                services.AddTransient<MemoryPage>();
                services.AddTransient<ProjectsPage>();
                services.AddTransient<SettingsPage>();
            })
            .Build();

    /// <summary>
    /// Startup housekeeping: load configuration, apply language and theme,
    /// migrate the database, reconcile interrupted tasks, index memory.
    /// </summary>
    private static async Task InitialiseAsync(IServiceProvider services)
    {
        var configurationStore = services.GetRequiredService<IConfigurationStore>();
        await configurationStore.LoadAsync().ConfigureAwait(true);

        // Localisation is constructed after configuration is loaded so it
        // picks up the persisted language on its first read.
        var localization = services.GetRequiredService<ILocalizationService>();
        LocalizationSource.Instance.Attach(localization);

        services.GetRequiredService<IThemeService>()
            .Apply(configurationStore.Current.General.Theme);

        services.GetRequiredService<SqliteDatabase>().Migrate();

        var logger = services.GetRequiredService<ILogger<App>>();

        // A task still marked Running means the previous process died; mark it
        // Failed so the UI does not claim work is in flight.
        var reconciled = await services.GetRequiredService<ITaskStore>()
            .ReconcileInterruptedAsync().ConfigureAwait(true);

        if (reconciled > 0)
        {
            logger.LogWarning("Marked {Count} interrupted task(s) as failed.", reconciled);
        }

        if (configurationStore.Current.Memory.IndexOnStartup)
        {
            // Incremental: unchanged files are skipped on size+mtime, so this
            // stays cheap as memory grows.
            var report = await services.GetRequiredService<MarkdownMemoryIndexer>()
                .ReindexAsync().ConfigureAwait(true);

            logger.LogInformation(
                "Memory index ready ({Scanned} file(s) scanned, {Changed} changed).",
                report.Scanned, report.Added + report.Updated);
        }

        // Probe providers once so the shell opens with real status rather than
        // placeholder values. Event-driven from here on; nothing polls.
        await services.GetRequiredService<ISystemStatusService>()
            .RefreshAsync().ConfigureAwait(true);
    }

    private static void ShowFirstWindow(IServiceProvider services)
    {
        var configurationStore = services.GetRequiredService<IConfigurationStore>();

        if (!configurationStore.Current.SetupCompleted)
        {
            var wizard = new FirstRunWindow(services.GetRequiredService<FirstRunViewModel>());

            // The wizard owns the decision to continue. Closing it without
            // finishing exits rather than dropping the user into an
            // unconfigured shell.
            if (wizard.ShowDialog() != true)
            {
                Current.Shutdown();
                return;
            }
        }

        services.GetRequiredService<MainWindow>().Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync().ConfigureAwait(false);
            _host.Dispose();
        }

        Log.Information("HAMMOR exiting.");
        await Log.CloseAndFlushAsync().ConfigureAwait(false);

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        // Logged with the full exception, then shown to the user. Marked
        // handled so a single bad interaction does not take the app down —
        // but never swallowed silently.
        Log.Error(e.Exception, "Unhandled exception on the UI thread.");

        MessageBox.Show(
            $"{LocalizationSource.Instance["Common.Error"]}\n\n{e.Exception.Message}",
            "HAMMOR",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Cannot be handled — record it before the process goes down.
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled non-UI exception; terminating.");
        Log.CloseAndFlush();
    }
}
