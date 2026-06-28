using Newtonsoft.Json;
using System.Collections.Generic;

namespace Grimoire.Game.Data
{
    public class QuestSaveData
    {
        [JsonProperty("QuestID")]
        public int Id { get; set; }

        [JsonProperty("sName")]
        public string Name { get; set; }

        [JsonProperty("sDesc")]
        public string Description { get; set; }

        [JsonProperty("iSlot")]
        public int? ISlot { get; set; }

        [JsonProperty("iValue")]
        public int IValue { get; set; }

        [JsonProperty("bOnce")]
        public bool IsNotRepeatable { get; set; }

        [JsonProperty("bUpg")]
        public bool IsMemberOnly { get; set; }

        [JsonProperty("iLvl")]
        public int Level { get; set; }

        [JsonProperty("iGold")]
        public int GoldReward { get; set; }

        [JsonProperty("iExp")]
        public int ExperienceReward { get; set; }

        [JsonProperty("iRep")]
        public int ReputationReward { get; set; }

        [JsonProperty("iReqRep")]
        public int RequiredReputation { get; set; }

        [JsonProperty("iReqCP")]
        public int RequiredClassPoints { get; set; }

        [JsonProperty("iClass")]
        public int ClassPointsReward { get; set; }

        [JsonProperty("FactionID")]
        public int FactionId { get; set; }

        [JsonProperty("sFaction")]
        public string Faction { get; set; }

        [JsonProperty("RequiredItems")]
        public List<InventoryItemSaveData> RequiredItems { get; set; }

        [JsonProperty("reward")]
        public List<InventoryItemSaveData> Rewards { get; set; }
    }

    public class InventoryItemSaveData
    {
        [JsonProperty("ItemID")]
        public int Id { get; set; }

        [JsonProperty("sName")]
        public string Name { get; set; }

        [JsonProperty("iQty")]
        public int Quantity { get; set; }

        [JsonProperty("iStk")]
        public int MaxStack { get; set; }

        [JsonProperty("iLvl")]
        public int Level { get; set; }

        [JsonProperty("iCost")]
        public int Cost { get; set; }

        [JsonProperty("sDesc")]
        public string Description { get; set; }

        [JsonProperty("sType")]
        public string Category { get; set; }

        [JsonProperty("sFile")]
        public string File { get; set; }

        [JsonProperty("sLink")]
        public string Link { get; set; }

        [JsonProperty("bCoins")]
        public bool IsAcItem { get; set; }

        [JsonProperty("bUpg")]
        public bool IsMemberOnly { get; set; }

        [JsonProperty("bTemp")]
        public bool IsTemporary { get; set; }

        [JsonProperty("iEnh")]
        public int Enhancement { get; set; }

        [JsonProperty("ShopItemID")]
        public int ShopItemId { get; set; }

        [JsonProperty("iRate")]
        public string DropChance { get; set; }
    }
}
