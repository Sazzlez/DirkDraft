using System.Runtime;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DraftPilot.Core.Config;
using DraftPilot.App.ViewModels;

namespace DraftPilot.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\DraftPilot.SingleInstance";
    private const string ShowSignalName = @"Local\DraftPilot.ShowWindow";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private CancellationTokenSource? _signalListener;
    private MainViewModel? _model;
    private MainWindow? _window;
    private TrayPresence? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Installed before anything else so even a failure in the lines below leaves a trace.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                CrashLog.Write("AppDomain", exception);
        };

        // Logged and swallowed. Left unhandled, WPF rethrows and the process dies — which is how a
        // single bad call on the UI thread managed to close the whole tool the moment a draft ended.
        // A panel that loses one update is far better than one that disappears mid-draft.
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write("Dispatcher", args.Exception);
            args.Handled = true;
        };

        try
        {
            Start();
        }
        catch (Exception exception)
        {
            CrashLog.Write("Startup", exception);

            // Tear the half-built window down first: a message box pumps messages, which would
            // re-enter the failing layout pass and throw a second time.
            try
            {
                _window?.Close();
            }
            catch (Exception teardown)
            {
                CrashLog.Write("Startup teardown", teardown);
            }

            _window = null;

            MessageBox.Show(
                $"DraftPilot konnte nicht starten.\n\n{exception.Message}\n\nDetails: {CrashLog.Path}",
                "DraftPilot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    private void Start()
    {
        BindingDiagnostics.EnableIfRequested();
        StartupReport.Write();

        // The panel is static text on flat backgrounds. Hardware rendering would pull in the GPU
        // user-mode driver (tens of megabytes of mapped pages) to draw rectangles; the CPU does it
        // for free, and there is nothing here that animates.
        if (AppSettings.Load().SoftwareRendering)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var dev = DevHarness.Parse(Environment.GetCommandLineArgs());

        // The development harness stays out of the single-instance handshake entirely: a replay or
        // screenshot run must neither be swallowed by a running instance (it would silently produce
        // nothing) nor block one.
        if (!dev.IsDemo && !dev.IsScreenshot)
        {
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);

            if (!isFirstInstance)
            {
                // Somebody double-clicked the shortcut while the tool was already running: bring
                // the existing window forward instead of starting a second copy.
                _showSignal.Set();
                Shutdown();
                return;
            }
        }

        _model = dev.IsDemo
            ? new MainViewModel(new Core.Lcu.JsonlSessionSource(dev.DemoRecording!, dev.DemoSpeed, dev.DemoFrames))
            : new MainViewModel();

        _window = new MainWindow(_model);

        _tray = new TrayPresence("DraftPilot");

        // The close glyph hides into the tray rather than quitting. Said once, out loud, because a
        // user who believes the tool is closed will keep talking to a stale instance forever.
        _window.HiddenToTray += () =>
        {
            if (_model.Settings.TrayHintShown)
                return;

            _model.Settings.TrayHintShown = true;
            _model.Settings.Save();
            _tray?.ShowHint(
                "DraftPilot läuft weiter",
                "Das Tool liegt jetzt im Infobereich. Beenden: Rechtsklick auf das Symbol → Beenden.");
        };
        _tray.ShowRequested += ShowWindow;
        _tray.UpdateRequested += () =>
        {
            ShowWindow();
            if (_model.UpdateDataCommand.CanExecute(null))
                _model.UpdateDataCommand.Execute(null);
        };
        _tray.ExitRequested += () => Shutdown();

        _model.DraftActiveChanged += OnDraftActiveChanged;
        _model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.StatusText))
                _tray?.SetTooltip($"DraftPilot — {_model.StatusText}");
        };

        if (_showSignal is not null)
            StartSignalListener();

        // Somebody just double-clicked the shortcut, so show them something. It stays on screen from
        // here on, shrinking to a compact status card outside a draft.
        ShowWindow();

        _ = _model.StartAsync();

        if (dev.IsScreenshot)
            ScheduleScreenshot(dev.ScreenshotPath!, dev.ScreenshotDelaySeconds);
    }

    /// <summary>
    /// Renders the window to a file once it has settled, then exits. Development only: it is the
    /// only way to actually look at the panel's layout and contrast.
    /// </summary>
    private void ScheduleScreenshot(string path, double delaySeconds)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            // Give the replayed draft and the icon cache time to land. A longer delay also makes the
            // local countdown observable instead of assumed.
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.2, delaySeconds))).ConfigureAwait(true);

            try
            {
                if (_window is not null)
                    DevHarness.Capture(_window, path);
            }
            catch (Exception exception)
            {
                CrashLog.Write("Screenshot", exception);
            }

            Shutdown();
        });
    }

    private void OnDraftActiveChanged(bool isActive)
    {
        Dispatcher.Invoke(() =>
        {
            if (_model is null || _window is null)
                return;

            if (isActive && _model.Settings.AutoShowOnChampSelect)
            {
                ShowWindow();
                return;
            }

            if (!isActive && _model.Settings.AutoHideAfterChampSelect && _window.IsVisible)
            {
                _window.SavePlacement();
                _window.Hide();

                // Champion select is the memory high-water mark. Hand the pages back rather than
                // sitting on them until the next draft.
                //
                // Blocking on purpose: GCCollectionMode.Aggressive rejects a non-blocking collection
                // outright, and the original non-blocking call threw every single time a draft ended.
                // Nothing is on screen at this point, so blocking costs nothing visible.
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }
        });
    }

    private void ShowWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (_window is null)
                return;

            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();

            // Somebody may have double-clicked the desktop icon precisely because the display looks
            // stale. Re-reading the snapshot is nearly free and repairs that without a restart.
            _model?.RequestSnapshotReload();
        });
    }

    /// <summary>Waits for a second instance to ask for the window to be shown.</summary>
    private void StartSignalListener()
    {
        _signalListener = new CancellationTokenSource();
        var token = _signalListener.Token;
        var signal = _showSignal!;

        var thread = new Thread(() =>
        {
            var handles = new[] { signal, token.WaitHandle };

            while (!token.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny(handles) == 0)
                    ShowWindow();
            }
        })
        {
            IsBackground = true,
            Name = "DraftPilot single-instance listener",
        };

        thread.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _window?.SavePlacement();
        _signalListener?.Cancel();
        _tray?.Dispose();

        if (_model is not null)
            _model.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));

        _signalListener?.Dispose();
        _showSignal?.Dispose();

        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not the owner, e.g. the second-instance path; nothing to release.
            }

            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
