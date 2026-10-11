using System.Collections.Generic;
using System.ComponentModel;

namespace Grimoire.Tools.Wiki
{
    public enum WikiEntryType
    {
        Monster,
        Item,
        Quest,
        Shop,
        Location,
        NPC,
        Class,
        Enhancement
    }

    public class WikiLinkItem
    {
        public string Text { get; set; }
        public string Url { get; set; }
        public int Depth { get; set; }
        public List<string> IconUrls { get; set; } = new List<string>();
        public bool HasLink => !string.IsNullOrEmpty(Url);
    }

    public class ScreenSubImage
    {
        public string Label { get; set; }
        public string ImageUrl { get; set; }
    }

    public class ScreenInfo
    {
        public string Label { get; set; }
        public List<string> NoteLines { get; set; } = new List<string>();
        public List<ScreenSubImage> Images { get; set; } = new List<ScreenSubImage>();
    }

    public class SkillField
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public List<string> ListValues { get; set; } = new List<string>();
        public bool IsList => ListValues != null && ListValues.Count > 0;
    }

    public class SkillInfo
    {
        public string Name { get; set; }
        public string ImageUrl { get; set; }
        public List<SkillField> Fields { get; set; } = new List<SkillField>();
    }

    public class SkillRowItem
    {
        public bool IsLink { get; set; }
        public string LinkText { get; set; }
        public SkillInfo Skill { get; set; }
    }

    public class SkillBlock : INotifyPropertyChanged
    {
        public string HeaderTitle { get; set; }
        public List<SkillRowItem> Rows { get; set; } = new List<SkillRowItem>();
        public string PhaseLabel { get; set; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(ChevronPrefix));
            }
        }

        public string ChevronPrefix => IsExpanded ? "−  " : "+  ";

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class PhaseGroup
    {
        public string Label { get; set; }
        public List<SkillBlock> Blocks { get; set; } = new List<SkillBlock>();
        public bool HasLabel => !string.IsNullOrEmpty(Label);
    }

    public class MonsterImageTab
    {
        public string Label { get; set; }
        public string ImageUrl { get; set; }
    }

    public class ShopRow
    {
        public string IconUrl { get; set; }
        public WikiLinkItem NameLink { get; set; } = new WikiLinkItem();
        public List<WikiLinkItem> PriceItems { get; set; } = new List<WikiLinkItem>();
    }

    public class ShopTab
    {
        public string Label { get; set; }
        public List<ShopRow> Rows { get; set; } = new List<ShopRow>();
    }

    public class QuestSubLine
    {
        public string Text { get; set; }
        public string Url { get; set; }
        public bool HasLink => !string.IsNullOrEmpty(Url);
    }

    public class QuestRequiredItem
    {
        public string Text { get; set; }
        public string Url { get; set; }
        public bool HasLink => !string.IsNullOrEmpty(Url);
        public List<QuestSubLine> SubLines { get; set; } = new List<QuestSubLine>();
    }

    public class QuestRewardLine
    {
        public string Text { get; set; }
        public string Url { get; set; }
        public bool HasLink => !string.IsNullOrEmpty(Url);
    }

    public class QuestItemRewardLine
    {
        public string Text { get; set; }
        public string Url { get; set; }
        public bool HasLink => !string.IsNullOrEmpty(Url);
        public List<string> IconUrls { get; set; } = new List<string>();
    }

    public class QuestTabInfo
    {
        public string Label { get; set; }
        public List<string> TopIconUrls { get; set; } = new List<string>();
        public List<string> RequirementLines { get; set; } = new List<string>();
        public string Description { get; set; }
        public string CompletionText { get; set; }
        public List<QuestRequiredItem> ItemsRequired { get; set; } = new List<QuestRequiredItem>();
        public List<QuestRewardLine> RewardLines { get; set; } = new List<QuestRewardLine>();
        public string ItemRewardsLabel { get; set; }
        public List<QuestItemRewardLine> ItemRewards { get; set; } = new List<QuestItemRewardLine>();
        public string ThanksLine { get; set; }
    }

    public class QuestGroup
    {
        public string AnchorName { get; set; }
        public List<QuestSubLine> LocationLines { get; set; } = new List<QuestSubLine>();
        public QuestSubLine BegunFromLink { get; set; }
        public List<string> RequirementLines { get; set; } = new List<string>();
        public List<string> NoteLines { get; set; } = new List<string>();
        public List<QuestTabInfo> Tabs { get; set; } = new List<QuestTabInfo>();
    }

        public class NpcTalkBlock
    {
        public string Title { get; set; }
        public string Text { get; set; }
    }

    public class NpcDialogueEntry
    {
        public string ConditionText { get; set; }
        public string ConditionLinkUrl { get; set; }
        public string Speaker { get; set; }
        public string DialogueText { get; set; }
        public List<NpcTalkBlock> TalkBlocks { get; set; } = new List<NpcTalkBlock>();
        public List<WikiLinkItem> Links { get; set; } = new List<WikiLinkItem>();

        public bool HasCondition => !string.IsNullOrEmpty(ConditionText);
        public bool HasTalkBlocks => TalkBlocks != null && TalkBlocks.Count > 0;
        public bool HasLinks => Links != null && Links.Count > 0;
    }

    public class NpcDialogueTab
    {
        public string Label { get; set; }
        public string IconUrl { get; set; }
        public List<NpcDialogueEntry> Entries { get; set; } = new List<NpcDialogueEntry>();
    }

    public class TextRun
    {
        public string Text { get; set; }
        public string Url { get; set; }
        public bool HasLink => !string.IsNullOrEmpty(Url);
    }

    public class LinkedLine
    {
        public List<TextRun> Runs { get; set; } = new List<TextRun>();
        public int Depth { get; set; }
    }

    public class EnhancementStatRow
    {
        public string Level { get; set; }
        public string STR { get; set; }
        public string INT { get; set; }
        public string DEX { get; set; }
        public string WIS { get; set; }
        public string END { get; set; }
        public string LUK { get; set; }
        public string Cost { get; set; }
    }

    public class EnhancementGroup : INotifyPropertyChanged
    {
        public string HeaderTitle { get; set; }
        public List<string> NoteLines { get; set; } = new List<string>();
        public string TableHeader { get; set; }
        public List<string> SkillDescriptionLines { get; set; } = new List<string>();
        public List<string> ColumnHeaders { get; set; } = new List<string>();
        public List<EnhancementStatRow> Rows { get; set; } = new List<EnhancementStatRow>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(ChevronPrefix));
            }
        }

        public string ChevronPrefix => IsExpanded ? "−  " : "+  ";

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class WikiEntry
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public WikiEntryType Type { get; set; }
        public string Description { get; set; }
        public string ImageUrl { get; set; }
        public List<MonsterImageTab> ItemImageTabs { get; set; } = new List<MonsterImageTab>();
        public string ImageUrlFemale { get; set; }
        public Dictionary<string, string> Stats { get; set; } = new Dictionary<string, string>();
        public List<string> Drops { get; set; } = new List<string>();
        public string Location { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public string WikiUrl { get; set; }

        public List<WikiLinkItem> ShopLocationLinks { get; set; } = new List<WikiLinkItem>();
        public List<ShopTab> ShopTabs { get; set; } = new List<ShopTab>();
        public string CategoryLabel { get; set; }
        public string CategorySlug { get; set; }

        public List<string> EventIconUrls { get; set; } = new List<string>();
        public List<string> TopIconUrls { get; set; } = new List<string>();
        public List<LinkedLine> LocationLines { get; set; } = new List<LinkedLine>();
        public List<LinkedLine> PriceLines { get; set; } = new List<LinkedLine>();
        public List<string> SellbackLines { get; set; } = new List<string>();
        public List<LinkedLine> NotesLines { get; set; } = new List<LinkedLine>();
        public string NotesIconUrl { get; set; }
        public List<string> SpecialEffectsLines { get; set; } = new List<string>();

        public List<WikiLinkItem> MonsterLines { get; set; } = new List<WikiLinkItem>();
        public List<WikiLinkItem> NpcLines { get; set; } = new List<WikiLinkItem>();
        public List<WikiLinkItem> QuestLines { get; set; } = new List<WikiLinkItem>();
        public List<WikiLinkItem> ShopLines { get; set; } = new List<WikiLinkItem>();
        public List<WikiLinkItem> AccessPointLines { get; set; } = new List<WikiLinkItem>();
        public string MapName { get; set; }
        public string RoomLimit { get; set; }
        public List<ScreenInfo> Screens { get; set; } = new List<ScreenInfo>();

        public WikiLinkItem MonsterLocationLink { get; set; }
        public List<string> AttackLines { get; set; } = new List<string>();
        public List<SkillBlock> SkillBlocks { get; set; } = new List<SkillBlock>();
        public List<string> BaseStatsLines { get; set; } = new List<string>();
        public List<string> AbilitiesLines { get; set; } = new List<string>();
        public List<PhaseGroup> PhaseGroups { get; set; } = new List<PhaseGroup>();
        public List<WikiLinkItem> TempItemsDroppedLines { get; set; } = new List<WikiLinkItem>();
        public List<WikiLinkItem> ItemsDroppedLines { get; set; } = new List<WikiLinkItem>();
        public List<MonsterImageTab> MonsterImageTabs { get; set; } = new List<MonsterImageTab>();

        public List<string> QuestJumpLinks { get; set; } = new List<string>();
        public List<QuestGroup> QuestGroups { get; set; } = new List<QuestGroup>();

        public List<NpcDialogueTab> NpcDialogueTabs { get; set; } = new List<NpcDialogueTab>();
        public List<WikiLinkItem> NpcLocationLinks { get; set; } = new List<WikiLinkItem>();
        public string ThanksLine { get; set; }

        public List<WikiLinkItem> EnhancementLocationLinks { get; set; } = new List<WikiLinkItem>();
        public List<EnhancementGroup> EnhancementGroups { get; set; } = new List<EnhancementGroup>();

        public string GetUniqueKey()
        {
            return $"{Type}_{Id}";
        }

        public override string ToString() => Name ?? "(unnamed)";
    }
}
