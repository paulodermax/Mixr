using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixr.Services;
using Mixr_App.Services;

namespace Mixr_App.Pages;

public sealed partial class SettingsPage : Page
{
    bool _suppressLimitSoundsSave;
    bool _suppressStartupSave;
    CancellationTokenSource? _fwCts;
    DispatcherQueue? _dq;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
    }

    void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _dq = DispatcherQueue.GetForCurrentThread();

        _suppressStartupSave = true;
        StartupToggle.IsOn = StartupRegistration.IsRunAtLoginEnabled();
        _suppressStartupSave = false;

        _suppressLimitSoundsSave = true;
        LimitSystemSoundsCheck.IsChecked = MixrRuntimeState.Config.Current.LimitSystemSoundsTo20Percent;
        _suppressLimitSoundsSave = false;

        CfgHint.Text =
            $"config.yaml: {MixrConfigPaths.ConfigYamlPath}{Environment.NewLine}" +
            $"Spielekatalog und Cover: {GameCatalogPaths.AppDataRoot}{Environment.NewLine}" +
            "Der Programmordner wird bei Updates ersetzt — alle Einstellungen liegen deshalb hier.";

        var (id, _) = IgdbCredentialResolver.GetFileValues();
        IgdbClientIdBox.Text = id ?? "";
        IgdbClientSecretBox.Password = "";
        RefreshIgdbStatus();

        AppUpdateService.StateChanged += OnUpdateStateChanged;
        MixrRuntimeState.DeviceChanged += OnDeviceChanged;
        MixrRuntimeState.EspConnectionChanged += OnDeviceChanged;
        FirmwareUpdateCoordinator.BusyChanged += OnDeviceChanged;

        RefreshUpdateUi();
        RefreshFirmwareUi();
    }

    void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        AppUpdateService.StateChanged -= OnUpdateStateChanged;
        MixrRuntimeState.DeviceChanged -= OnDeviceChanged;
        MixrRuntimeState.EspConnectionChanged -= OnDeviceChanged;
        FirmwareUpdateCoordinator.BusyChanged -= OnDeviceChanged;
    }

    // ---- App-Updates -------------------------------------------------------------------------

    void OnUpdateStateChanged() => _dq?.TryEnqueue(RefreshUpdateUi);

    void RefreshUpdateUi()
    {
        AppVersionText.Text = $"Installierte Version: {AppUpdateService.CurrentVersion}";
        var st = AppUpdateService.State;
        InstallUpdateButton.Visibility = st == AppUpdateState.ReadyToInstall ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdatesButton.IsEnabled = st is not (AppUpdateState.Checking or AppUpdateState.Downloading);
        UpdateProgress.Visibility = st == AppUpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.IsIndeterminate = st == AppUpdateState.Checking;
        UpdateProgress.Value = AppUpdateService.DownloadPercent;

        var last = AppUpdateService.LastCheck is { } t ? $" (zuletzt geprüft {t:HH:mm})" : "";
        UpdateStatusText.Text = st switch
        {
            AppUpdateState.NotInstalled => "Entwicklungsstart ohne Installation — automatische Updates sind nur in der per Setup installierten App aktiv.",
            AppUpdateState.Idle => "Mixr ist auf dem neuesten Stand." + last,
            AppUpdateState.Checking => "Suche nach Updates …",
            AppUpdateState.UpdateAvailable => $"Version {AppUpdateService.AvailableVersion} gefunden.",
            AppUpdateState.Downloading => $"Lade Version {AppUpdateService.AvailableVersion} … {AppUpdateService.DownloadPercent} %",
            AppUpdateState.ReadyToInstall => $"Version {AppUpdateService.AvailableVersion} ist heruntergeladen und bereit.",
            AppUpdateState.Error => "Update-Prüfung fehlgeschlagen: " + AppUpdateService.LastError,
            _ => "",
        };
    }

    async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await AppUpdateService.CheckAndDownloadAsync(userInitiated: true);
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Settings/CheckUpdates: " + ex.Message);
        }
    }

    async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            InstallUpdateButton.IsEnabled = false;
            await AppUpdateService.InstallAndRestartAsync();
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Settings/InstallUpdate: " + ex.Message);
            InstallUpdateButton.IsEnabled = true;
        }
    }

    // ---- Firmware ----------------------------------------------------------------------------

    void OnDeviceChanged() => _dq?.TryEnqueue(RefreshFirmwareUi);

    void RefreshFirmwareUi()
    {
        var busy = FirmwareUpdateCoordinator.IsBusy;
        FirmwareStatusText.Text = FirmwareUpdateCoordinator.DescribeState();
        FirmwareUpdateButton.IsEnabled = !busy
            && FirmwareUpdateCoordinator.Bundled != null
            && (MixrRuntimeState.EspConnected || MixrRuntimeState.LastPortName != null);
        FirmwareUpdateButton.Content = FirmwareUpdateCoordinator.UpdateRecommended
            ? $"Firmware auf {FirmwareUpdateCoordinator.Bundled?.Version} aktualisieren"
            : "Firmware neu installieren";
        FirmwareCancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy)
        {
            FirmwareProgress.Visibility = Visibility.Collapsed;
            FirmwareStageText.Visibility = Visibility.Collapsed;
        }
    }

    async void FirmwareUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (FirmwareUpdateCoordinator.IsBusy)
            return;

        var confirm = new ContentDialog
        {
            Title = "Firmware aktualisieren",
            Content = "Das Gerät wird während des Updates neu gestartet und ist kurz nicht bedienbar. " +
                      "USB-Kabel nicht trennen.\n\n" + FirmwareUpdateCoordinator.DescribeState(),
            PrimaryButtonText = "Aktualisieren",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            return;

        _fwCts = new CancellationTokenSource();
        FirmwareProgress.Visibility = Visibility.Visible;
        FirmwareProgress.IsIndeterminate = true;
        FirmwareStageText.Visibility = Visibility.Visible;
        FirmwareStageText.Text = "Starte …";
        FirmwareUpdateButton.IsEnabled = false;
        FirmwareCancelButton.Visibility = Visibility.Visible;

        var progress = new Progress<FirmwareUpdateProgress>(p =>
        {
            FirmwareProgress.IsIndeterminate = false;
            FirmwareProgress.Value = p.Percent;
            FirmwareStageText.Text = p.Stage;
        });

        FirmwareUpdateResult result;
        try
        {
            result = await Task.Run(() => FirmwareUpdateCoordinator.UpdateAsync(progress, _fwCts.Token));
        }
        catch (Exception ex)
        {
            result = new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed, ex.Message);
        }
        finally
        {
            _fwCts.Dispose();
            _fwCts = null;
        }

        FirmwareStageText.Text = result.Message;
        AppLog.WriteLine($"Firmware-Update: {result.Outcome} — {result.Message}");
        RefreshFirmwareUi();
        FirmwareStageText.Visibility = Visibility.Visible;

        if (result.Outcome is FirmwareUpdateOutcome.Failed or FirmwareUpdateOutcome.Unsupported)
        {
            var dlg = new ContentDialog
            {
                Title = "Firmware-Update fehlgeschlagen",
                Content = result.Message + $"\n\nDetails im Log: {AppLog.LogFilePath}",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            };
            await dlg.ShowAsync();
        }
    }

    void FirmwareCancel_Click(object sender, RoutedEventArgs e) => _fwCts?.Cancel();

    // ---- Windows / Audio ---------------------------------------------------------------------

    void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressStartupSave || sender is not ToggleSwitch t)
            return;

        if (!StartupRegistration.SetRunAtLogin(t.IsOn))
        {
            _suppressStartupSave = true;
            t.IsOn = StartupRegistration.IsRunAtLoginEnabled();
            _suppressStartupSave = false;
        }
    }

    void LimitSystemSoundsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressLimitSoundsSave)
            return;

        try
        {
            var cfg = MixrConfigClone.DeepClone(MixrRuntimeState.Config.Current);
            cfg.LimitSystemSoundsTo20Percent = LimitSystemSoundsCheck.IsChecked == true;
            MixrConfigWriter.Save(cfg);
            MixrRuntimeState.ReloadConfigFromDisk(Array.Empty<string>());
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Settings save failed: " + ex.Message);
        }
    }

    // ---- IGDB --------------------------------------------------------------------------------

    void SaveIgdb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var id = IgdbClientIdBox.Text.Trim();
            var secret = IgdbClientSecretBox.Password.Trim();

            // Leeres Secret-Feld = vorhandenes Secret behalten (Passwortfeld wird nie vorbefüllt).
            if (secret.Length == 0)
                secret = IgdbCredentialResolver.GetFileValues().clientSecret ?? "";

            IgdbCredentialResolver.WriteSecretsFile(MixrConfigPaths.SecretsYamlPath, id, secret);
            IgdbClientSecretBox.Password = "";
            RefreshIgdbStatus("Gespeichert.");
        }
        catch (Exception ex)
        {
            RefreshIgdbStatus("Speichern fehlgeschlagen: " + ex.Message);
        }
    }

    void RefreshIgdbStatus(string? prefix = null)
    {
        var (id, secret) = IgdbCredentialResolver.Resolve();
        var state = id != null && secret != null
            ? "Zugangsdaten vorhanden — Cover-Suche aktiv."
            : "Keine vollständigen Zugangsdaten — es werden nur Steam-Cover und manuelle Bilder verwendet.";
        IgdbStatusText.Text = prefix is null ? state : prefix + " " + state;
    }

    // ---- Ordner ------------------------------------------------------------------------------

    void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(MixrConfigPaths.DataRoot);

    void OpenLogFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(AppLog.LogDirectory);

    static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("OpenFolder: " + ex.Message);
        }
    }
}
