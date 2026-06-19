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
                Player.QueueMapItemRequest(mapItemId);
        }
    }
}
