using Grimoire.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grimoire.Game.Data
{
    public class Inventory
    {
        public List<InventoryItem> Items => Flash.Call<List<InventoryItem>>("GetInventoryItems", new string[0]);

        /// <summary>
        /// Loads the full list tolerantly: parses the raw response item by
        /// item so a single corrupt entry (bad chars in a name/desc) cannot
        /// nuke the whole inventory. This is what forge-enhancement lookups
        /// (Arcana, Valiance, ...) need, since the single-name bridge can
        /// never resolve an enhancement. Returns null when the response is
        /// empty or truncated beyond repair.
        /// </summary>
        public async Task<List<InventoryItem>> LoadItemsTolerantAsync(int attempts = 3, int delayMs = 500)
        {
            return await LoadListTolerantAsync("GetInventoryItems", attempts, delayMs).ConfigureAwait(false);
        }

        /// <summary>
        /// Shared tolerant list fetch for any Flash function returning a JSON
        /// array of InventoryItem (GetInventoryItems, GetBankItems).
        /// Results are cached briefly: the game-side data barely changes, so
        /// consecutive checks/equips reuse one fetch instead of hammering
        /// the bridge. Call InvalidateTolerantCache after equipping.
        /// </summary>
        private static readonly object _tolerantCacheLock = new object();
        private static readonly System.Collections.Generic.Dictionary<string, Tuple<System.Collections.Generic.List<InventoryItem>, long>> _tolerantCache =
            new System.Collections.Generic.Dictionary<string, Tuple<System.Collections.Generic.List<InventoryItem>, long>>(System.StringComparer.OrdinalIgnoreCase);
        private const long TolerantCacheTtlTicks = 5L * 10000000L; // 5s

        public static void InvalidateTolerantCache()
        {
            try { lock (_tolerantCacheLock) { _tolerantCache.Clear(); } } catch { }
        }

        public static async Task<List<InventoryItem>> LoadListTolerantAsync(string function, int attempts = 3, int delayMs = 500)
        {
            try
            {
                lock (_tolerantCacheLock)
                {
                    Tuple<List<InventoryItem>, long> cached;
                    if (_tolerantCache.TryGetValue(function, out cached) && cached != null &&
                        cached.Item1 != null && cached.Item1.Count > 0 &&
                        DateTime.UtcNow.Ticks - cached.Item2 < TolerantCacheTtlTicks)
                        return cached.Item1;
                }
            }
            catch { }
            for (int a = 0; a < attempts; a++)
            {
                try
                {
                    string raw = Flash.CallRawJson(function);
                    var parsed = ParseItemsTolerant(raw);
                    if (parsed != null && parsed.Count > 0)
                    {
                        try { Grimoire.UI.LogForm.Instance.AppendDebug($"[Equip] Tolerant list recovered: {parsed.Count} items via {function} (attempt {a + 1})"); } catch { }
                        try
                        {
                            lock (_tolerantCacheLock)
                            {
                                _tolerantCache[function] = Tuple.Create(parsed, DateTime.UtcNow.Ticks);
                            }
                        }
                        catch { }
                        return parsed;
                    }
                }
                catch { }
                if (a < attempts - 1)
                {
                    try { await Task.Delay(delayMs).ConfigureAwait(false); } catch { }
                }
            }
            return null;
        }

        private static List<InventoryItem> ParseItemsTolerant(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // 1) Per-item conversion: one bad apple can't spoil the bunch.
            try
            {
                JArray arr = JArray.Parse(raw);
                var list = new List<InventoryItem>(arr.Count);
                int bad = 0;
                foreach (JToken token in arr)
                {
                    try
                    {
                        InventoryItem item = token.ToObject<InventoryItem>();
                        if (item != null)
                            list.Add(item);
                    }
                    catch { bad++; }
                }
                if (list.Count > 0)
                {
                    if (bad > 0)
                    {
                        try { Grimoire.UI.LogForm.Instance.AppendDebug($"[Equip] Tolerant parse: {list.Count} ok, {bad} bad entries skipped"); } catch { }
                    }
                    return list;
                }
            }
            catch { }

            // 2) Whole-list deserialize with per-error tolerance.
            try
            {
                var errors = 0;
                var list = JsonConvert.DeserializeObject<List<InventoryItem>>(raw,
                    new JsonSerializerSettings
                    {
                        Error = (sender, args) => { errors++; args.ErrorContext.Handled = true; }
                    });
                if (list != null && list.Count > 0)
                    return list;
            }
            catch { }

            return null;
        }

        public InventoryItem GetItemByName(string name)
        {
            return Flash.Call<InventoryItem>("GetInventoryItemByName", name);
        }

        public int MaxSlots => Flash.Call<int>("InventorySlots", new string[0]);

        public int UsedSlots => Flash.Call<int>("UsedInventorySlots", new string[0]);

        public int AvailableSlots => MaxSlots - UsedSlots;

        public bool ContainsItemX(string name, string quantity)
        {
            InventoryItem inventoryItem = Items.FirstOrDefault((InventoryItem i) => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (inventoryItem != null)
            {
                if (!(quantity == "*"))
                {
                    return inventoryItem.Quantity >= int.Parse(quantity);
                }
                return true;
            }
            return false;
        }

        public bool ContainsItem(string itemName, string quantity = "*")
        {
            InventoryItem item = Player.Inventory.GetItemByName(itemName);
            if (item == null)
            {
                return false;
            }
            else
            {
                if (Int32.TryParse(quantity, out int qty))
                    if (item.Quantity < qty)
                        return false;
            }
            return true;
        }

        public bool ContainsMaxItem(string name)
        {
            InventoryItem inventoryItem = Items.FirstOrDefault((InventoryItem i) => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (inventoryItem != null)
            {
                return inventoryItem.Quantity >= inventoryItem.MaxStack;
            }
            return false;
        }
    }
}
