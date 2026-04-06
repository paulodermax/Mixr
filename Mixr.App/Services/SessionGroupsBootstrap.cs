using Mixr.Models;
using Mixr.Services;

namespace Mixr_App.Services;

/// <summary>
/// Nach Katalog-Refresh: <c>session_groups</c> mit Master (leer), Kommunikation, Medien, Spielen füllen — nur additiv, falls Programme/Spiele vorhanden.
/// </summary>
public static class SessionGroupsBootstrap
{
    /// <summary>Nach <see cref="GameCatalogCoordinator.RunStartupAsync"/> aufrufen, damit die Spieleliste aus dem Katalog kommt.</summary>
    public static void RunMergeIfNeeded()
    {
        try
        {
            CatalogIgnoreList.EnsureLauncherIgnoreLines();

            var cfg = MixrConfigClone.DeepClone(MixrConfigLoader.Load(Array.Empty<string>()));
            var changed = false;

            if (SessionGroupsLauncherPrune.RemoveLauncherTokensFromAllGroups(cfg))
                changed = true;

            if (!cfg.SessionGroups.ContainsKey("master"))
            {
                cfg.SessionGroups["master"] = [];
                changed = true;
            }

            if (SessionGroupsAutoMerge.MergeDetectedInto(cfg))
                changed = true;

            if (SessionGroupsCatalogMerge.MergeSteamGamesInto(cfg))
                changed = true;

            if (!changed)
                return;

            MixrConfigWriter.Save(cfg, MixrConfigPaths.ConfigYamlPath);
            MixrRuntimeState.ReloadConfigFromDisk(Array.Empty<string>());
            AppLog.WriteLine("Auto-detect: session_groups updated (master · communication · media · games — see config.yaml).");
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("SessionGroupsBootstrap: " + ex.Message);
        }
    }
}
