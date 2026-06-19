using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Grimoire.Game.Data
{
    [Serializable]
    public class AccountPresenceData
    {
        [JsonProperty("trackerId")]
        public string TrackerId { get; set; }

        [JsonProperty("processId")]
        public int ProcessId { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("userId")]
        public int UserId { get; set; }

        [JsonProperty("map")]
        public string Map { get; set; }

        [JsonProperty("mapName")]
        public string MapName { get; set; }

        [JsonProperty("roomNumber")]
        public int? RoomNumber { get; set; }

        [JsonProperty("cell")]
        public string Cell { get; set; }

        [JsonProperty("pad")]
        public string Pad { get; set; }

        [JsonProperty("server")]
        public string Server { get; set; }

        [JsonProperty("isLoggedIn")]
        public bool IsLoggedIn { get; set; }

        [JsonProperty("isOnline")]
        public bool IsOnline { get; set; }

        [JsonProperty("lastUpdatedUtc")]
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    [Serializable]
    public class AccountPresenceCollection
    {
        [JsonProperty("accounts")]
        public List<AccountPresenceData> Accounts { get; set; } = new List<AccountPresenceData>();

        [JsonProperty("lastUpdatedUtc")]
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
