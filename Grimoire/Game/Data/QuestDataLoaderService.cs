using Grimoire.FlashTools;
using Grimoire.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Grimoire.Game.Data
{
    /// <summary>
    /// Loads quest data from the live game (via <see cref="Flash.Call{T}"/>) and
    /// persists it as JSON in <see cref="ClientFileSources.GrimliteQuestsFile"/>.
    /// Modeled after Skua's <c>QuestDataLoaderService</c> but simplified because
    /// Grimlite's <c>GetQuestTree</c> already returns the full quest list in one
    /// call - no batching required.
    /// </summary>
    public class QuestDataLoaderService
    {
        private static readonly JsonSerializerSettings _serializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        private readonly Dictionary<string, List<Quest>> _cachedQuests = new();

        /// <summary>
        /// Clears the in-memory cache so the next read pulls fresh data from disk.
        /// </summary>
        public void ClearCache()
        {
            _cachedQuests.Clear();
        }

        /// <summary>
        /// Reads the cached quest list from <see cref="ClientFileSources.GrimliteQuestsFile"/>.
        /// Returns an empty list if the file does not exist.
        /// </summary>
        public async Task<List<Quest>> GetFromFileAsync()
        {
            return await GetFromFileAsync(ClientFileSources.GrimliteQuestsFile);
        }

        /// <summary>
        /// Reads the cached quest list from an arbitrary file path.
        /// </summary>
        public async Task<List<Quest>> GetFromFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                filePath = ClientFileSources.GrimliteQuestsFile;

            if (_cachedQuests.TryGetValue(filePath, out List<Quest> cached))
                return cached ?? new List<Quest>();

            if (!File.Exists(filePath))
                return new List<Quest>();

            string text = await Task.Run(() => File.ReadAllText(filePath));
            List<QuestSaveData> saveDataList = JsonConvert.DeserializeObject<List<QuestSaveData>>(text, _serializerSettings)
                                                ?? new List<QuestSaveData>();
            List<Quest> quests = saveDataList.Select(FromSaveData).ToList();

            _cachedQuests[filePath] = quests;
            return quests;
        }

        /// <summary>
        /// Pulls the full quest tree from the game, merges it with any existing
        /// cached quests (so we never lose data), and writes the result to disk.
        /// </summary>
        /// <param name="progress">Optional progress reporter for status messages.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task<List<Quest>> UpdateAsync(
            IProgress<string> progress = null,
            CancellationToken token = default)
        {
            return await Task.Run(async () =>
            {
                ClientFileSources.EnsureDirectoriesExist();
                string filePath = ClientFileSources.GrimliteQuestsFile;

                progress?.Report("Loading quests from game...");
                List<Quest> gameQuests = SafeGetQuestTree();
                if (gameQuests == null || gameQuests.Count == 0)
                {
                    progress?.Report("No quests returned by the game. Returning cached file (if any).");
                    return await GetFromFileAsync(filePath);
                }

                progress?.Report($"Got {gameQuests.Count} quests from game. Merging with cache...");

                // Merge with existing data so we never lose quests the game didn't return.
                List<Quest> existing = await GetFromFileAsync(filePath);
                HashSet<int> gameIds = new HashSet<int>(gameQuests.Select(q => q.Id));
                List<Quest> merged = new List<Quest>(gameQuests);
                merged.AddRange(existing.Where(q => !gameIds.Contains(q.Id)));

                List<Quest> ordered = merged
                    .GroupBy(q => q.Id)
                    .Select(g => g.First())
                    .OrderBy(q => q.Id)
                    .ToList();

                progress?.Report($"Writing {ordered.Count} quests to {filePath}...");
                string serialized;
                try
                {
                    List<QuestSaveData> saveDataList = ordered.Select(ToSaveData).ToList();
                    serialized = JsonConvert.SerializeObject(saveDataList, _serializerSettings);
                }
                catch (Exception ex)
                {
                    progress?.Report($"Could not serialize merged quests ({ex.Message}). Saving game quests only.");
                    List<QuestSaveData> saveDataList = gameQuests.OrderBy(q => q.Id).Select(ToSaveData).ToList();
                    serialized = JsonConvert.SerializeObject(saveDataList, _serializerSettings);
                }
                await Task.Run(() => File.WriteAllText(filePath, serialized), token);

                // Bust cache so the next read picks up the freshly written file.
                _cachedQuests.Remove(filePath);

                progress?.Report("Done.");
                return await GetFromFileAsync(filePath);
            }, token);
        }

        private static QuestSaveData ToSaveData(Quest quest)
        {
            return new QuestSaveData
            {
                Id = quest.Id,
                Name = quest.Name,
                Description = quest.Description,
                ISlot = quest.ISlot,
                IValue = quest.IValue,
                IsNotRepeatable = quest.IsNotRepeatable,
                IsMemberOnly = quest.IsMemberOnly,
                Level = quest.Level,
                GoldReward = quest.GoldReward,
                ExperienceReward = quest.ExperienceReward,
                ReputationReward = quest.ReputationReward,
                RequiredReputation = quest.RequiredReputation,
                RequiredClassPoints = quest.RequiredClassPoints,
                ClassPointsReward = quest.ClassPointsReward,
                FactionId = quest.FactionId,
                Faction = quest.Faction,
                RequiredItems = quest.RequiredItems?.Select(ToSaveData).ToList(),
                Rewards = quest.Rewards?.Select(ToSaveData).ToList()
            };
        }

        private static InventoryItemSaveData ToSaveData(InventoryItem item)
        {
            return new InventoryItemSaveData
            {
                Id = item.Id,
                Name = item.Name,
                Quantity = item.Quantity,
                MaxStack = item.MaxStack,
                Level = item.Level,
                Cost = item.Cost,
                Description = item.Description,
                Category = item.Category,
                File = item.File,
                Link = item.Link,
                IsAcItem = item.IsAcItem,
                IsMemberOnly = item.IsMemberOnly,
                IsTemporary = item.IsTemporary,
                Enhancement = item.Enhancement,
                ShopItemId = item.ShopItemId,
                DropChance = item.DropChance
            };
        }

        private static Quest FromSaveData(QuestSaveData saveData)
        {
            return new Quest
            {
                Id = saveData.Id,
                Name = saveData.Name,
                Description = saveData.Description,
                ISlot = saveData.ISlot,
                IValue = saveData.IValue,
                IsNotRepeatable = saveData.IsNotRepeatable,
                IsMemberOnly = saveData.IsMemberOnly,
                Level = saveData.Level,
                GoldReward = saveData.GoldReward,
                ExperienceReward = saveData.ExperienceReward,
                ReputationReward = saveData.ReputationReward,
                RequiredReputation = saveData.RequiredReputation,
                RequiredClassPoints = saveData.RequiredClassPoints,
                ClassPointsReward = saveData.ClassPointsReward,
                FactionId = saveData.FactionId,
                Faction = saveData.Faction,
                RequiredItems = saveData.RequiredItems?.Select(FromSaveData).ToList(),
                Rewards = saveData.Rewards?.Select(FromSaveData).ToList()
            };
        }

        private static InventoryItem FromSaveData(InventoryItemSaveData saveData)
        {
            return new InventoryItem
            {
                Id = saveData.Id,
                Name = saveData.Name,
                Quantity = saveData.Quantity,
                MaxStack = saveData.MaxStack,
                Level = saveData.Level,
                Cost = saveData.Cost,
                Description = saveData.Description,
                Category = saveData.Category,
                File = saveData.File,
                Link = saveData.Link,
                IsAcItem = saveData.IsAcItem,
                IsMemberOnly = saveData.IsMemberOnly,
                IsTemporary = saveData.IsTemporary,
                Enhancement = saveData.Enhancement,
                ShopItemId = saveData.ShopItemId,
                DropChance = saveData.DropChance
            };
        }

        /// <summary>
        /// Appends any quests from <paramref name="newQuests"/> that are not
        /// already present in the cached JSON file (matched by <see cref="Quest.Id"/>).
        /// Returns the number of quests that were actually added.
        /// </summary>
        public async Task<int> AppendMissingQuestsAsync(IEnumerable<Quest> newQuests)
        {
            LogForm.Instance?.AppendDebug("[QuestDataLoaderService] Starting AppendMissingQuestsAsync...");
            
            if (newQuests == null)
            {
                LogForm.Instance?.AppendDebug("[QuestDataLoaderService] newQuests is null, returning 0");
                return 0;
            }

            ClientFileSources.EnsureDirectoriesExist();
            string filePath = ClientFileSources.GrimliteQuestsFile;
            
            LogForm.Instance?.AppendDebug($"[QuestDataLoaderService] Quest file path: {filePath}");

            List<Quest> incoming = newQuests
                .Where(q => q != null && q.Id > 0)
                .ToList();
                
            LogForm.Instance?.AppendDebug($"[QuestDataLoaderService] Incoming valid quests: {incoming.Count}");
            
            if (incoming.Count == 0)
            {
                LogForm.Instance?.AppendDebug("[QuestDataLoaderService] No valid incoming quests, returning 0");
                return 0;
            }

            return await Task.Run(() =>
            {
                List<QuestSaveData> existingSaveData = new List<QuestSaveData>();
                if (File.Exists(filePath))
                {
                    try
                    {
                        existingSaveData = JsonConvert.DeserializeObject<List<QuestSaveData>>(
                            File.ReadAllText(filePath), _serializerSettings) ?? new List<QuestSaveData>();
                        LogForm.Instance?.AppendDebug($"[QuestDataLoaderService] Successfully loaded {existingSaveData.Count} existing quests from file");
                    }
                    catch (Exception ex)
                    {
                        LogForm.Instance?.AppendDebug($"[QuestDataLoaderService] WARNING: Failed to load existing quest file! Starting fresh. Error: {ex.Message}");
                        LogForm.Instance?.AppendDebug($"[QuestDataLoaderService] Stack trace: {ex.StackTrace}");
                        existingSaveData = new List<QuestSaveData>();
                    }
                }
                else
                {
                    LogForm.Instance?.AppendDebug("[QuestDataLoaderService] Quest file doesn't exist yet, creating new one");
                }
                    
                LogForm.Instance?.AppendDebug($"[QuestDataLoaderService] Existing quests to use: {existingSaveData.Count}");

                HashSet<int> existingIds = new HashSet<int>(existingSaveData.Select(q => q.Id));
                List<Quest> toAdd = incoming
                    .Where(q => !existingIds.Contains(q.Id))
                    .ToList();
                    
                LogForm.Instance?.AppendDebug($"[QuestDataLoaderService] New quests to add: {toAdd.Count}");
                
                if (toAdd.Count > 0)
                {
                    foreach (var quest in toAdd)
                    {
                        LogForm.Instance?.AppendDebug($"[QuestDataLoaderService] Adding quest: {quest.Id} - {quest.Name}");
                        
                        // Log required items count
                        if (quest.RequiredItems != null && quest.RequiredItems.Count > 0)
                        {
                            LogForm.Instance?.AppendDebug($"  - Required items: {quest.RequiredItems.Count}");
                            foreach (var item in quest.RequiredItems)
                            {
                                LogForm.Instance?.AppendDebug($"    - {item.Name} (ID: {item.Id}, Qty: {item.Quantity})");
                            }
                        }
                        
                        // Log rewards count
                        if (quest.Rewards != null && quest.Rewards.Count > 0)
                        {
                            LogForm.Instance?.AppendDebug($"  - Rewards: {quest.Rewards.Count}");
                            foreach (var item in quest.Rewards)
                            {
                                LogForm.Instance?.AppendDebug($"    - {item.Name} (ID: {item.Id}, Qty: {item.Quantity}, Drop Rate: {item.DropChance})");
                            }
                        }

                        // Convert to safe save data and add
                        existingSaveData.Add(ToSaveData(quest));
                    }

                    List<QuestSaveData> ordered = existingSaveData
                        .GroupBy(q => q.Id)
                        .Select(g => g.First())
                        .OrderBy(q => q.Id)
                        .ToList();

                    string serialized = JsonConvert.SerializeObject(ordered, _serializerSettings);
                    File.WriteAllText(filePath, serialized);
                    LogForm.Instance?.AppendDebug($"[QuestDataLoaderService] Successfully saved {toAdd.Count} new quests to {filePath}");

                    // Bust cache so the next read picks up the freshly written file.
                    _cachedQuests.Remove(filePath);
                    return toAdd.Count;
                }
                else
                {
                    LogForm.Instance?.AppendDebug("[QuestDataLoaderService] No new quests to add, file unchanged");
                    return 0;
                }
            });
        }

        /// <summary>
        /// One-time cleanup: reads the cached JSON file, collapses any duplicate
        /// entries (matched by <see cref="Quest.Id"/>) into a single record per Id,
        /// keeping whichever copy has the most populated fields, and writes the
        /// result back. Returns the number of duplicates that were removed.
        /// Safe to call repeatedly - no-op if the file is already deduped.
        /// </summary>
        public async Task<int> DedupeFileAsync()
        {
            return await DedupeFileAsync(ClientFileSources.GrimliteQuestsFile);
        }

        /// <summary>
        /// One-time cleanup variant that operates on an arbitrary file path.
        /// </summary>
        public async Task<int> DedupeFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                filePath = ClientFileSources.GrimliteQuestsFile;
            if (!File.Exists(filePath))
                return 0;

            return await Task.Run(() =>
            {
                List<Quest> existing = JsonConvert.DeserializeObject<List<Quest>>(
                    File.ReadAllText(filePath), _serializerSettings) ?? new List<Quest>();

                int before = existing.Count;
                if (before == 0)
                    return 0;

                // Group by Id, keep the entry with the most populated fields.
                // "Most populated" = highest count of non-default/non-empty
                // properties, so a fully-described quest wins over a stub.
                List<Quest> deduped = existing
                    .GroupBy(q => q.Id)
                    .Select(g => g.OrderByDescending(ScoreCompleteness).First())
                    .OrderBy(q => q.Id)
                    .ToList();

                int removed = before - deduped.Count;
                if (removed == 0)
                    return 0;

                string serialized = JsonConvert.SerializeObject(deduped, _serializerSettings);
                File.WriteAllText(filePath, serialized);

                // Bust cache so the next read picks up the freshly written file.
                _cachedQuests.Remove(filePath);
                return removed;
            });
        }

        /// <summary>
        /// Heuristic score: how "complete" a quest record looks. Higher = better.
        /// Used to pick which duplicate copy to keep during dedup.
        /// </summary>
        private static int ScoreCompleteness(Quest q)
        {
            if (q == null) return 0;
            int score = 0;
            if (!string.IsNullOrEmpty(q.Name)) score++;
            if (q.ISlot.HasValue && q.ISlot.Value > 0) score++;
            if (q.IValue > 0) score++;
            if (!string.IsNullOrEmpty(q.Description)) score++;
            if (q.RequiredItems != null && q.RequiredItems.Count > 0) score += 2;
            if (q.Rewards != null && q.Rewards.Count > 0) score += 2;
            if (q.oRewards != null && q.oRewards.Count > 0) score++;
            return score;
        }

        /// <summary>
        /// Copies a quest JSON file from a source path into Grimlite's AppData
        /// folder. Useful for seeding the cache from Skua's QuestData.json.
        /// Auto-detects Skua's PascalCase format (ID/Slot/Value/Once/XP/...) and
        /// maps it onto Grimlite's <see cref="Quest"/> model.
        /// </summary>
        public async Task<List<Quest>> ImportFromFileAsync(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Source quest file not found.", sourcePath);

            ClientFileSources.EnsureDirectoriesExist();
            string dest = ClientFileSources.GrimliteQuestsFile;

            string text = await Task.Run(() => File.ReadAllText(sourcePath));
            List<Quest> quests = DeserializeAny(text);

            string destJson = JsonConvert.SerializeObject(quests.OrderBy(q => q.Id).ToList(), _serializerSettings);
            await Task.Run(() => File.WriteAllText(dest, destJson));

            _cachedQuests.Remove(dest);
            return quests;
        }

        /// <summary>
        /// Reads a quest JSON file in either Grimlite's native format or Skua's
        /// PascalCase format and returns a list of <see cref="Quest"/>.
        /// </summary>
        private static List<Quest> DeserializeAny(string text)
        {
            // Try Grimlite's native format first (uses short ActionScript-style
            // property names like QuestID, sName, iLvl, bOnce, ...).
            try
            {
                List<Quest> native = JsonConvert.DeserializeObject<List<Quest>>(text, _serializerSettings);
                if (native != null && native.Count > 0 && native[0].Id != 0)
                    return native;
            }
            catch
            {
                // fall through to Skua format
            }

            // Fall back to external PascalCase format.
            List<ExternalQuestData> external = JsonConvert.DeserializeObject<List<ExternalQuestData>>(text);
            if (external == null)
                return new List<Quest>();
            return external.Select(s => s.ToQuest()).ToList();
        }

        private static List<Quest> SafeGetQuestTree()
        {
            try
            {
                List<Quest> tree = Player.Quests?.QuestTree;
                return tree ?? new List<Quest>();
            }
            catch
            {
                return new List<Quest>();
            }
        }
    }
}
