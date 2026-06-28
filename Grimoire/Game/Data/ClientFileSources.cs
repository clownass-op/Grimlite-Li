using System;
using System.IO;
using System.Reflection;

namespace Grimoire.Game.Data
{
    /// <summary>
    /// Centralised file-system locations used by Grimlite to persist user data.
    /// Mirrors Skua's <c>ClientFileSources</c> pattern so quest data, scripts and
    /// settings live in a predictable per-user folder under %AppData%.
    /// </summary>
    public static class ClientFileSources
    {
        public static string AssemblyVersion { get; } =
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0.0";

        /// <summary>%AppData%\Grimoire</summary>
        public static string GrimliteDIR { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Grimoire");

        /// <summary>%AppData%\Grimoire\QuestData.json</summary>
        public static string GrimliteQuestsFile { get; } = Path.Combine(GrimliteDIR, "QuestData.json");

        /// <summary>%AppData%\Grimoire\Bots</summary>
        public static string GrimliteBotsDIR { get; } = Path.Combine(GrimliteDIR, "Bots");

        /// <summary>%AppData%\Grimoire\Plugins</summary>
        public static string GrimlitePluginsDIR { get; } = Path.Combine(GrimliteDIR, "Plugins");

        /// <summary>
        /// Ensures the Grimlite AppData folder (and any required sub-folders) exists.
        /// Safe to call repeatedly.
        /// </summary>
        public static void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(GrimliteDIR);
            Directory.CreateDirectory(GrimliteBotsDIR);
            Directory.CreateDirectory(GrimlitePluginsDIR);
        }
    }
}
