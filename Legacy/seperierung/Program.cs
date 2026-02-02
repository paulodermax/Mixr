using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mixr.Services;
using Serilog;

namespace Mixr;

public class Program
{
    public static void Main(string[] args)
    {
        LoggerService.Initialize();
        try
        {
            LoggerService.Info("Mixr startet...");
            Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton<ConfigService>();
                    services.AddSingleton<SerialService>();
                    services.AddSingleton<AudioService>();
                    services.AddSingleton<MediaService>();
                    services.AddSingleton<ProcessWatcher>();
                    services.AddHostedService<MixrWorker>();
                })
                .Build()
                .Run();
        }
        catch (Exception ex)
        {
            LoggerService.Error("Fataler Fehler beim Start", ex);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}