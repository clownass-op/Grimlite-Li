using Grimoire.Game;
using Grimoire.Game.Data;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Grimoire.Botting.Commands.Item
{
	public class CmdEquip : IBotCommand
	{
		public string ItemName
		{
			get;
			set;
		}

		public bool Safe
		{
			get;
			set;
		}

		public async Task Execute(IBotEngine instance)
		{
			// Resolve variables first (if ItemName is a variable key).
			var raw = instance.ResolveVars(ItemName);
            if (string.IsNullOrEmpty(raw))
                return;

            // Only forge/enhancement-ID lookups need the full list (slow, often null
            // on big inventories). Plain names use instant single lookup — no wait,
            // so stopping the bot can never be what "unblocks" the send.
            bool needsList = Enum.TryParse<InventoryItem.forgeID>(raw, ignoreCase: true, out var _f)
                || InventoryItem.EnhancementNames.Any(p => p.Value.Equals(raw, StringComparison.OrdinalIgnoreCase))
                || int.TryParse(raw, out var _n);
            if (needsList)
            {
                try
                {
                    if (Player.Inventory?.Items == null)
                        await instance.WaitUntil(() =>
                        {
                            try { return Player.Inventory != null && Player.Inventory.Items != null; }
                            catch { return false; }
                        }, () => instance.IsRunning && Player.IsLoggedIn, timeout: 15, interval: 1000);
                }
                catch { }
            }

            System.Collections.Generic.List<InventoryItem> items = null;
            try { items = Player.Inventory?.Items; } catch { items = null; }
            if (items == null)
            {
                // Fallback: single-item Flash lookup (small response, works when full list fails).
                try
                {
                    var single = Player.Inventory?.GetItemByName(raw);
                    if (single != null && single.IsEquippable)
                    {
                        items = new System.Collections.Generic.List<InventoryItem> { single };
                        try { Grimoire.UI.LogForm.Instance.AppendDebug($"[Equip] List null, single-lookup OK: {ItemName}->{raw} (id:{single.Id})"); } catch { }
                    }
                    else
                    {
                        try { Grimoire.UI.LogForm.Instance.AppendDebug($"[Equip] Inventory null, skip {ItemName}->{raw}"); } catch { }
                        return;
                    }
                }
                catch { return; }
            }

            InventoryItem item = null;

            // 1) Forge enhancement by enum name (Valiance, Dauntless, Praxis, etc.)
            if (Enum.TryParse<InventoryItem.forgeID>(raw, ignoreCase: true, out var forge))
            {
                item = items.FirstOrDefault(i => i.ForgeEnhancement == forge && i.IsEquippable);
            }

            // 2) Enhancement display name (Luck Awe Blast, etc.)
            if (item == null)
            {
                var kvp = InventoryItem.EnhancementNames
                    .FirstOrDefault(p => p.Value.Equals(raw, StringComparison.OrdinalIgnoreCase));
                if (!kvp.Equals(default(System.Collections.Generic.KeyValuePair<int, string>)))
                {
                    int enhIdByName = kvp.Key;
                    item = items.FirstOrDefault(i => i.Enhancement == enhIdByName && i.IsEquippable);
                }
            }

            // 3) Raw enhancement ID
            if (item == null && int.TryParse(raw, out int enhId))
            {
                item = items.FirstOrDefault(i => i.Enhancement == enhId && i.IsEquippable);
            }

            // 4) Fallback: normal item name match.
            if (item == null)
            {
                item = items.FirstOrDefault(i =>
                    i.IsEquippable && i.Name.Equals(raw, StringComparison.OrdinalIgnoreCase));
            }

            // Last resort: single-item lookup when list is a 1-item fallback or list missed it.
            if (item == null)
            {
                try
                {
                    var single = Player.Inventory?.GetItemByName(raw);
                    if (single != null && single.IsEquippable)
                        item = single;
                }
                catch { }
            }

			if (item == null)
            {
                try { Grimoire.UI.LogForm.Instance.AppendDebug($"[Equip] Not found: {raw} (inv:{items.Count})"); } catch { }
                return;
            }

			// Fire-and-forget like the SWF/Aethra path: resolve, send once, move on.
			// No verify loop — verification is what hangs when the list bridge is null.
			using (new pauseProvoke(instance.Configuration))
			{
				// Potions/scrolls are used IN combat — don't combat-wait for them.
				if (Safe && item.Category != "Item")
				{
					BotData.BotState = BotData.State.Transaction;
					try { Grimoire.UI.LogForm.Instance.AppendDebug($"[Equip] Safe check: combat={Player.CurrentState} lock={World.IsActionAvailable(LockActions.EquipItem)}"); } catch { }
					int combatWait = 0;
					while (instance.IsRunning && Player.CurrentState == Player.State.InCombat && combatWait < 15)
					{
						Player.MoveToCell(Player.Cell, Player.Pad);
						await Task.Delay(1000);
						combatWait++;
					}
					await instance.WaitUntil(() => World.IsActionAvailable(LockActions.EquipItem));
				}

				if (item.Category == "Item")
					Player.EquipPotion(item.Id, item.Description, item.File, item.Name);
				else
					Player.Equip(item.Id);

				try { Grimoire.UI.LogForm.Instance.AppendDebug($"[Equip] Sent: {ItemName}->{raw} (id:{item.Id} cat:{item.Category})"); } catch { }
				await Task.Delay(500);
			}
		}

		private bool IsEquippedNow(InventoryItem item)
		{
			try
			{
				var it = Player.Inventory?.Items?.FirstOrDefault(i => i.IsEquipped && i.Id == item.Id);
				if (it != null)
					return true;
			}
			catch { }
			// Fallback when full list is null: single-item lookup works.
			try
			{
				var s = Player.Inventory?.GetItemByName(item.Name);
				if (s != null && s.IsEquipped && s.Id == item.Id)
					return true;
			}
			catch { }
			return false;
		}

		public bool IsEquipped(int ItemID)
		{
			try
			{
				return Player.Inventory?.Items?.FirstOrDefault((InventoryItem it) => it.IsEquipped && it.Id == ItemID) != null;
			}
			catch { return false; }
		}

		public override string ToString()
		{
			return (Safe ? "Safe" : "Unsafe") + " Equip: " + ItemName;
		}
	}
}