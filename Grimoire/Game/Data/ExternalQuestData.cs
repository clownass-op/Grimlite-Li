using Newtonsoft.Json;
using System.Collections.Generic;

namespace Grimoire.Game.Data
{
    /// <summary>
    /// DTO that mirrors external quest data JSON shapes (PascalCase fields
    /// like <c>ID</c>, <c>Slot</c>, <c>Value</c>, <c>Once</c>, <c>XP</c>,
    /// <c>RequiredClassID</c>, <c>RequiredFactionId</c>, <c>RequiredFactionRep</c>).
    /// Used to import external quest data into Grimlite's <see cref="Quest"/> model.
    /// </summary>
    public class ExternalQuestData
    {
        [JsonProperty("ID")] public int ID { get; set; }
        [JsonProperty("Slot")] public int? Slot { get; set; }
        [JsonProperty("Value")] public int Value { get; set; }
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("Once")] public bool Once { get; set; }
        [JsonProperty("Field")] public string Field { get; set; }
        [JsonProperty("Index")] public int Index { get; set; }
        [JsonProperty("Upgrade")] public bool Upgrade { get; set; }
        [JsonProperty("Level")] public int Level { get; set; }
        [JsonProperty("RequiredClassID")] public int RequiredClassID { get; set; }
        [JsonProperty("RequiredClassPoints")] public int RequiredClassPoints { get; set; }
        [JsonProperty("RequiredFactionId")] public int RequiredFactionId { get; set; }
        [JsonProperty("RequiredFactionRep")] public int RequiredFactionRep { get; set; }
        [JsonProperty("Gold")] public int Gold { get; set; }
        [JsonProperty("XP")] public int XP { get; set; }
        [JsonProperty("AcceptRequirements")] public List<object> AcceptRequirements { get; set; }
        [JsonProperty("Requirements")] public List<object> Requirements { get; set; }
        [JsonProperty("Rewards")] public List<object> Rewards { get; set; }
        [JsonProperty("SimpleRewards")] public List<object> SimpleRewards { get; set; }

        /// <summary>
        /// Convert this Skua-shaped record into a Grimlite <see cref="Quest"/>.
        /// </summary>
        public Quest ToQuest()
        {
            return new Quest
            {
                Id = ID,
                ISlot = Slot,
                IValue = Value,
                Name = Name,
                IsNotRepeatable = Once,
                IsMemberOnly = Upgrade,
                Level = Level,
                RequiredClassPoints = RequiredClassPoints,
                FactionId = RequiredFactionId,
                RequiredReputation = RequiredFactionRep,
                GoldReward = Gold,
                ExperienceReward = XP
            };
        }
    }
}
