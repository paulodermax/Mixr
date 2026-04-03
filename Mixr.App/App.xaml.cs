using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mixr;
using Mixr_App.Services;
using WinRT.Interop;

namespace Mixr_App;

public partial class App : Application
{
    const string MutexName = @"Local\MixrDesktopSingleInstance";
    const string ActivateEventName = @"Local\MixrDesktopActivateWindow";

    static Mutex? s_mutex;
    EventWaitHandle? _activateEvent;
    DispatcherQueue? _dispatcherQueue;
    Task? _activateWaitTask;
    MainWindow? _window;
    CancellationTokenSource? _cts;
    Task? _hostTask;
    TaskbarIcon? _trayIcon;

    public App()
    {
        AppLog.WriteLine("App ctor (vor InitializeComponent)");
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
    }

    void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.WriteLine("UnhandledException:");
        AppLog.WriteException(e.Exception);
        Debug.WriteLine(e.Exception);
        e.Handled = true;
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

        AppLog.WriteLine("Erste Instanz — Mutex ok.");

        _cts = new CancellationTokenSource();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        StartupRegistration.SetRunAtLogin(true);

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

        var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Mixr — läuft im Hintergrund",
            Icon = File.Exists(icoPath) ? new Icon(icoPath) : SystemIcons.Application,
        };
        var openOnDbl = new XamlUICommand();
        openOnDbl.ExecuteRequested += (_, _) => ShowMainWindow();
        _trayIcon.DoubleClickCommand = openOnDbl;

        var flyout = new MenuFlyout();
        var openItem = new MenuFlyoutItem { Text = "Mixr öffnen" };
        openItem.Click += (_, _) => ShowMainWindow();
        var exitItem = new MenuFlyoutItem { Text = "Beenden" };
        exitItem.Click += (_, _) => ExitFromTray();
        var logItem = new MenuFlyoutItem { Text = "Log öffnen (EXE-Ordner oder %LocalAppData%\\Mixr)" };
        logItem.Click += OpenLogFile_Click;
        flyout.Items.Add(openItem);
        flyout.Items.Add(logItem);
        flyout.Items.Add(exitItem);
        _trayIcon.ContextFlyout = flyout;

        // Ohne ForceCreate erscheint das Tray-Icon in reinem Code oft nicht — dann wirkt die App „weg“, sobald das Fenster ausgeblendet wird.
        try
        {
            _trayIcon.ForceCreate(enablesEfficiencyMode: false);
            AppLog.WriteLine("Tray: ForceCreate ok.");
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Tray: ForceCreate fehlgeschlagen:");
            AppLog.WriteException(ex);
        }

        var hostOpts = new MixrHost.Options
        {
            Log = s => AppLog.WriteLine(s),
            LogError = s => AppLog.WriteLine("[ERR] " + s),
        };

        var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
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
                Debug.WriteLine(ex);
                EnqueueHostError(ex);
            }
        });

        _window.Activate();

        _ = Task.Run(async () =>
        {
            try
            {
                await GameCatalogCoordinator.RunStartupAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                /* Hintergrund-Katalog — Fehler ignorieren */
            }
        });

        // Kurz sichtbar lassen, dann in den Tray — sonst wirkt es wie „Absturz“, bevor das Icon da ist.
        _ = DelayHideMainWindowAsync();
    }

    /// <summary>Beendet Prozess inkl. Host (wie Tray „Beenden“).</summary>
    public static void ExitCompletely()
    {
        if (Current is App app)
            app.ExitFromTray();
    }

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

    void EnqueueHostError(Exception ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        AppLog.WriteLine($"UI: Fehlerdialog geplant: {msg}");
        _dispatcherQueue?.TryEnqueue(() => _ = ShowHostErrorDialogAsync(msg));
    }

    async Task ShowHostErrorDialogAsync(string message)
    {
        try
        {
            if (_window?.Content is FrameworkElement fe && fe.XamlRoot != null)
            {
                var dlg = new ContentDialog
                {
                    Title = "Mixr — Hintergrunddienst",
                    Content =
                        "Serial oder Medien-Dienst konnte nicht starten:\n\n"
                        + message
                        + "\n\nDetails stehen in mixr_app.log neben der EXE.",
                    CloseButtonText = "OK",
                    XamlRoot = fe.XamlRoot,
                };
                await dlg.ShowAsync();
            }
            else
            {
                AppLog.WriteLine("UI: Kein XamlRoot — Fehlerdialog übersprungen. Siehe mixr_app.log.");
            }
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("UI: Fehlerdialog fehlgeschlagen:");
            AppLog.WriteException(ex);
        }
    }

    void OpenLogFile_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            AppLog.WriteLine("(Tray) Log öffnen angefordert.");
            var path = AppLog.LogFilePath;
            if (!File.Exists(path))
                File.WriteAllText(path, "");

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("OpenLogFile:");
            AppLog.WriteException(ex);
        }
    }

    /// <summary>
    /// Zweiter Start: Mutex ist belegt — Event an die laufende Instanz senden und beenden.
    /// Kurz retry, falls die erste Instanz das Event noch anlegt.
    /// </summary>
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

    void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
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
        Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id).Hide();
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
        var aw = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(wid);
        aw.Show();
        _window.Activate();
    }

    internal void ExitFromTray()
    {
        try
        {
            _trayIcon?.Dispose();
        }
        catch
        {
            /* */
        }

        _cts?.Cancel();
        try
        {
            _activateWaitTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            /* */
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
            _hostTask?.Wait(TimeSpan.FromSeconds(10));
        }
        catch
        {
            /* */
        }

        Environment.Exit(0);
    }
}
