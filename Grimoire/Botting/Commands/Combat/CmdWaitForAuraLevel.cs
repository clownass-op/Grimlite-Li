using System;
using System.Threading.Tasks;
using Grimoire.Game;
using Grimoire.Game.Data;
using Grimoire.UI;

namespace Grimoire.Botting.Commands.Combat
{
    /// <summary>
    /// Keeps pressing a skill until a specific aura level is reached.
    /// Repeats this cycle a configurable number of times.
    /// Perfect for buffing sequences where you need to reach a specific aura level multiple times.
    /// </summary>
    public class CmdWaitForAuraLevel : IBotCommand
    {
        /// <summary>Skill index to press repeatedly</summary>
        public string SkillIndex { get; set; }

        /// <summary>Name of the aura to monitor (e.g., "Temporal Rift")</summary>
        public string AuraName { get; set; }

        /// <summary>Target aura level to reach before stopping</summary>
        public int TargetAuraLevel { get; set; }

        /// <summary>Number of times to repeat the cycle (reach target aura level)</summary>
        public int RepeatCount { get; set; } = 1;

        /// <summary>Maximum time to wait for aura to reach target level (milliseconds)</summary>
        public int TimeoutMs { get; set; } = 60000; // 60 seconds default

        /// <summary>Delay between skill presses (milliseconds)</summary>
        public int SkillDelayMs { get; set; } = 500; // 500ms between presses

        /// <summary>Continue even if timeout is reached</summary>
        public bool ContinueOnTimeout { get; set; } = false;

        public async Task Execute(IBotEngine instance)
        {
            if (string.IsNullOrEmpty(SkillIndex) || string.IsNullOrEmpty(AuraName))
            {
                LogForm.Instance.AppendDebug($"[WaitForAuraLevel] ERROR: SkillIndex or AuraName not set");
                return;
            }

            LogForm.Instance.AppendDebug($"[WaitForAuraLevel] Starting - Skill: {SkillIndex}, Aura: {AuraName}, Target: {TargetAuraLevel}, Repeats: {RepeatCount}");

            for (int cycle = 1; cycle <= RepeatCount; cycle++)
            {
                LogForm.Instance.AppendDebug($"[WaitForAuraLevel] === Cycle {cycle}/{RepeatCount} ===");
                
                // Wait for aura to reach target level
                bool success = await WaitForAuraToReachLevel(SkillIndex, AuraName, TargetAuraLevel);

                if (!success && !ContinueOnTimeout)
                {
                    LogForm.Instance.AppendDebug($"[WaitForAuraLevel] ⏱ Timeout on cycle {cycle}. Stopping bot...");
                    instance.Stop();
                    return;
                }

                if (cycle < RepeatCount)
                {
                    // Add delay between cycles
                    LogForm.Instance.AppendDebug($"[WaitForAuraLevel] Preparing for next cycle...");
                    await Task.Delay(500); // 500ms between cycles
                }
            }

            LogForm.Instance.AppendDebug($"[WaitForAuraLevel] ✓ Completed all {RepeatCount} cycles!");
        }

        private async Task<bool> WaitForAuraToReachLevel(string skillIndex, string auraName, int targetLevel)
        {
            DateTime startTime = DateTime.Now;
            DateTime timeoutTime = startTime.AddMilliseconds(TimeoutMs);
            int pressCount = 0;

            LogForm.Instance.AppendDebug($"[WaitForAuraLevel] Waiting for '{auraName}' to reach level {targetLevel}...");

            while (DateTime.Now < timeoutTime)
            {
                int currentAuraLevel = Player.GetAuras(true, auraName);
                
                if (currentAuraLevel >= targetLevel)
                {
                    LogForm.Instance.AppendDebug($"[WaitForAuraLevel] ✓ Aura '{auraName}' reached level {currentAuraLevel} (target: {targetLevel}) after {pressCount} skill presses");
                    return true;
                }

                // Press the skill
                LogForm.Instance.AppendDebug($"[WaitForAuraLevel] Pressing skill {skillIndex} - Current aura level: {currentAuraLevel}/{targetLevel}");
                
                try
                {
                    await PressSkill(skillIndex);
                    pressCount++;
                }
                catch (Exception ex)
                {
                    LogForm.Instance.AppendDebug($"[WaitForAuraLevel] ERROR pressing skill: {ex.Message}");
                }

                // Wait before pressing again
                await Task.Delay(SkillDelayMs);
            }

            // Timeout reached
            int finalAuraLevel = Player.GetAuras(true, auraName);
            LogForm.Instance.AppendDebug($"[WaitForAuraLevel] ⏱ Timeout reached! Final aura level: {finalAuraLevel}/{targetLevel} after {pressCount} skill presses");
            
            return false;
        }

        private async Task PressSkill(string skillIndex)
        {
            // Flash call to activate skill
            try
            {
                Flash.Call<object>("SwitchSkill", new string[] { skillIndex, "0" });
                await Task.Delay(100); // Small delay to ensure skill is registered
            }
            catch (Exception ex)
            {
                LogForm.Instance.AppendDebug($"[WaitForAuraLevel] ERROR: Could not activate skill {skillIndex}: {ex.Message}");
            }
        }

        public override string ToString()
        {
            return $"Wait for Aura Level - Skill: {SkillIndex}, Aura: {AuraName}, Target: {TargetAuraLevel}, Repeats: {RepeatCount}, Timeout: {TimeoutMs}ms";
        }
    }
}
