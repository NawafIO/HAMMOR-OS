using System.IO;
using System.Windows.Threading;
using HAMMOR.App.Localization;
using HAMMOR.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HAMMOR.App.Tests.Localization;

/// <summary>
/// Regression tests for the language-switch crash: "The calling thread cannot
/// access this object because a different thread owns it" after switching
/// between English and Arabic.
/// </summary>
public sealed class ResxLocalizationServiceTests
{
    [Fact]
    public async Task A_switch_is_announced_on_the_ui_thread_when_saving_finishes_elsewhere()
    {
        using var ui = new DispatcherThread();
        var store = new OffThreadConfigurationStore();
        var service = await ui.Dispatcher.InvokeAsync(() => new ResxLocalizationService(
            store, NullLogger<ResxLocalizationService>.Instance, ui.Dispatcher));

        var announcements = 0;
        var announcedOffUiThread = false;
        service.LanguageChanged += (_, _) =>
        {
            announcements++;
            announcedOffUiThread |= !ui.Dispatcher.CheckAccess();
        };

        var stringsRefreshedOffUiThread = false;
        service.PropertyChanged += (_, _) => stringsRefreshedOffUiThread |= !ui.Dispatcher.CheckAccess();

        // English to Arabic and back, several times, as the Windows bug report
        // describes.
        foreach (var language in new[] { "ar", "en", "ar", "en" })
        {
            await await ui.Dispatcher.InvokeAsync(() => service.SetLanguageAsync(language));

            Assert.Equal(language, service.CurrentLanguage);
            Assert.Equal(language == "ar", service.IsRightToLeft);
            Assert.Equal(language, store.Current.General.Language);
        }

        // The scenario is real: every save completed off the UI thread.
        Assert.Equal(4, store.Saves);
        Assert.All(store.SaveThreads, id => Assert.NotEqual(ui.ThreadId, id));

        Assert.Equal(4, announcements);
        Assert.False(announcedOffUiThread);
        Assert.False(stringsRefreshedOffUiThread);
    }

    [Fact]
    public async Task A_switch_that_cannot_be_saved_changes_nothing()
    {
        using var ui = new DispatcherThread();
        var store = new OffThreadConfigurationStore { FailSaves = true };
        var service = await ui.Dispatcher.InvokeAsync(() => new ResxLocalizationService(
            store, NullLogger<ResxLocalizationService>.Instance, ui.Dispatcher));

        var announced = false;
        service.LanguageChanged += (_, _) => announced = true;

        await Assert.ThrowsAsync<IOException>(
            async () => await await ui.Dispatcher.InvokeAsync(() => service.SetLanguageAsync("ar")));

        Assert.Equal("en", service.CurrentLanguage);
        Assert.False(announced);
    }

    /// <summary>A configuration store whose saves complete on a pool thread, like the real one.</summary>
    private sealed class OffThreadConfigurationStore : IConfigurationStore
    {
        private readonly object _gate = new();
        private readonly List<int> _saveThreads = [];

        public HammorConfiguration Current { get; private set; } = new();

        public string ConfigurationFilePath => "test-config.json";

        public bool FailSaves { get; init; }

        public int Saves
        {
            get
            {
                lock (_gate)
                {
                    return _saveThreads.Count;
                }
            }
        }

        public IReadOnlyList<int> SaveThreads
        {
            get
            {
                lock (_gate)
                {
                    return _saveThreads.ToArray();
                }
            }
        }

        public event EventHandler<HammorConfiguration>? ConfigurationChanged;

        public Task<HammorConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public async Task SaveAsync(HammorConfiguration configuration, CancellationToken cancellationToken = default)
        {
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);

            if (FailSaves)
            {
                throw new IOException("The configuration file is locked.");
            }

            lock (_gate)
            {
                _saveThreads.Add(Environment.CurrentManagedThreadId);
            }

            Current = configuration;
            ConfigurationChanged?.Invoke(this, configuration);
        }
    }

    /// <summary>An STA thread running a WPF dispatcher, standing in for the UI thread.</summary>
    private sealed class DispatcherThread : IDisposable
    {
        private readonly Thread _thread;

        public DispatcherThread()
        {
            using var ready = new ManualResetEventSlim();
            Dispatcher? dispatcher = null;
            _thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                ThreadId = Environment.CurrentManagedThreadId;
                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "Test UI thread",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            ready.Wait();
            Dispatcher = dispatcher!;
        }

        public Dispatcher Dispatcher { get; }

        public int ThreadId { get; private set; }

        public void Dispose()
        {
            Dispatcher.InvokeShutdown();
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
