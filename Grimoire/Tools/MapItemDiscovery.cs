using Grimoire.Game;
using Grimoire.Game.Data;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Grimoire.Tools
{
    public static class MapItemDiscovery
    {
        private static readonly HashSet<string> NameKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sName", "name", "nam"
        };

        private static readonly HashSet<string> ItemIdKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ItemID", "itemId"
        };

        private static readonly HashSet<string> ExcludedIdKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ItemID", "itemId", "ShopItemID", "shopItemId", "CharItemID", "charItemId",
            "QuestID", "questId", "FactionID", "factionId", "UserID", "userId"
        };

        public static void ProcessServerPacket(string packet)
        {
            if (string.IsNullOrWhiteSpace(packet) || !packet.StartsWith("{"))
                return;

            try
            {
                JToken token = JToken.Parse(packet);

                if (token is JObject rootObject)
                    TryRegisterFromObject(rootObject);

                if (token is JContainer container)
                {
                    foreach (JObject obj in container.Descendants().OfType<JObject>())
                        TryRegisterFromObject(obj);
                }
            }
            catch
            {
            }
        }

        private static void TryRegisterFromObject(JObject obj)
        {
            string itemName = GetName(obj);
            if (string.IsNullOrWhiteSpace(itemName))
                return;

            if (!TryGetInt(obj, ItemIdKeys, out int itemId) || itemId <= 0)
                return;

            foreach (JProperty prop in obj.Properties())
            {
                if (!LooksLikeAlternateId(prop.Name))
                    continue;

                if (!TryGetInt(prop.Value, out int mapItemId) || mapItemId <= 0 || mapItemId == itemId)
                    continue;

                Player.RegisterMapItem(mapItemId, new InventoryItem
                {
                    Id = itemId,
                    Name = itemName,
                    MapItemId = mapItemId
                });

                UI.LogForm.Instance?.devDebug($"[MapItemDiscovery] Discovered map item from server packet: {itemName} -> {mapItemId} (ItemID {itemId}, key {prop.Name})");
                return;
            }
        }

        private static string GetName(JObject obj)
        {
            foreach (JProperty prop in obj.Properties())
            {
                if (NameKeys.Contains(prop.Name) && prop.Value.Type == JTokenType.String)
                {
                    string value = prop.Value.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            return null;
        }

        private static bool LooksLikeAlternateId(string key)
        {
            if (ExcludedIdKeys.Contains(key))
                return false;

            return key.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetInt(JObject obj, IEnumerable<string> keys, out int value)
        {
            foreach (string key in keys)
            {
                JProperty prop = obj.Properties().FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (prop != null && TryGetInt(prop.Value, out value))
                    return true;
            }

            value = 0;
            return false;
        }

        private static bool TryGetInt(JToken token, out int value)
        {
            value = 0;
            if (token == null)
                return false;

            if (token.Type == JTokenType.Integer)
            {
                value = token.Value<int>();
                return true;
            }

            if (token.Type == JTokenType.String)
                return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

            return false;
        }
    }
}
