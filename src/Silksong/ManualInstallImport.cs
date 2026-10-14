#if SILKSONG_BUILD
using System;
using System.IO;
using BepInEx.Logging;

namespace ReplayTimerMod
{
    // On first launch, copies runs and settings from a manually installed copy in the same plugins folder.
    internal static class ManualInstallImport
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ManualInstallImport");

        private const string DataFolder = "ReplayMod";
        private const string PluginDll = "ReplayTimerMod.SS.dll";

        public static void Run(string pluginDirectory)
        {
            string target = Path.Combine(pluginDirectory, DataFolder);
            if (Directory.Exists(target))
                return;

            string staging = target + ".importing";
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);

                string? source = FindManualInstall();
                if (source == null)
                    return;

                CopyDirectory(source, staging);
                Directory.Move(staging, target);
                Log.LogInfo($"[ManualInstallImport] Imported runs and settings from {source}");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[ManualInstallImport] Import skipped: {ex.Message}");
                try
                {
                    if (Directory.Exists(staging))
                        Directory.Delete(staging, true);
                }
                catch (IOException) { }
            }
        }

        private static string? FindManualInstall()
        {
            string? found = null;
            foreach (string dir in Directory.GetDirectories(
                BepInEx.Paths.PluginPath, DataFolder, SearchOption.AllDirectories))
            {
                string data = Path.Combine(dir, "data");
                string owner = Path.GetDirectoryName(dir) ?? "";
                if (!File.Exists(Path.Combine(owner, PluginDll))
                    || !Directory.Exists(data)
                    || Directory.GetFiles(data, "*.json").Length == 0)
                    continue;

                if (found != null)
                {
                    Log.LogInfo("[ManualInstallImport] Several manual installs found - not importing");
                    return null;
                }
                found = dir;
            }
            return found;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }
}
#endif
