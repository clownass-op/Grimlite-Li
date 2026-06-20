using System.Threading.Tasks;
using Grimoire.Game;
using Grimoire.UI;
using Grimoire.Game.Data;
namespace Grimoire.Botting.Commands.Misc
{
    public class CmdStop : IBotCommand
    {
        public bool KeepLagkiller { get; set; } = false;
        public Task Execute(IBotEngine instance)
        {
            LogForm.Instance.devDebug("[Stop] Stop Bot command executed");
            if (Configuration.Instance.BankOnStop)
            {
                LogForm.Instance.devDebug("[Stop] BankOnStop is enabled, transferring items...");
                foreach (InventoryItem item in Player.Inventory.Items)
                {
                    if (!item.IsEquipped && item.IsAcItem && item.Category != "Class" && item.Name.ToLower() != "treasure potion" && Configuration.Instance.Items.Contains(item.Name))
                    {
                        Player.Bank.TransferToBank(item.Name);
                        Task.Delay(70);
                        LogForm.Instance.AppendDebug("Transferred to Bank: " + item.Name);
                    }
                }
                LogForm.Instance.AppendDebug("Banked all AC Items in Items list");
            }
            Configuration.Instance.keepLagKiller = KeepLagkiller;
            LogForm.Instance.devDebug($"[Stop] Keep lag killer set to: {KeepLagkiller}");
            Task.Delay(2000);
            LogForm.Instance.devDebug("[Stop] Calling instance.Stop()");
            instance.Stop();
            LogForm.Instance.devDebug("[Stop] Bot stopped");
            return Task.FromResult<object>(null);
        }

        public override string ToString()
        {
            return "Stop bot";
        }
    }
}