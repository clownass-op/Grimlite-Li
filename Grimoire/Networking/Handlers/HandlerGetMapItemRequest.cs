using Grimoire.Game;

namespace Grimoire.Networking.Handlers
{
    public class HandlerGetMapItemRequest : IXtMessageHandler
    {
        public string[] HandledCommands { get; } = { "getMapItem" };

        public void Handle(XtMessage message)
        {
            if (message?.Arguments == null || message.Arguments.Length <= 5)
                return;

            if (int.TryParse(message.Arguments[5], out int mapItemId) && mapItemId > 0)
            {
                int questSlot = 0;
                if (message.Arguments.Length > 4)
                    int.TryParse(message.Arguments[4], out questSlot);

                // If Arguments[4] contains a valid quest ID (>1000), store it immediately
                if (questSlot > 1000)
                    Player.QueueMapItemRequest(mapItemId, questSlot);
                else
                    Player.QueueMapItemRequest(mapItemId);
            }
        }
    }
}
