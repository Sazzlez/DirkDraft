using System.Runtime;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DraftPilot.Core.Config;
using DraftPilot.App.ViewModels;

namespace DraftPilot.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\DirkDraft.SingleInstance";
    private const string ShowSignalName = @"Local\DirkDraft.ShowWindow";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private string? _phase;

    /// <summary>When the running game was announced (0 = never); anchors the anti-minimise grace window.</summary>
    private long _gameStartedAtTick;
    private CancellationTokenSource? _signalListener;
    private Thread? _signalThread;
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
                _window?.AllowClose();
                _window?.Close();
            }
            catch (Exception teardown)
            {
                CrashLog.Write("Startup teardown", teardown);
            }

            _window = null;

            MessageBox.Show(
                $"DirkDraft konnte nicht starten.\n\n{exception.Message}\n\nDetails: {CrashLog.Path}",
                "DirkDraft",
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
        DevHarness.SuppressPlacementSave = dev.IsDemo || dev.IsScreenshot;

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

        _tray = new TrayPresence("DirkDraft");

        // The close glyph hides into the tray rather than quitting. Said once, out loud, because a
        // user who believes the tool is closed will keep talking to a stale instance forever.
        _window.HiddenToTray += () =>
        {
            if (_model.Settings.TrayHintShown)
                return;

            _model.Settings.TrayHintShown = true;
            _model.Settings.Save();
            _tray?.ShowHint(
                "DirkDraft läuft weiter",
                "Das Tool liegt jetzt im Infobereich. Beenden: Rechtsklick auf das Symbol → Beenden.");
        };
        _tray.ShowRequested += () => ShowWindow();
        _tray.UpdateRequested += () =>
        {
            ShowWindow();
            if (_model.UpdateDataCommand.CanExecute(null))
                _model.UpdateDataCommand.Execute(null);
        };
        _tray.ExitRequested += () =>
        {
            // Without this the Closing handler would turn the shutdown's close into a hide.
            _window?.AllowClose();
            Shutdown();
        };

        _model.DraftActiveChanged += OnDraftActiveChanged;

        // The build view is only useful if it is actually on screen when the game loads — but a
        // window that is already visible must not move, resize or steal focus from the game. Only
        // one hidden in the tray (or minimised — IsVisible stays true then) comes back, and
        // deliberately without Activate.
        _model.GameActiveChanged += isRunning => Dispatcher.Invoke(() =>
        {
            if (!isRunning || !_model.Settings.AutoShowOnGameStart || _window is null)
                return;

            _gameStartedAtTick = Environment.TickCount64;

            if (!_window.IsVisible)
                _window.Show();

            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
        });

        // The game taking the screen can MINIMISE other windows — ours included, second monitor
        // or not. GameActiveChanged re-anchors the tick on every phase change (GameStart AND
        // InProgress — the fullscreen grab happens at the latter), and around those moments a
        // minimise is never the user's doing, so the window puts itself back (without Activate:
        // the game keeps the focus). The time window keeps this from fighting the user's own
        // minimise later in the game.
        _window.StateChanged += (_, _) =>
        {
            if (_window is not { WindowState: WindowState.Minimized }
                || _model is not { IsGameRunning: true }
                || !_model.Settings.AutoShowOnGameStart
                || _gameStartedAtTick == 0
                || Environment.TickCount64 - _gameStartedAtTick > 60_000)
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(
                () =>
                {
                    if (_window is { WindowState: WindowState.Minimized })
                        _window.WindowState = WindowState.Normal;
                },
                System.Windows.Threading.DispatcherPriority.Background);
        };
        _model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.StatusText))
                _tray?.SetTooltip($"DirkDraft — {_model.StatusText}");
        };

        if (_showSignal is not null)
            StartSignalListener();

        // Somebody just double-clicked the shortcut, so show them something. StartAsync loads the
        // snapshot right after, so there is nothing to reload yet.
        ShowWindow(reloadSnapshot: false);

        // Fire and forget, but never unobserved: a broken lockfile path used to fail in here
        // without a trace, leaving the panel on "Starte…" forever with an empty crash log.
        _ = _model.StartAsync().ContinueWith(
            task => CrashLog.Write("Session-Quelle", task.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        _phase = dev.Phase;

        if (dev.IsScreenshot)
            ScheduleScreenshot(dev.ScreenshotPath!, dev.ScreenshotDelaySeconds, dev.ExpandRows);
    }

    /// <summary>
    /// Renders the window to a file once it has settled, then exits. Development only: it is the
    /// only way to actually look at the panel's layout and contrast.
    /// </summary>
    private void ScheduleScreenshot(string path, double delaySeconds, int expandRows)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            // Give the replayed draft and the icon cache time to land. A longer delay also makes the
            // local countdown observable instead of assumed.
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.2, delaySeconds))).ConfigureAwait(true);

            // Preparation inside the try too: if expanding a row or forcing a phase throws, the
            // harness must still shut down instead of hanging as an invisible process.
            try
            {
                if (_model is not null)
                {
                    foreach (var row in _model.Recommendations.Take(Math.Max(0, expandRows)))
                        row.IsExpanded = true;

                    if (_phase is { Length: > 0 })
                        _model.OnGameflowPhase(_phase);
                }

                if (_window is not null)
                    DevHarness.Capture(_window, path);
            }
            catch (Exception exception)
            {
                CrashLog.Write("Screenshot", exception);
            }

            _window?.AllowClose();
            Shutdown();
        });
    }

    private void OnDraftActiveChanged(bool isActive)
    {
        // Background priority on purpose: this event fires from inside the view-model's own
        // update pass, BEFORE it has cleared the draft collections. Running inline meant the
        // aggressive collection below walked a heap where everything was still reachable — and
        // froze the UI mid-event for the privilege.
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_model is null || _window is null)
                return;

            if (isActive && _model.Settings.AutoShowOnChampSelect)
            {
                // No snapshot reload here: it would throw away the live matchup data this very
                // draft just fetched.
                ShowWindow(reloadSnapshot: false);
                return;
            }

            if (!isActive && _model.Settings.AutoHideAfterChampSelect && _window.IsVisible)
            {
                _window.SavePlacement();
                _window.Hide();

                // Champion select is the memory high-water mark. Hand the pages back rather than
                // sitting on them until the next draft.
                //
                // Blocking is mandatory (GCCollectionMode.Aggressive rejects non-blocking calls),
                // so the collection runs on a worker: several hundred milliseconds of blocking
                // belong to a pool thread, not to the dispatcher — even with nothing on screen.
                _ = Task.Run(() =>
                {
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                });
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ShowWindow(bool reloadSnapshot = true)
    {
        Dispatcher.Invoke(() =>
        {
            if (_window is null)
                return;

            // A window that is already on screen stays exactly as it is — no focus grab, no state
            // reset. Activate only what was actually hidden or minimised.
            var wasPresented = _window.IsVisible && _window.WindowState == WindowState.Normal;

            _window.Show();

            if (!wasPresented)
            {
                _window.WindowState = WindowState.Normal;
                _window.Activate();
            }

            // Somebody may have double-clicked the desktop icon precisely because the display
            // looks stale. Re-reading the snapshot repairs that without a restart — but only on
            // those user-initiated paths; automatic shows must not touch the loaded data.
            if (reloadSnapshot)
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
                try
                {
                    if (WaitHandle.WaitAny(handles) == 0)
                        ShowWindow();
                }
                catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException or InvalidOperationException)
                {
                    // Shutdown race: the handles or the dispatcher went away while we were
                    // waiting. On a background thread this would otherwise kill the process.
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "DirkDraft single-instance listener",
        };

        _signalThread = thread;
        thread.Start();
    }

    /// <summary>
    /// Windows log-off/restart: WPF calls Shutdown() itself afterwards, and that ignores the
    /// Closing handler's cancel but still runs its body — which would hide the window into the
    /// tray and burn the one-off "still running" hint on the way out.
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _window?.AllowClose();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _window?.SavePlacement();

        // Wake the listener and give it a moment to leave WaitAny BEFORE its handles are
        // disposed below — disposing under a waiter throws on a background thread.
        _signalListener?.Cancel();
        _signalThread?.Join(500);

        _tray?.Dispose();

        try
        {
            if (_model is not null)
                _model.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException exception)
        {
            // OnExit is past the dispatcher's exception handling; a teardown failure here must
            // not turn a clean quit into a crash dialog.
            CrashLog.Write("Beenden", exception.GetBaseException());
        }

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
