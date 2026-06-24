using Grimoire.Botting.Commands.Map;
using Grimoire.Game;
using Grimoire.Tools;
using Grimoire.UI;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Grimoire.Botting.Commands.Combat
{
    class CmdShortHunt : IBotCommand
    {
        public string Map { get; set; }
        public string Cell { get; set; }
        public string Pad { get; set; }
        public string Monster { get; set; }
        public string SkillSet { get; set; } = "";
        public string ItemName { get; set; }
        public ItemType ItemType { get; set; }
        public string Quantity { get; set; }
        public string MapItemId { get; set; } = "";
        public string MapItemQuantity { get; set; } = "1";
        public string KillPriority { get; set; } = "";
        public bool AntiCounter { get; set; } = false;
        public string QuestId { get; set; }
        public int DelayAfterKill { get; set; } = 50;
        public bool BlankFirst { get; set; }

        public async Task Execute(IBotEngine instance)
        {
            // Always enable cutscene skipping for short hunt
            OptionsManager.SetSkipCutscenes();

            string _Items = instance.ResolveVars(ItemName);
            string _Qty = instance.ResolveVars(Quantity);
            string _Map = instance.ResolveVars(Map.ToLower());
            string[] _Cells = instance.ResolveVars(Cell).Split(',');
            string[] _pad = instance.ResolveVars(Pad).Split(',');
            string _MapItemId = instance.ResolveVars(MapItemId);
            string _MapItemQty = instance.ResolveVars(MapItemQuantity);

            // Parse comma-separated items and quantities
            string[] itemNames = _Items.Split(',');
            string[] quantities = _Qty.Split(',');
            
            // Trim whitespace
            for (int i = 0; i < itemNames.Length; i++)
                itemNames[i] = itemNames[i].Trim();
            for (int i = 0; i < quantities.Length; i++)
                quantities[i] = quantities[i].Trim();
            
            // Ensure quantities array matches items array length
            if (quantities.Length < itemNames.Length)
            {
                Array.Resize(ref quantities, itemNames.Length);
                for (int i = quantities.Length - 1; i < itemNames.Length; i++)
                    quantities[i] = "1";
            }

            // Associate each item with its corresponding cell
            // If cells array is shorter than items, use the last cell for remaining items
            string[] itemCells = new string[itemNames.Length];
            for (int i = 0; i < itemNames.Length; i++)
            {
                if (i < _Cells.Length)
                    itemCells[i] = _Cells[i];
                else
                    itemCells[i] = _Cells[_Cells.Length - 1];
            }

            LogForm.Instance.AppendDebug($"[CmdShortHunt] Starting hunt for {itemNames.Length} item(s) on map {_Map}");

            // Handle quest if QuestId is provided
            int qid = 0;
            bool doQuest = !string.IsNullOrEmpty(QuestId) && int.TryParse(QuestId, out qid) && qid != 0;
            if (doQuest)
            {
                await instance.WaitUntil(() => Player.Quests != null, timeout: 10);

                // Check if quest can already be completed
                if (Player.Quests.CanComplete(qid))
                {
                    LogForm.Instance.AppendDebug($"[CmdShortHunt] Quest {qid} is ready to complete, completing now...");
                    Player.Quests.Complete(qid);
                    await Task.Delay(1000);
                    return;
                }

                // Load the quest
                if (!Player.Quests.QuestTree.Exists(q => q.Id == qid))
                {
                    Player.Quests.Load(qid);
                    await instance.WaitUntil(() => Player.Quests.QuestTree.Any(q => q.Id == qid), timeout: 3);
                }

                var quest = Player.Quests.Quest(qid);

                // Check if quest has been completed
                if (quest != null && quest.HasBeenCompleted())
                {
                    LogForm.Instance.AppendDebug($"[CmdShortHunt] Quest {qid} has already been completed, skipping...");
                    return;
                }

                // Accept the quest if not in progress
                if (!Player.Quests.IsInProgress(qid))
                {
                    LogForm.Instance.AppendDebug($"[CmdShortHunt] Accepting quest {qid}...");
                    int retries = 0;
                    while (!Player.Quests.IsInProgress(qid) && retries < 5 && instance.IsRunning)
                    {
                        Player.Quests.Accept(qid);
                        await Task.Delay(800);
                        retries++;
                    }
                    if (Player.Quests.IsInProgress(qid))
                    {
                        LogForm.Instance.AppendDebug($"[CmdShortHunt] Quest {qid} accepted successfully!");
                    }
                }
            }

            // If MapItemId is provided, get map item after hunting
            bool doMapItem = !string.IsNullOrEmpty(_MapItemId);

            // Check if all items are already obtained
            bool allObtained = true;
            for (int i = 0; i < itemNames.Length; i++)
            {
                if (ItemType == ItemType.Items)
                {
                    if (!Player.Inventory.ContainsItem(itemNames[i], quantities[i]))
                    {
                        allObtained = false;
                        break;
                    }
                }
                else // ItemType.Temps
                {
                    if (!Player.TempInventory.ContainsItem(itemNames[i], quantities[i]))
                    {
                        allObtained = false;
                        break;
                    }
                }
            }
            if (allObtained)
            {
                LogForm.Instance.AppendDebug($"[CmdShortHunt] Already have all items");
                return;
            }

            CmdJoin join = new CmdJoin
            {
                Map = _Map,
                Cell = _Cells[0],
                Pad = _pad[0]
            };
            while (!Player.Map.Equals(_Map.Split('-')[0]) && instance.IsRunning)
            {
                if (BlankFirst)
                {
                    string[] safeCell = ClientConfig.GetValue(ClientConfig.C_SAFE_CELL).Split(',');
                    Player.MoveToCell(safeCell[0], safeCell[1]);
                    await instance.WaitUntil(() => Player.CurrentState != Player.State.InCombat, timeout: 3);
                    await Task.Delay(1000);
                }
                await join.Execute(instance);
            }

            // Hunt for each item at its designated cell
            for (int itemIdx = 0; itemIdx < itemNames.Length && instance.IsRunning; itemIdx++)
            {
                string currentItem = itemNames[itemIdx];
                string currentQty = quantities[itemIdx];
                string currentCell = itemCells[itemIdx];
                string currentPad = (itemIdx < _pad.Length) ? _pad[itemIdx] : "Left";

                LogForm.Instance.AppendDebug($"[CmdShortHunt] Hunting for {currentItem}x{currentQty} at cell {currentCell}");

                // Check if item is already obtained
                bool itemObtained = false;
                if (ItemType == ItemType.Items)
                {
                    if (Player.Inventory.ContainsItem(currentItem, currentQty))
                    {
                        LogForm.Instance.AppendDebug($"[CmdShortHunt] Already have {currentItem}x{currentQty}");
                        itemObtained = true;
                    }
                }
                else
                {
                    if (Player.TempInventory.ContainsItem(currentItem, currentQty))
                    {
                        LogForm.Instance.AppendDebug($"[CmdShortHunt] Already have {currentItem}x{currentQty} in temp");
                        itemObtained = true;
                    }
                }

                if (itemObtained)
                    continue;

                // Move to the designated cell for this item
                if (Player.Cell != currentCell)
                {
                    LogForm.Instance.AppendDebug($"[CmdShortHunt] Moving to cell {currentCell} pad {currentPad}");
                    Player.MoveToCell(currentCell, currentPad);
                    // Wait until player is actually in the correct cell
                    await instance.WaitUntil(() => Player.Cell == currentCell, timeout: 5);
                    LogForm.Instance.AppendDebug($"[CmdShortHunt] Successfully moved to cell {Player.Cell}");
                }

                // Hunt for this specific item
                CmdKillFor killFor = new CmdKillFor
                {
                    Monster = Monster,
                    ItemName = currentItem,
                    ItemType = ItemType,
                    Quantity = currentQty,
                    QuestId = QuestId,
                    DelayAfterKill = DelayAfterKill,
                    KillPriority = KillPriority,
                    AntiCounter = AntiCounter,
                    SkillSet = SkillSet,
                    TargetCell = currentCell,
                    TargetPad = currentPad
                };

                LogForm.Instance.AppendDebug($"[CmdShortHunt] Starting kill loop for {currentItem}");
                await killFor.Execute(instance);
                LogForm.Instance.AppendDebug($"[CmdShortHunt] Hunt complete for {currentItem}");
                
                // Verify and reposition to correct cell after cutscene
                if (Player.Cell != currentCell)
                {
                    LogForm.Instance.AppendDebug($"[CmdShortHunt] Current cell: {Player.Cell}, Target cell: {currentCell}. Repositioning...");
                    Player.MoveToCell(currentCell, currentPad);
                    // Wait until player is actually in the correct cell
                    await instance.WaitUntil(() => Player.Cell == currentCell, timeout: 5);
                    LogForm.Instance.AppendDebug($"[CmdShortHunt] Successfully repositioned to cell {Player.Cell}");
                }
                
                // Complete quest if it can be completed after this hunt
                if (doQuest && Player.Quests.CanComplete(qid))
                {
                    LogForm.Instance.AppendDebug($"[CmdShortHunt] Quest {qid} can be completed, completing now...");
                    Player.Quests.Complete(qid);
                    await Task.Delay(1000);
                }
            }

            LogForm.Instance.AppendDebug($"[CmdShortHunt] All hunts complete");

            // If MapItemId is provided, get map item after hunting
            if (doMapItem)
            {
                // Parse comma-separated map item IDs and quantities
                string[] mapItemIds = _MapItemId.Split(',');
                string[] mapItemQtys = string.IsNullOrEmpty(_MapItemQty) ? new string[] { "1" } : _MapItemQty.Split(',');
                
                // Ensure quantities array matches IDs array length
                if (mapItemQtys.Length < mapItemIds.Length)
                {
                    Array.Resize(ref mapItemQtys, mapItemIds.Length);
                    for (int i = mapItemQtys.Length - 1; i < mapItemIds.Length; i++)
                        mapItemQtys[i] = "1";
                }
                
                LogForm.Instance.AppendDebug($"[CmdShortHunt] Getting {mapItemIds.Length} map item(s) after hunt");
                
                // Exit combat and stop attacking before picking up map items
                if (Player.CurrentState == Player.State.InCombat)
                {
                    LogForm.Instance.AppendDebug($"[CmdShortHunt] Exiting combat before picking up map items");
                    Player.CancelAutoAttack();
                    await instance.WaitUntil(() => Player.CurrentState != Player.State.InCombat, timeout: 5);
                    await Task.Delay(500);
                }
                
                // Get each map item
                for (int idx = 0; idx < mapItemIds.Length && instance.IsRunning; idx++)
                {
                    if (int.TryParse(mapItemIds[idx].Trim(), out int mapItemId) && int.TryParse(mapItemQtys[idx].Trim(), out int qty))
                    {
                        LogForm.Instance.AppendDebug($"[CmdShortHunt] Getting map item ID: {mapItemId} x{qty}");
                        
                        for (int i = 0; i < qty && instance.IsRunning; i++)
                        {
                            // Ensure not in combat before picking up map item
                            if (Player.CurrentState == Player.State.InCombat)
                            {
                                LogForm.Instance.AppendDebug($"[CmdShortHunt] Exiting combat before map item pickup");
                                Player.CancelAutoAttack();
                                await instance.WaitUntil(() => Player.CurrentState != Player.State.InCombat, timeout: 5);
                                await Task.Delay(500);
                            }
                            
                            Player.GetMapItem(mapItemId);
                            await Task.Delay(1500);
                        }
                    }
                }

                // Complete quest if it can be completed
                if (doQuest && Player.Quests.CanComplete(qid))
                {
                    LogForm.Instance.AppendDebug($"[CmdShortHunt] Quest {qid} can be completed, completing now...");
                    Player.Quests.Complete(qid);
                    await Task.Delay(1000);
                }
            }
        }

        public override string ToString()
        {
            string itemType = ItemType == ItemType.Items ? "Items" : "Temps";
            
            // If QuestId is provided, show Quest ID - Hunt itemnames
            if (!string.IsNullOrEmpty(QuestId) && int.TryParse(QuestId, out int qid) && qid != 0)
            {
                return $"{qid}:Hunt {ItemName}";
            }
            
            return $"Hunt {itemType} {Quantity}x {ItemName}";
        }

    }
}
