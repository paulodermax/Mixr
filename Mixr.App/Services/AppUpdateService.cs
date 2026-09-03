using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Mixr_App.Services;

public enum AppUpdateState
{
    /// <summary>Entwicklungsstart (nicht per Setup installiert) — Updates deaktiviert.</summary>
    NotInstalled,
    Idle,
    Checking,
    UpdateAvailable,
    Downloading,
    ReadyToInstall,
    Error,
}

/// <summary>
/// App-Updates über Velopack + GitHub Releases. Prüft beim Start (verzögert) und danach alle 6 Stunden,
/// lädt gefundene Updates im Hintergrund und meldet „bereit“ — installiert wird erst auf Nutzerwunsch
/// (oder beim nächsten Start automatisch durch Velopack).
/// </summary>
public static class AppUpdateService
{
    public const string RepositoryUrl = "https://github.com/paulodermax/Mixr";

    static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(45);
    static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    static readonly Lazy<UpdateManager> Manager = new(() =>
        new UpdateManager(new GithubSource(RepositoryUrl, null, prerelease: false)));

    static readonly SemaphoreSlim Gate = new(1, 1);
    static Timer? _timer;

    public static AppUpdateState State { get; private set; } = AppUpdateState.Idle;

    public static UpdateInfo? Available { get; private set; }

    public static int DownloadPercent { get; private set; }

    public static string? LastError { get; private set; }

    public static DateTimeOffset? LastCheck { get; private set; }

    /// <summary>Feuert auf beliebigem Thread — UI muss auf den Dispatcher wechseln.</summary>
    public static event Action? StateChanged;

    public static bool IsInstalled
    {
        get
        {
            try
            {
                return Manager.Value.IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    public static string CurrentVersion => AppVersion.Display;

    public static string? AvailableVersion => Available?.TargetFullRelease.Version.ToString();

    public static void StartBackgroundChecks()
    {
        if (!IsInstalled)
        {
            SetState(AppUpdateState.NotInstalled);
            AppLog.WriteLine("Updates: App läuft nicht aus einer Velopack-Installation — automatische Prüfung aus.");
            return;
        }

        _timer ??= new Timer(_ => _ = CheckAndDownloadAsync(userInitiated: false), null, InitialDelay, Interval);
    }

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Prüft und lädt ein verfügbares Update herunter. Gibt <c>true</c> zurück, wenn eines bereitliegt.</summary>
    public static async Task<bool> CheckAndDownloadAsync(bool userInitiated)
    {
        if (!IsInstalled)
        {
            SetState(AppUpdateState.NotInstalled);
            return false;
        }

        if (!await Gate.WaitAsync(0).ConfigureAwait(false))
            return State == AppUpdateState.ReadyToInstall;

        try
        {
            if (State == AppUpdateState.ReadyToInstall && Available != null)
                return true;

            SetState(AppUpdateState.Checking);
            var mgr = Manager.Value;
            var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            LastCheck = DateTimeOffset.Now;

            if (info is null)
            {
                Available = null;
                SetState(AppUpdateState.Idle);
                if (userInitiated)
                    AppLog.WriteLine($"Updates: {CurrentVersion} ist aktuell.");
                return false;
            }

            Available = info;
            AppLog.WriteLine($"Updates: {info.TargetFullRelease.Version} verfügbar (aktuell {CurrentVersion}).");
            SetState(AppUpdateState.UpdateAvailable);

            SetState(AppUpdateState.Downloading);
            await mgr.DownloadUpdatesAsync(info, p =>
            {
                DownloadPercent = p;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);

            DownloadPercent = 100;
            SetState(AppUpdateState.ReadyToInstall);
            AppLog.WriteLine($"Updates: {info.TargetFullRelease.Version} heruntergeladen — bereit zur Installation.");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLog.WriteLine("Updates: " + ex.Message);
            SetState(AppUpdateState.Error);
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Beendet die App geordnet (Host, Hook, Tray) und lässt Velopack das heruntergeladene Update installieren
    /// und die App neu starten. Kehrt nicht zurück.
    /// </summary>
    public static async Task InstallAndRestartAsync()
    {
        var info = Available;
        if (info is null || State != AppUpdateState.ReadyToInstall)
            return;

        AppLog.WriteLine($"Updates: installiere {info.TargetFullRelease.Version} und starte neu …");
        await App.PrepareShutdownAsync().ConfigureAwait(false);
        Manager.Value.ApplyUpdatesAndRestart(info);
    }

    static void SetState(AppUpdateState s)
    {
        State = s;
        StateChanged?.Invoke();
    }
}
