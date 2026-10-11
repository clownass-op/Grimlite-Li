using Grimoire.Game;
using Grimoire.Game.Data;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Grimoire.Botting.Commands.Misc.Statements
{
    public class CmdNotEquipped : StatementCommand, IBotCommand
    {
        public CmdNotEquipped()
        {
            Tag = "Item";
            Text = "Is not equipped";
        }

        public Task Execute(IBotEngine instance)
        {
            try
            {
                if (Player.Inventory?.Items == null)
                    return Task.FromResult<object>(null);
            string raw = instance.ResolveVars(Value1 ?? "");
            if (string.IsNullOrEmpty(raw))
                return Task.FromResult<object>(null);

            InventoryItem item = null;

            // 1) Forge enhancement by enum name (Valiance, Dauntless, Praxis, Lament, etc.)
            if (Enum.TryParse<InventoryItem.forgeID>(raw, ignoreCase: true, out var forge))
            {
                item = Player.Inventory.Items.FirstOrDefault(i => i.ForgeEnhancement == forge && i.IsEquippable);
            }

            // 2) Enhancement display name (Luck Awe Blast, etc.)
            if (item == null)
            {
                var kvp = InventoryItem.EnhancementNames
                    .FirstOrDefault(p => p.Value.Equals(raw, StringComparison.OrdinalIgnoreCase));
                if (!kvp.Equals(default(System.Collections.Generic.KeyValuePair<int, string>)))
                {
                    int enhIdByName = kvp.Key;
                    item = Player.Inventory.Items
                        .FirstOrDefault(i => i.Enhancement == enhIdByName && i.IsEquippable);
                }
            }

            // 3) Raw enhancement ID
            if (item == null && int.TryParse(raw, out int enhId))
            {
                item = Player.Inventory.Items.FirstOrDefault(i => i.Enhancement == enhId && i.IsEquippable);
            }

            // 4) Fallback: normal item name match (also resolves [Vars] via ResolveVars above)
            if (item == null)
            {
                item = Player.Inventory.Items.FirstOrDefault(i =>
                    i.IsEquippable && i.Name.Equals(raw, StringComparison.OrdinalIgnoreCase));
            }

            // Not in inventory -> treat as not equipped (do NOT skip, go to Equip which will no-op)
            if (item == null)
                return Task.FromResult<object>(null);

            bool isEquipped = false;

            // For classes, check against EquippedClass
            if (item.Category == "Class")
            {
                isEquipped = Player.EquippedClass.Equals(item.Name, StringComparison.OrdinalIgnoreCase);
            }
            // For weapons, try Flash property first, fallback to inventory
            else if (InventoryItem.Weapons.Contains(item.Category))
            {
                string equipped = Player.EquippedWeapon ?? "";
                isEquipped = !string.IsNullOrEmpty(equipped) && equipped.Equals(item.Name, StringComparison.OrdinalIgnoreCase);
                
                // Fallback to inventory check if Flash property is empty
                if (!isEquipped && item.Id > 0)
                {
                    var invItem = Player.Inventory.Items.FirstOrDefault(it => it.Id == item.Id && it.IsEquipped);
                    isEquipped = invItem != null;
                }
            }
            // For armor, try Flash property first, fallback to inventory
            else if (item.Category == "Armor")
            {
                string equipped = Player.EquippedArmor ?? "";
                isEquipped = !string.IsNullOrEmpty(equipped) && equipped.Equals(item.Name, StringComparison.OrdinalIgnoreCase);
                
                if (!isEquipped && item.Id > 0)
                {
                    var invItem = Player.Inventory.Items.FirstOrDefault(it => it.Id == item.Id && it.IsEquipped);
                    isEquipped = invItem != null;
                }
            }
            // For helm, try Flash property first, fallback to inventory
            else if (item.Category == "Helm")
            {
                string equipped = Player.EquippedHelm ?? "";
                isEquipped = !string.IsNullOrEmpty(equipped) && equipped.Equals(item.Name, StringComparison.OrdinalIgnoreCase);
                
                if (!isEquipped && item.Id > 0)
                {
                    var invItem = Player.Inventory.Items.FirstOrDefault(it => it.Id == item.Id && it.IsEquipped);
                    isEquipped = invItem != null;
                }
            }
            // For cape, try Flash property first, fallback to inventory
            else if (item.Category == "Cape")
            {
                string equipped = Player.EquippedCape ?? "";
                isEquipped = !string.IsNullOrEmpty(equipped) && equipped.Equals(item.Name, StringComparison.OrdinalIgnoreCase);
                
                if (!isEquipped && item.Id > 0)
                {
                    var invItem = Player.Inventory.Items.FirstOrDefault(it => it.Id == item.Id && it.IsEquipped);
                    isEquipped = invItem != null;
                }
            }
            else
            {
                // For other items (potions, etc.), check IsEquipped flag
                isEquipped = item.IsEquipped;
            }

            if (isEquipped)
            {
                instance.Index++;
            }
            return Task.FromResult<object>(null);
            }
            catch
            {
                return Task.FromResult<object>(null);
            }
        }

        public override string ToString()
        {
            return "Item is not equipped: " + Value1;
        }
    }
}