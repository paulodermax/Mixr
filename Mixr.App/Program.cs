using System;
using System.IO;

namespace Mixr_App;

/// <summary>
/// Eigenes Main, damit sofort geloggt wird (vor App / XAML). Erfordert DISABLE_XAML_GENERATED_MAIN.
/// </summary>
public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    var dir = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.SetCurrentDirectory(dir);
                }
            }
            catch
            {
                /* */
            }

            AppLog.WriteLine($"Main entry (args: {string.Join(" ", args)})");

            AppLog.WriteLine("InitializeComWrappers …");
            global::WinRT.ComWrappersSupport.InitializeComWrappers();
            AppLog.WriteLine("InitializeComWrappers OK");

            AppLog.WriteLine("Application.Start …");
            global::Microsoft.UI.Xaml.Application.Start(_ =>
            {
                try
                {
                    AppLog.WriteLine("Application.Start callback (UI thread)");
                    var dq = global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                    AppLog.WriteLine($"DispatcherQueue: {(dq is null ? "null" : "ok")}");

                    var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(dq);
                    global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                    AppLog.WriteLine("SynchronizationContext gesetzt");

                    AppLog.WriteLine("new App() …");
                    new App();
                    AppLog.WriteLine("new App() zurück");
                }
                catch (Exception ex)
                {
                    AppLog.WriteLine("Exception im Application.Start-Callback:");
                    AppLog.WriteException(ex);
                    throw;
                }
            });
            AppLog.WriteLine("Application.Start beendet (Prozess endet normalerweise nicht hier bei WinUI)");
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Fatal in Main:");
            AppLog.WriteException(ex);
            throw;
        }
    }
}
