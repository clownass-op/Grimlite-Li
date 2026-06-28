using Grimoire.Game;
using Grimoire.Game.Data;
using Grimoire.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Grimoire.Tools
{
    public static class MapItemFinder
    {
        private static readonly Regex RemoveLetter = new Regex(@"[^0-9]", RegexOptions.Compiled);
        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Grimoire/1.0");
            return client;
        }
        private static readonly string CachePath = Path.Combine(Application.StartupPath, "cache");
        private static readonly string SavedCacheFilePath = Path.Combine(CachePath, "0SavedMaps.json");
        private static readonly string SavedQuestCacheFilePath = Path.Combine(CachePath, "0SavedMapQuests.json");
        private static Dictionary<string, List<MapItem>> _savedMapItems = LoadSavedMapItems() ?? new Dictionary<string, List<MapItem>>();
        private static Dictionary<string, HashSet<int>> _savedMapQuestIds = LoadSavedMapQuestIds() ?? new Dictionary<string, HashSet<int>>();

        public static List<MapItem> FindMapItems(bool forceRefresh = false)
        {
            string filePath = ResolveMapFilePath();
            if (string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show(
                    "Map SWF path is unknown. Join a map first so the client can capture the map file name.",
                    "Get Map Item IDs",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return null;
            }

            string fileName = GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
                return null;

            if (!Directory.Exists(CachePath))
                Directory.CreateDirectory(CachePath);

            string ffdecDir = FindFFDecDirectory();
            if (ffdecDir == null)
            {
                MessageBox.Show(
                    "FFDec folder not found. Place FFDec next to Grimlite or use bin\\Skua\\FFDec from the Skua repo.",
                    "Get Map Item IDs",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return null;
            }

            if (!forceRefresh && _savedMapItems.TryGetValue(fileName, out List<MapItem> cached))
                return cached;

            string swfPath = Path.Combine(CachePath, fileName);
            if (!File.Exists(swfPath) && !DownloadMapSwf(filePath, swfPath))
                return null;

            if (!DecompileSwf(swfPath, ffdecDir))
            {
                MessageBox.Show(
                    "FFDec failed to decompile the map SWF. Check dev logs for details.",
                    "Get Map Item IDs",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return null;
            }

            List<MapItem> items = ParseMapSwfData(filePath, fileName);
            
            // Add dynamically discovered items from server packets
            if (items != null)
            {
                lock (Player.recentMapItem)
                {
                    foreach (var kvp in Player.recentMapItem)
                    {
                        if (!items.Any(i => i.Id == kvp.Key))
                        {
                            items.Add(new MapItem
                            {
                                Id = kvp.Key,
                                QuestId = 0, // Unknown from packet
                                MapFilePath = filePath,
                                MapName = Player.Map
                            });
                        }
                    }
                }
            }

            if (items != null && items.Count > 0)
                SaveMapItemInfo(fileName, items);

            return items;
        }

        public static void ClearCache(string specificMap = null)
        {
            if (!string.IsNullOrEmpty(specificMap))
            {
                _savedMapItems.Remove(specificMap);
                File.WriteAllText(SavedCacheFilePath, JsonConvert.SerializeObject(_savedMapItems, Formatting.Indented));
                return;
            }

            _savedMapItems.Clear();
            if (File.Exists(SavedCacheFilePath))
                File.Delete(SavedCacheFilePath);
        }

        private static string ResolveMapFilePath()
        {
            if (!string.IsNullOrEmpty(World.MapFilePath))
                return World.MapFilePath;

            try
            {
                string fromFlash = Flash.GetGameObject("world.strMapFileName")?.Trim('"', ' ', '\r', '\n');
                if (!string.IsNullOrEmpty(fromFlash))
                {
                    World.MapFilePath = fromFlash;
                    return fromFlash;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string GetFileName(string filePath)
        {
            return filePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        }

        private static string FindFFDecDirectory()
        {
            string[] candidates =
            {
                Path.Combine(Application.StartupPath, "FFDec"),
                Path.GetFullPath(Path.Combine(Application.StartupPath, "..", "Skua", "FFDec")),
                Path.GetFullPath(Path.Combine(Application.StartupPath, "..", "..", "bin", "Skua", "FFDec"))
            };

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "ffdec.bat")))
                    return candidate;
            }

            return null;
        }

        private static bool DownloadMapSwf(string filePath, string destination)
        {
            try
            {
                byte[] fileBytes = Task.Run(() => Http.GetByteArrayAsync($"https://game.aq.com/game/gamefiles/maps/{filePath}")).GetAwaiter().GetResult();
                File.WriteAllBytes(destination, fileBytes);
                return File.Exists(destination);
            }
            catch (Exception ex)
            {
                LogForm.Instance?.devDebug($"[MapItemFinder] Failed to download map SWF: {ex.Message}");
                MessageBox.Show($"Failed to download map SWF.\r\n{ex.Message}", "Get Map Item IDs", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static bool DecompileSwf(string swfPath, string ffdecDir)
        {
            string tmpPath = Path.Combine(CachePath, "tmp");
            if (Directory.Exists(tmpPath))
            {
                try { Directory.Delete(tmpPath, true); }
                catch { }
            }

            Process decompile = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    FileName = "cmd.exe",
                    WorkingDirectory = ffdecDir,
                    Arguments = $"/c ffdec.bat -export script \"{tmpPath}\" \"{swfPath}\""
                }
            };

            decompile.Start();
            string error = decompile.StandardError.ReadToEnd();
            decompile.WaitForExit();

            if (!string.IsNullOrEmpty(error))
                LogForm.Instance?.devDebug($"[MapItemFinder] FFDec stderr: {error}");

            return Directory.Exists(tmpPath);
        }

        private static List<MapItem> ParseMapSwfData(string mapFilePath, string fileName)
        {
            List<MapItem> items = new List<MapItem>();
            HashSet<int> mapQuestIds = new HashSet<int>();
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                string scriptsPath = Path.Combine(CachePath, "tmp", "scripts");
                if (!Directory.Exists(scriptsPath))
                    return items;

                // Search ALL .as files in the scripts folder
                string[] asFiles = Directory.GetFiles(scriptsPath, "*.as", SearchOption.AllDirectories);

                // Regex for extracting IDs from common patterns
                Regex getMapItemRegex = new Regex(@"(?i)getmapitem\s*\(\s*(\d+)", RegexOptions.Compiled);
                Regex questProgressRegex = new Regex(@"(?i)isquestinprogress\s*\(\s*(\d+)", RegexOptions.Compiled);
                Regex questStatusRegex = new Regex(@"(?i)getqueststatus\s*\(\s*(\d+)", RegexOptions.Compiled);
                Regex questNumRegex = new Regex(@"(?i)questnum\s*=\s*(\d+)", RegexOptions.Compiled);
                Regex intQuestRegex = new Regex(@"(?i)intquest\s*=\s*(\d+)", RegexOptions.Compiled);
                Regex mapItemAssignRegex = new Regex(@"(?i)mapitem\s*=\s*(\d+)", RegexOptions.Compiled);
                Regex itemDropRegex = new Regex(@"(?i)itemdrop\s*=\s*(\d+)", RegexOptions.Compiled);

                foreach (string file in asFiles)
                {
                    string[] lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];

                        // Collect every quest id referenced anywhere in the SWF so we can
                        // surface quests that don't have a map item attached.
                        foreach (Match qm in questProgressRegex.Matches(line))
                        {
                            if (int.TryParse(qm.Groups[1].Value, out int qid) && qid > 0)
                                mapQuestIds.Add(qid);
                        }
                        foreach (Match qm in questStatusRegex.Matches(line))
                        {
                            if (int.TryParse(qm.Groups[1].Value, out int qid) && qid > 0)
                                mapQuestIds.Add(qid);
                        }
                        foreach (Match qm in questNumRegex.Matches(line))
                        {
                            if (int.TryParse(qm.Groups[1].Value, out int qid) && qid > 0)
                                mapQuestIds.Add(qid);
                        }
                        foreach (Match qm in intQuestRegex.Matches(line))
                        {
                            if (int.TryParse(qm.Groups[1].Value, out int qid) && qid > 0)
                                mapQuestIds.Add(qid);
                        }

                        Match mapItemMatch = getMapItemRegex.Match(line);
                        bool isAssignment = false;

                        if (!mapItemMatch.Success)
                        {
                            mapItemMatch = mapItemAssignRegex.Match(line);
                            if (!mapItemMatch.Success)
                                mapItemMatch = itemDropRegex.Match(line);
                            isAssignment = mapItemMatch.Success;
                        }

                        if (mapItemMatch.Success && int.TryParse(mapItemMatch.Groups[1].Value, out int mapItemId))
                        {
                            // Found a map item ID! Now look for a Quest ID nearby (up to 20 lines before or after)
                            int questIdValue = 0;
                            int start = Math.Max(0, i - 20);
                            int end = Math.Min(lines.Length - 1, i + 20);

                            for (int j = start; j <= end; j++)
                            {
                                string searchLine = lines[j];
                                Match qMatch = questProgressRegex.Match(searchLine);
                                if (!qMatch.Success) qMatch = questStatusRegex.Match(searchLine);
                                if (!qMatch.Success) qMatch = questNumRegex.Match(searchLine);
                                if (!qMatch.Success) qMatch = intQuestRegex.Match(searchLine);

                                if (qMatch.Success && int.TryParse(qMatch.Groups[1].Value, out int qid))
                                {
                                    questIdValue = qid;
                                    break;
                                }
                            }

                            if (mapItemId > 0)
                                AddMapItem(items, mapItemId, questIdValue, mapFilePath);
                        }
                    }
                }

                try { Directory.Delete(Path.Combine(CachePath, "tmp"), true); }
                catch { }
            }
            catch (Exception ex)
            {
                LogForm.Instance?.devDebug($"[MapItemFinder] Parse error: {ex.Message}");
            }

            sw.Stop();
            LogForm.Instance?.devDebug($"[MapItemFinder] Parsing took {sw.Elapsed.TotalSeconds:0.00}s, found {items.Count} item(s), {mapQuestIds.Count} quest(s).");

            // Stash the discovered quest ids on the cache entry so Grabber can read them later.
            if (mapQuestIds.Count > 0)
            {
                _mapQuestIdsByFile[fileName] = mapQuestIds;
                _savedMapQuestIds[fileName] = new HashSet<int>(mapQuestIds);
                SaveMapQuestInfo();
            }
            else
            {
                _mapQuestIdsByFile.Remove(fileName);
            }

            return items.OrderBy(item => item.Id).ToList();
        }

        private static readonly Dictionary<string, HashSet<int>> _mapQuestIdsByFile = new Dictionary<string, HashSet<int>>();

        public static HashSet<int> GetMapQuestIds(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return new HashSet<int>();

            // Prefer the in-memory cache populated by a fresh scan, but fall back
            // to the persisted cache so completed quests still show up after a
            // restart or when the map items were loaded from disk.
            if (_mapQuestIdsByFile.TryGetValue(fileName, out HashSet<int> ids))
                return new HashSet<int>(ids);

            if (_savedMapQuestIds.TryGetValue(fileName, out HashSet<int> saved))
                return new HashSet<int>(saved);

            return new HashSet<int>();
        }

        private static void AddMapItem(List<MapItem> items, int mapItemId, int questId, string mapFilePath)
        {
            if (items.Any(item => item.Id == mapItemId))
                return;

            items.Add(new MapItem
            {
                Id = mapItemId,
                QuestId = questId,
                MapFilePath = mapFilePath,
                MapName = Player.Map
            });
        }

        private static void SaveMapItemInfo(string fileName, List<MapItem> info)
        {
            _savedMapItems[fileName] = info;
            File.WriteAllText(SavedCacheFilePath, JsonConvert.SerializeObject(_savedMapItems, Formatting.Indented));
        }

        private static void SaveMapQuestInfo()
        {
            try
            {
                File.WriteAllText(SavedQuestCacheFilePath, JsonConvert.SerializeObject(_savedMapQuestIds, Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogForm.Instance?.devDebug($"[MapItemFinder] Failed to save quest cache: {ex.Message}");
            }
        }

        private static Dictionary<string, List<MapItem>> LoadSavedMapItems()
        {
            if (!File.Exists(SavedCacheFilePath))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, List<MapItem>>>(File.ReadAllText(SavedCacheFilePath));
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, HashSet<int>> LoadSavedMapQuestIds()
        {
            if (!File.Exists(SavedQuestCacheFilePath))
                return null;

            try
            {
                // HashSet<int> serializes as a JSON array; convert each list back to a HashSet.
                Dictionary<string, List<int>> raw = JsonConvert.DeserializeObject<Dictionary<string, List<int>>>(File.ReadAllText(SavedQuestCacheFilePath));
                if (raw == null)
                    return null;
                Dictionary<string, HashSet<int>> result = new Dictionary<string, HashSet<int>>();
                foreach (KeyValuePair<string, List<int>> kvp in raw)
                    result[kvp.Key] = new HashSet<int>(kvp.Value);
                return result;
            }
            catch
            {
                return null;
            }
        }
    }
}
