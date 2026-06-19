using Grimoire.Game;
using Grimoire.Tools;

namespace Grimoire.Networking.Handlers
{
    public class HandlerAccountPresenceMoveToArea : IJsonMessageHandler
    {
        public string[] HandledCommands { get; } = { "moveToArea" };

        public void Handle(JsonMessage message)
        {
            try
            {
                string areaName = message.DataObject?["areaName"]?.ToString();
                string cell = message.DataObject?["uoBranch"]?[0]?["strFrame"]?.ToString();
                string pad = message.DataObject?["uoBranch"]?[0]?["strPad"]?.ToString();

                if (!string.IsNullOrWhiteSpace(areaName))
                    AccountPresenceTracker.Instance.UpdateJoinedMap(areaName, cell, pad);
            }
            catch
            {
            }
        }
    }

    public class HandlerAccountPresenceMoveToCell : IXtMessageHandler
    {
        public string[] HandledCommands { get; } = { "moveToCell" };

        public void Handle(XtMessage message)
        {
            try
            {
                if (message.Arguments == null || message.Arguments.Length < 7)
                    return;

                string cell = message.Arguments[5];
                string pad = message.Arguments[6];

                if (!string.IsNullOrWhiteSpace(cell) && !string.IsNullOrWhiteSpace(pad))
                    AccountPresenceTracker.Instance.UpdateCellPad(cell, pad);
            }
            catch
            {
            }
        }
    }

    public class HandlerAccountPresenceUotls : IXtMessageHandler
    {
        public string[] HandledCommands { get; } = { "uotls" };

        public void Handle(XtMessage message)
        {
            try
            {
                if (message.Arguments == null || message.Arguments.Length < 6)
                    return;

                string username = message.Arguments[4];
                string movement = message.Arguments[5];
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(movement))
                    return;

                if (!username.Equals(Player.Username, System.StringComparison.OrdinalIgnoreCase))
                    return;

                string cell = null;
                string pad = null;
                foreach (string part in movement.Split(','))
                {
                    if (part.StartsWith("strFrame:"))
                        cell = part.Substring("strFrame:".Length);
                    else if (part.StartsWith("strPad:"))
                        pad = part.Substring("strPad:".Length);
                }

                if (!string.IsNullOrWhiteSpace(cell) || !string.IsNullOrWhiteSpace(pad))
                    AccountPresenceTracker.Instance.UpdateCellPad(cell, pad);
            }
            catch
            {
            }
        }
    }

    public class HandlerAccountPresenceLogout : IXtMessageHandler
    {
        public string[] HandledCommands { get; } = { "logout" };

        public void Handle(XtMessage message)
        {
            AccountPresenceTracker.Instance.MarkCurrentSessionOffline();
        }
    }

    public class HandlerAccountPresenceFirstJoin : IXtMessageHandler
    {
        public string[] HandledCommands { get; } = { "firstJoin" };

        public void Handle(XtMessage message)
        {
            AccountPresenceTracker.Instance.StartTrackingCurrentSession();
            AccountPresenceTracker.Instance.RefreshNow();
        }
    }
}
