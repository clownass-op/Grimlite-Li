using Grimoire.Game;
using Grimoire.Game.Data;
using Grimoire.UI;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Grimoire.Botting.Commands.Quest
{
    public class CmdAcceptQuest : IBotCommand
    {
        public Game.Data.Quest Quest
        {
            get;
            set;
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
        public bool ghostAccept
        {
            get;
            set;
        } = false;

        public async Task Execute(IBotEngine instance)
        {
            BotData.BotState = BotData.State.Quest;
            int id = this.Quest.Id;
            LogForm.Instance.devDebug($"[Quest] Starting Accept Quest command for ID: {id}");

            // Ensure quest is loaded
            if (!Player.Quests.QuestTree.Any(q => q.Id == id))
            {
                LogForm.Instance.devDebug($"[Quest] Quest {id} not in QuestTree, loading...");
                Player.Quests.Load(id);
                LogForm.Instance.devDebug($"[Quest] LoadQuest called for {id}, waiting for load...");
                
                // Wait for quest to load with timeout (faster interval)
                await instance.WaitUntil(() => Player.Quests.QuestTree.Any(q => q.Id == id), timeout: 3, interval: 200);
                
                if (!Player.Quests.QuestTree.Any(q => q.Id == id))
                {
                    LogForm.Instance.devDebug($"[Quest] Timeout: Quest {id} failed to load within 3 seconds");
                    return;
                }
                LogForm.Instance.devDebug($"[Quest] Quest {id} loaded successfully");
            }
            else
            {
                LogForm.Instance.devDebug($"[Quest] Quest {id} already in QuestTree");
            }

            // Get quest reference with null safety
            var Quest = Player.Quests.Quest(id);
            if (Quest == null)
            {
                LogForm.Instance.devDebug($"[Quest] Failed to accept: Quest {id} not found after loading");
                return;
            }
            LogForm.Instance.devDebug($"[Quest] Quest object found for ID: {id}");

            // Skip if quest is already completed (non-repeatable quests only)
            LogForm.Instance.devDebug($"[Quest] Checking if quest is completed...");
            try
            {
                int progress = Player.Quests.progress(Quest.Id);
                LogForm.Instance.devDebug($"[Quest] Quest progress: {progress}, IValue: {Quest.IValue}, ISlot: {Quest.ISlot}, IsNotRepeatable: {Quest.IsNotRepeatable}");
                if (Quest.IValue <= progress && Quest.ISlot != 0 && Quest.IsNotRepeatable)
                {
                    LogForm.Instance.devDebug($"[Quest] Skipping quest {id} - already completed ({Quest.ISlot}): {progress}/{Quest.IValue}");
                    return;
                }
            }
            catch (Exception ex)
            {
                LogForm.Instance.devDebug($"[Quest] Error checking quest progress: {ex.Message}");
            }
            LogForm.Instance.devDebug($"[Quest] Quest {id} not completed, checking progress...");

            // Skip if quest is already in progress
            if (Player.Quests.IsInProgress(Quest.Id))
            {
                LogForm.Instance.devDebug($"[Quest] Quest {id} already in progress");
                return;
            }
            LogForm.Instance.devDebug($"[Quest] Quest {id} not in progress, proceeding to accept");

            // Wait for action to be available
            LogForm.Instance.devDebug($"[Quest] Waiting for AcceptQuest action to be available...");
            await instance.WaitUntil(() => World.IsActionAvailable(LockActions.AcceptQuest), timeout: 5, interval: 200);
            
            if (!World.IsActionAvailable(LockActions.AcceptQuest))
            {
                LogForm.Instance.devDebug($"[Quest] Warning: AcceptQuest action not available after 5 seconds, attempting anyway...");
            }
            else
            {
                LogForm.Instance.devDebug($"[Quest] AcceptQuest action is available");
            }

            // Handle ghost accept
            if (ghostAccept)
            {
                LogForm.Instance.devDebug($"[Quest] Using ghost accept for quest {id}");
                Quest.GhostAccept();
                await Task.Delay(600);
                LogForm.Instance.devDebug($"[Quest] Ghost accepted: {id}");
                return;
            }

            // Try to accept quest with retry logic
            int attempts = 0;
            int maxAttempts = 3;
            LogForm.Instance.devDebug($"[Quest] Starting normal accept with {maxAttempts} max attempts");

            while (!Player.Quests.IsInProgress(Quest.Id) && Player.IsLoggedIn && instance.IsRunning && attempts < maxAttempts)
            {
                attempts++;
                LogForm.Instance.devDebug($"[Quest] Accept attempt {attempts}/{maxAttempts} for quest {id}");
                Quest.Accept();
                await Task.Delay(600);
                LogForm.Instance.devDebug($"[Quest] Accept called, waiting 600ms, checking if in progress...");
                
                if (Player.Quests.IsInProgress(Quest.Id))
                {
                    LogForm.Instance.devDebug($"[Quest] Quest {id} is now in progress after attempt {attempts}");
                    break;
                }

                if (attempts == maxAttempts && !Player.Quests.IsInProgress(Quest.Id))
                {
                    LogForm.Instance.devDebug($"[Quest] Failed to accept quest {id} after {maxAttempts} attempts");
                }
            }

            if (Player.Quests.IsInProgress(Quest.Id))
            {
                LogForm.Instance.devDebug($"[Quest] Successfully accepted: {id}");
            }
            else
            {
                LogForm.Instance.devDebug($"[Quest] Quest {id} is NOT in progress after all attempts");
            }
            LogForm.Instance.devDebug($"[Quest] Accept Quest command completed for ID: {id}");
        }

        public override string ToString()
        {
            return (ghostAccept ? "Ghost Accept: " : "Accept Quest: ") + Quest.Id;
        }
    }
}