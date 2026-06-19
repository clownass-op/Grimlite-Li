namespace Grimoire.Game.Data
{
    public class MapItem
    {
        public int Id { get; set; }
        public int QuestId { get; set; }
        public string MapFilePath { get; set; }
        public string MapName { get; set; }

        public override string ToString()
        {
            return $"ID [{Id}], Quest [{QuestId}]";
        }
    }
}
