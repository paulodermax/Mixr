using System;
using System.IO;
using System.Linq;
using Mixr.Services;
using Velopack;

namespace Mixr_App;

/// <summary>
/// Eigenes Main: Velopack-Hooks müssen als Allererstes laufen (Install/Update/Uninstall starten die EXE mit
/// Spezialargumenten und erwarten sofortiges Beenden), danach Logging vor App / XAML.
/// Erfordert DISABLE_XAML_GENERATED_MAIN.
/// </summary>
public static class Program
{
    public static bool StartMinimized { get; private set; }

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            VelopackApp.Build()
                .SetLogger(new VelopackAppLogger())
                .OnFirstRun(_ => AppLog.WriteLine("Velopack: erster Start nach Installation."))
                .OnRestarted(v => AppLog.WriteLine($"Velopack: nach Update auf {v} neu gestartet."))
                .Run();

            StartMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            try
            {
                var exe = Environment.ProcessPath;
                var dir = string.IsNullOrEmpty(exe) ? null : Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir))
                    Directory.SetCurrentDirectory(dir);
            }
            catch (Exception ex)
            {
                AppLog.WriteLine("SetCurrentDirectory: " + ex.Message);
            }

            AppLog.WriteLine($"==== Mixr {AppVersion.Display} — Main (args: {string.Join(" ", args)})");
            AppLog.WriteLine($"Datenordner: {MixrConfigPaths.DataRoot}");

            MixrConfigLoader.DiagnosticLog = AppLog.WriteLine;
            IgdbCredentialResolver.DiagnosticLog = AppLog.WriteLine;
            IgdbCoverService.DiagnosticLog = AppLog.WriteLine;
            _ = MixrConfigLoader.Load(args);
            AppLog.WriteLine("[IGDB] " + IgdbCredentialResolver.FormatDiagnosticSummary());

            global::WinRT.ComWrappersSupport.InitializeComWrappers();

            global::Microsoft.UI.Xaml.Application.Start(_ =>
            {
                try
                {
                    var dq = global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                    var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(dq);
                    global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                    new App();
                }
                catch (Exception ex)
                {
                    AppLog.WriteLine("Exception im Application.Start-Callback:");
                    AppLog.WriteException(ex);
                    throw;
                }
            });
            AppLog.WriteLine("Application.Start beendet.");
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Fatal in Main:");
            AppLog.WriteException(ex);
            throw;
        }
    }

    sealed class VelopackAppLogger : Velopack.Logging.IVelopackLogger
    {
        public void Log(Velopack.Logging.VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            if (logLevel < Velopack.Logging.VelopackLogLevel.Information)
                return;
            AppLog.WriteLine($"[Velopack/{logLevel}] {message}{(exception is null ? "" : " " + exception.Message)}");
        }
    }
}
