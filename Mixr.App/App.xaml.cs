using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mixr;
using Mixr.Services;
using Mixr_App.Services;
using WinRT.Interop;

namespace Mixr_App;

public partial class App : Application
{
    const string MutexName = @"Local\MixrDesktopSingleInstance";
    const string ActivateEventName = @"Local\MixrDesktopActivateWindow";

    static Mutex? s_mutex;
    static int s_shutdownStarted;
    static readonly TaskCompletionSource s_shutdownDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

    EventWaitHandle? _activateEvent;
    DispatcherQueue? _dispatcherQueue;
    Task? _activateWaitTask;
    MainWindow? _window;
    CancellationTokenSource? _cts;
    Task? _hostTask;
    TaskbarIcon? _trayIcon;
    MenuFlyoutItem? _trayUpdateItem;
    int _unhandledCount;

    public App()
    {
        AppLog.WriteLine("App ctor");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("InitializeComponent fehlgeschlagen:");
            AppLog.WriteException(ex);
            throw;
        }

        UnhandledException += App_UnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // AudioSwitcher 3.x feuert gelegentlich ArgumentNullException („source“) aus dem
            // Session-Observer, wenn Windows Sessions hinzufügt/entfernt — harmlos, aber laut.
            if (IsKnownAudioSwitcherNoise(e.Exception))
            {
                e.SetObserved();
                return;
            }

            AppLog.WriteLine("UnobservedTaskException:");
            AppLog.WriteException(e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLog.WriteLine("AppDomain.UnhandledException (terminating=" + e.IsTerminating + "):");
            if (e.ExceptionObject is Exception ex2)
                AppLog.WriteException(ex2);
        };
    }

    void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.WriteLine("UnhandledException (UI):");
        AppLog.WriteException(e.Exception);
        Debug.WriteLine(e.Exception);

        // Einzelne UI-Ausnahmen (z. B. Binding/Layout) abfangen, statt den Tray-Dienst zu töten.
        // Häufen sie sich, ist der Zustand nicht mehr vertrauenswürdig → geordnet beenden.
        if (Interlocked.Increment(ref _unhandledCount) <= 3)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        _dispatcherQueue?.TryEnqueue(async () =>
        {
            await ShowFatalDialogAsync(e.Exception.Message);
            await PrepareShutdownAsync();
            Environment.Exit(1);
        });
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        AppLog.WriteLine($"OnLaunched BaseDirectory={AppContext.BaseDirectory}");

        s_mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            AppLog.WriteLine("Zweite Instanz — signalisiere laufende App und beende.");
            TrySignalRunningInstanceAndExit();
            return;
        }

        GameCatalogPaths.SyncBundledCoversToAppData();

        _cts = new CancellationTokenSource();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        // Autostart ist Opt-in (Settings). Nur ein bereits vorhandener Eintrag wird nach Updates auf den neuen Pfad korrigiert.
        StartupRegistration.RefreshPathIfEnabled();

        _window = new MainWindow();
        _window.AppWindow.Closing += AppWindow_Closing;

        var wh = _activateEvent;
        var cancelWait = _cts.Token.WaitHandle;
        _activateWaitTask = Task.Run(() =>
        {
            while (true)
            {
                var n = WaitHandle.WaitAny(new[] { wh, cancelWait });
                if (n == 1)
                    break;
                _dispatcherQueue?.TryEnqueue(ShowMainWindowCore);
            }
        });

        CreateTrayIcon();

        var hostOpts = new MixrHost.Options
        {
            Log = s => AppLog.WriteLine(s),
            LogError = s => AppLog.WriteLine("[ERR] " + s),
        };

        var argv = Environment.GetCommandLineArgs().Skip(1).Where(a => !a.StartsWith("--minimized", StringComparison.OrdinalIgnoreCase)).ToArray();
        _hostTask = Task.Run(async () =>
        {
            try
            {
                AppLog.WriteLine("MixrHost: Start …");
                await MixrHost.RunAsync(argv, _cts.Token, hostOpts);
                AppLog.WriteLine("MixrHost: beendet (normal).");
            }
            catch (OperationCanceledException)
            {
                AppLog.WriteLine("MixrHost: Abbruch (Cancellation).");
            }
            catch (Exception ex)
            {
                AppLog.WriteLine("MixrHost: Exception:");
                AppLog.WriteException(ex);
                EnqueueHostError(ex);
            }
        });

        if (Program.StartMinimized)
        {
            AppLog.WriteLine("Start minimiert (Autostart) — Fenster bleibt im Tray.");
        }
        else
        {
            _window.Activate();
            _ = DelayHideMainWindowAsync();
        }

        _ = TryAttachTrayFlyoutXamlRootAsync();

        var startupCts = _cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await GameCatalogCoordinator.RunStartupAsync(startupCts.Token).ConfigureAwait(false);
                CatalogManualCoverSync.ApplyManualFilesToStore();
                SessionGroupsBootstrap.RunMergeIfNeeded();
                var storeAfterBootstrap = GameCatalogStore.LoadOrCreate();
                storeAfterBootstrap.Save();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.WriteLine("Katalog-Start: " + ex.Message);
            }

            _dispatcherQueue?.TryEnqueue(async () =>
            {
                try
                {
                    await CoverWarmup.PreloadAllAsync();
                    GameCatalogCoordinator.NotifyCatalogChanged();
                }
                catch (Exception ex)
                {
                    AppLog.WriteLine("CoverWarmup: " + ex.Message);
                }
            });
        });

        AppUpdateService.StateChanged += OnUpdateStateChanged;
        AppUpdateService.StartBackgroundChecks();
    }

    // ---- Tray ----------------------------------------------------------------------------------

    void CreateTrayIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = $"Mixr {AppVersion.Display}",
            Icon = File.Exists(icoPath) ? new Icon(icoPath) : SystemIcons.Application,
        };
        var openOnDbl = new XamlUICommand();
        openOnDbl.ExecuteRequested += (_, _) => ShowMainWindow();
        _trayIcon.DoubleClickCommand = openOnDbl;

        var flyout = new MenuFlyout();
        var openItem = new MenuFlyoutItem { Text = "Mixr öffnen" };
        openItem.Click += (_, _) => _dispatcherQueue?.TryEnqueue(ShowMainWindowCore);

        _trayUpdateItem = new MenuFlyoutItem { Text = "Update installieren", Visibility = Visibility.Collapsed };
        _trayUpdateItem.Click += (_, _) => _ = AppUpdateService.InstallAndRestartAsync();

        var logItem = new MenuFlyoutItem { Text = "Log-Ordner öffnen" };
        logItem.Click += (_, _) => _dispatcherQueue?.TryEnqueue(OpenLogFolderCore);
        var exitItem = new MenuFlyoutItem { Text = "Beenden" };
        exitItem.Click += (_, _) => _dispatcherQueue?.TryEnqueue(() => _ = ExitAsync());

        flyout.Items.Add(openItem);
        flyout.Items.Add(_trayUpdateItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(logItem);
        flyout.Items.Add(exitItem);
        _trayIcon.ContextFlyout = flyout;

        try
        {
            _trayIcon.ForceCreate(enablesEfficiencyMode: false);
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Tray: ForceCreate fehlgeschlagen:");
            AppLog.WriteException(ex);
        }
    }

    void OnUpdateStateChanged()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            if (_trayUpdateItem is null || _trayIcon is null)
                return;

            var ready = AppUpdateService.State == AppUpdateState.ReadyToInstall;
            _trayUpdateItem.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
            if (ready)
            {
                _trayUpdateItem.Text = $"Update auf {AppUpdateService.AvailableVersion} installieren";
                _trayIcon.ToolTipText = $"Mixr {AppVersion.Display} — Update {AppUpdateService.AvailableVersion} bereit";
                try
                {
                    _trayIcon.ShowNotification(
                        "Mixr-Update bereit",
                        $"Version {AppUpdateService.AvailableVersion} wurde heruntergeladen. Installation über das Tray-Menü oder in den Einstellungen.");
                }
                catch (Exception ex)
                {
                    AppLog.WriteLine("Tray-Benachrichtigung: " + ex.Message);
                }
            }
            else
            {
                _trayIcon.ToolTipText = $"Mixr {AppVersion.Display}";
            }
        });
    }

    async Task TryAttachTrayFlyoutXamlRootAsync()
    {
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(80);
            if (_window?.Content is FrameworkElement fe && fe.XamlRoot != null && _trayIcon?.ContextFlyout is MenuFlyout mf)
            {
                mf.XamlRoot = fe.XamlRoot;
                return;
            }
        }
    }

    void OpenLogFolderCore()
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectory);
            Process.Start(new ProcessStartInfo { FileName = AppLog.LogDirectory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("OpenLogFolder: " + ex.Message);
        }
    }

    // ---- Fenster -------------------------------------------------------------------------------

    async Task DelayHideMainWindowAsync()
    {
        try
        {
            await Task.Delay(1200);
            _dispatcherQueue?.TryEnqueue(HideMainWindow);
        }
        catch
        {
            /* */
        }
    }

    void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        sender.Hide();
    }

    void HideMainWindow()
    {
        if (_window == null)
            return;
        var hWnd = WindowNative.GetWindowHandle(_window);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow.GetFromWindowId(id).Hide();
    }

    void ShowMainWindow()
    {
        if (_window?.DispatcherQueue == null)
            return;
        _window.DispatcherQueue.TryEnqueue(ShowMainWindowCore);
    }

    void ShowMainWindowCore()
    {
        if (_window == null)
            return;
        var hWnd = WindowNative.GetWindowHandle(_window);
        var wid = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var aw = AppWindow.GetFromWindowId(wid);
        aw.Show();

        if (aw.Presenter is OverlappedPresenter presenter)
            presenter.Restore(activateWindow: true);
        else
            _window.Activate();

        ShowWindow(hWnd, SwRestore);
        SetForegroundWindow(hWnd);
        _window.Activate();
    }

    const int SwRestore = 9;

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---- Fehler / Dialoge ----------------------------------------------------------------------

    void EnqueueHostError(Exception ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        _dispatcherQueue?.TryEnqueue(() => _ = ShowDialogAsync(
            "Mixr — Hintergrunddienst",
            "Der Hintergrunddienst (Seriell/Audio/Medien) konnte nicht gestartet werden:\n\n" + msg +
            $"\n\nDetails im Log: {AppLog.LogFilePath}"));
    }

    Task ShowFatalDialogAsync(string message) => ShowDialogAsync(
        "Mixr — unerwarteter Fehler",
        "Mixr hat mehrere unerwartete Fehler festgestellt und wird beendet.\n\n" + message +
        $"\n\nDetails im Log: {AppLog.LogFilePath}");

    async Task ShowDialogAsync(string title, string content)
    {
        try
        {
            if (_window?.Content is FrameworkElement fe && fe.XamlRoot != null)
            {
                ShowMainWindowCore();
                var dlg = new ContentDialog
                {
                    Title = title,
                    Content = content,
                    CloseButtonText = "OK",
                    XamlRoot = fe.XamlRoot,
                };
                await dlg.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Dialog fehlgeschlagen: " + ex.Message);
        }
    }

    static void TrySignalRunningInstanceAndExit()
    {
        for (var i = 0; i < 50; i++)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ActivateEventName);
                ev.Set();
                break;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(100);
            }
        }

        Environment.Exit(0);
    }

    // ---- Herunterfahren ------------------------------------------------------------------------

    /// <summary>Beendet Prozess inkl. Host (Navigation „Beenden“).</summary>
    public static void ExitCompletely()
    {
        if (Current is App app)
            _ = app.ExitAsync();
    }

    /// <summary>
    /// Stoppt Host (Serial, Audio, Hotkey-Hook), Update-Timer und Tray geordnet. Idempotent; wird vom Update-Pfad
    /// (Velopack-Neustart) und vom Beenden benutzt. Läuft nicht auf dem UI-Thread blockierend.
    /// </summary>
    public static async Task PrepareShutdownAsync()
    {
        if (Interlocked.Exchange(ref s_shutdownStarted, 1) != 0)
        {
            await s_shutdownDone.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            if (Current is App app)
                await app.ShutdownCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            s_shutdownDone.TrySetResult();
        }
    }

    async Task ShutdownCoreAsync()
    {
        AppLog.WriteLine("Shutdown: beginne …");
        AppUpdateService.Stop();
        AppUpdateService.StateChanged -= OnUpdateStateChanged;

        try
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    _trayIcon?.Dispose();
                }
                catch
                {
                    /* */
                }
            });
        }
        catch
        {
            /* */
        }

        _cts?.Cancel();

        var pending = new[] { _hostTask, _activateWaitTask }.Where(t => t != null).Cast<Task>().ToArray();
        if (pending.Length > 0)
        {
            var finished = await Task.WhenAny(Task.WhenAll(pending), Task.Delay(TimeSpan.FromSeconds(8))).ConfigureAwait(false);
            if (finished is not { IsCompleted: true } || !pending.All(t => t.IsCompleted))
                AppLog.WriteLine("Shutdown: Host hat nicht rechtzeitig beendet — erzwinge.");
        }

        try
        {
            _activateEvent?.Dispose();
        }
        catch
        {
            /* */
        }

        try
        {
            s_mutex?.ReleaseMutex();
            s_mutex?.Dispose();
        }
        catch
        {
            /* Mutex gehört evtl. einem anderen Thread — beim Prozessende egal */
        }

        AppLog.WriteLine("Shutdown: abgeschlossen.");
    }

    async Task ExitAsync()
    {
        await PrepareShutdownAsync().ConfigureAwait(false);
        Environment.Exit(0);
    }

    static bool IsKnownAudioSwitcherNoise(Exception ex)
    {
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            if (cur is ArgumentNullException ane
                && string.Equals(ane.ParamName, "source", StringComparison.Ordinal)
                && (cur.StackTrace?.Contains("AudioSwitcher", StringComparison.Ordinal) == true
                    || (cur.TargetSite?.DeclaringType?.FullName?.Contains("AudioSwitcher", StringComparison.Ordinal) == true)))
                return true;

            if (cur is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    if (IsKnownAudioSwitcherNoise(inner))
                        return true;
                }
            }
        }

        return false;
    }
}
