using Grimoire.Game;
using Grimoire.Game.Data;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace Grimoire.Networking.Handlers
{
    public class HandlerAddItemsMapItem : IJsonMessageHandler
    {
        public string[] HandledCommands { get; } = { "addItems" };

        public void Handle(JsonMessage message)
        {
            if (!Player.TryDequeueMapItemRequest(out int mapItemId))
                return;

            JObject items = message.DataObject?["items"] as JObject;
            if (items == null)
                return;

            Dictionary<int, InventoryItem> parsedItems = items.ToObject<Dictionary<int, InventoryItem>>();
            if (parsedItems == null || parsedItems.Count == 0)
                return;

            foreach (InventoryItem item in parsedItems.Values.Where(i => i != null))
            {
                if (string.IsNullOrWhiteSpace(item.Name))
                    item.Name = Player.TempInventory.Items.FirstOrDefault(i => i.Id == item.Id)?.Name ?? "blank";

                Player.RegisterMapItem(mapItemId, item);
            }
        }
    }
}
