using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Grimoire.Game;
using Grimoire.Game.Data;
using Grimoire.Networking;
using Newtonsoft.Json;

namespace Grimoire.Tools
{
    public class AccountPresenceTracker : IDisposable
    {
        private static AccountPresenceTracker _instance;
        private static readonly object _instanceLock = new object();

        private readonly string _trackerDir;
        private readonly string _collectionPath;
        private readonly string _trackerId;
        private readonly int _processId;
        private readonly Mutex _fileMutex;

        private bool _disposed;
        private bool _sessionOnline;
        private string _lastJoinedMap;
        private string _lastCell;
        private string _lastPad;

        public static AccountPresenceTracker Instance
        {
            get
            {
                lock (_instanceLock)
                {
                    return _instance ?? (_instance = new AccountPresenceTracker());
                }
            }
        }

        private AccountPresenceTracker()
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Grimoire");
            _trackerDir = Path.Combine(appDataPath, "Accounts");
            _collectionPath = Path.Combine(_trackerDir, "account-presence.json");
            _processId = Process.GetCurrentProcess().Id;
            _trackerId = $"{Environment.MachineName}:{_processId}";
            _fileMutex = new Mutex(false, "Grimoire_AccountPresenceTracker_FileLock");

            Directory.CreateDirectory(_trackerDir);

            AppDomain.CurrentDomain.ProcessExit += (_, __) => SafeMarkOffline();
            AppDomain.CurrentDomain.DomainUnload += (_, __) => SafeMarkOffline();
        }

        public void StartTrackingCurrentSession()
        {
            if (_disposed)
                return;

            _sessionOnline = true;
            SafeUpdateCurrentState();
        }

        public void StopTrackingCurrentSession()
        {
        }

        public void RefreshNow()
        {
            SafeUpdateCurrentState();
        }

        public void UpdateJoinedMap(string fullMap, string cell = null, string pad = null)
        {
            _sessionOnline = true;
            if (!string.IsNullOrWhiteSpace(fullMap))
                _lastJoinedMap = fullMap.Trim().ToLower();
            if (!string.IsNullOrWhiteSpace(cell))
                _lastCell = cell.Trim();
            if (!string.IsNullOrWhiteSpace(pad))
                _lastPad = pad.Trim();
            SafeUpdateCurrentState();
        }

        public void UpdateCellPad(string cell, string pad)
        {
            _sessionOnline = true;
            if (!string.IsNullOrWhiteSpace(cell))
                _lastCell = cell.Trim();
            if (!string.IsNullOrWhiteSpace(pad))
                _lastPad = pad.Trim();
            SafeUpdateCurrentState();
        }

        public void MarkCurrentSessionOffline()
        {
            if (_disposed)
                return;

            _sessionOnline = false;
            UpsertCurrentAccount(false);
            StopTrackingCurrentSession();
        }

        public List<AccountPresenceData> GetTrackedAccounts()
        {
            return ExecuteWithFileLock(() =>
            {
                AccountPresenceCollection collection = LoadCollectionUnsafe();
                return collection.Accounts
                    .OrderByDescending(a => a.LastUpdatedUtc)
                    .ToList();
            }, new List<AccountPresenceData>());
        }

        private void SafeUpdateCurrentState()
        {
            try
            {
                UpdateCurrentState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AccountPresenceTracker update failed: {ex.Message}");
            }
        }

        private void SafeMarkOffline()
        {
            try
            {
                MarkCurrentSessionOffline();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AccountPresenceTracker offline mark failed: {ex.Message}");
            }
        }

        private void UpdateCurrentState()
        {
            if (!_sessionOnline)
            {
                MarkCurrentSessionOffline();
                return;
            }

            UpsertCurrentAccount(true);
        }

        private void UpsertCurrentAccount(bool isOnline)
        {
            string currentUsername = SafeGet(() => Player.Username, string.Empty);
            ExecuteWithFileLock(() =>
            {
                AccountPresenceCollection collection = LoadCollectionUnsafe();
                AccountPresenceData current = FindExistingAccount(collection, currentUsername);
                if (current == null)
                {
                    current = new AccountPresenceData
                    {
                        TrackerId = _trackerId,
                        ProcessId = _processId
                    };
                    collection.Accounts.Add(current);
                }

                PopulateCurrentState(current, isOnline);
                RemoveDuplicateAccounts(collection, current);
                collection.LastUpdatedUtc = DateTime.UtcNow;

                SaveCollectionUnsafe(collection);
                return true;
            }, false);
        }

        private AccountPresenceData FindExistingAccount(AccountPresenceCollection collection, string username)
        {
            if (!string.IsNullOrWhiteSpace(username))
            {
                AccountPresenceData accountByUsername = collection.Accounts.FirstOrDefault(a =>
                    !string.IsNullOrWhiteSpace(a.Username) &&
                    a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                if (accountByUsername != null)
                    return accountByUsername;
            }

            return collection.Accounts.FirstOrDefault(a => a.TrackerId == _trackerId);
        }

        private void RemoveDuplicateAccounts(AccountPresenceCollection collection, AccountPresenceData current)
        {
            if (current == null)
                return;

            for (int i = collection.Accounts.Count - 1; i >= 0; i--)
            {
                AccountPresenceData account = collection.Accounts[i];
                if (ReferenceEquals(account, current))
                    continue;

                bool sameUsername = !string.IsNullOrWhiteSpace(current.Username) &&
                    !string.IsNullOrWhiteSpace(account.Username) &&
                    account.Username.Equals(current.Username, StringComparison.OrdinalIgnoreCase);
                bool emptyOfflineGhost = string.IsNullOrWhiteSpace(account.Username) && !account.IsOnline && !account.IsLoggedIn;

                if (sameUsername || emptyOfflineGhost)
                    collection.Accounts.RemoveAt(i);
            }
        }

        private void PopulateCurrentState(AccountPresenceData account, bool isOnline)
        {
            string username = SafeGet(() => Player.Username, string.Empty);
            string playerMap = SafeGet(() => Player.Map, string.Empty);
            string playerCell = SafeGet(() => Player.Cell, string.Empty);
            string playerPad = SafeGet(() => Player.Pad, string.Empty);
            int userId = SafeGet(() => Player.UserID, 0);
            string map = GetEffectiveMap(playerMap);
            string cell = string.IsNullOrWhiteSpace(_lastCell) ? playerCell : _lastCell;
            string pad = string.IsNullOrWhiteSpace(_lastPad) ? playerPad : _lastPad;
            string server = SafeGet(() => Proxy.Instance.DestinationServerOverride?.Name, string.Empty);

            account.ProcessId = _processId;
            if (!string.IsNullOrWhiteSpace(username))
                account.Username = username;
            if (userId > 0)
                account.UserId = userId;
            if (!string.IsNullOrWhiteSpace(map))
            {
                account.Map = map;
                account.MapName = GetMapName(map);
                account.RoomNumber = GetRoomNumber(map);
            }
            if (!string.IsNullOrWhiteSpace(cell))
                account.Cell = cell;
            if (!string.IsNullOrWhiteSpace(pad))
                account.Pad = pad;
            if (!string.IsNullOrWhiteSpace(server))
                account.Server = server;
            else if (string.IsNullOrWhiteSpace(account.Server))
                account.Server = string.Empty;
            account.IsLoggedIn = isOnline;
            account.IsOnline = isOnline;
            account.LastUpdatedUtc = DateTime.UtcNow;
        }

        private string GetEffectiveMap(string playerMap)
        {
            if (string.IsNullOrWhiteSpace(_lastJoinedMap))
                return playerMap;

            if (string.IsNullOrWhiteSpace(playerMap))
                return _lastJoinedMap;

            string joinedBaseMap = GetMapName(_lastJoinedMap);
            return joinedBaseMap.Equals(playerMap, StringComparison.OrdinalIgnoreCase)
                ? _lastJoinedMap
                : playerMap;
        }

        private string GetMapName(string map)
        {
            if (string.IsNullOrWhiteSpace(map))
                return string.Empty;

            int index = map.LastIndexOf('-');
            return index > 0 ? map.Substring(0, index) : map;
        }

        private int? GetRoomNumber(string map)
        {
            if (string.IsNullOrWhiteSpace(map))
                return null;

            int index = map.LastIndexOf('-');
            if (index < 0 || index >= map.Length - 1)
                return null;

            return int.TryParse(map.Substring(index + 1), out int roomNumber)
                ? roomNumber
                : (int?)null;
        }

        private AccountPresenceCollection LoadCollectionUnsafe()
        {
            if (!File.Exists(_collectionPath))
                return new AccountPresenceCollection();

            try
            {
                string json = File.ReadAllText(_collectionPath);
                return JsonConvert.DeserializeObject<AccountPresenceCollection>(json) ?? new AccountPresenceCollection();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AccountPresenceTracker load failed: {ex.Message}");
                return new AccountPresenceCollection();
            }
        }

        private void SaveCollectionUnsafe(AccountPresenceCollection collection)
        {
            try
            {
                string json = JsonConvert.SerializeObject(collection, Formatting.Indented);
                string tempPath = _collectionPath + ".tmp";

                File.WriteAllText(tempPath, json);
                if (File.Exists(_collectionPath))
                    File.Delete(_collectionPath);
                File.Move(tempPath, _collectionPath);
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"AccountPresenceTracker atomic save failed: {ex.Message}");
                File.WriteAllText(_collectionPath, JsonConvert.SerializeObject(collection, Formatting.Indented));
            }
        }

        private T ExecuteWithFileLock<T>(Func<T> action, T fallback)
        {
            bool lockTaken = false;
            try
            {
                lockTaken = _fileMutex.WaitOne(TimeSpan.FromSeconds(2));
                if (!lockTaken)
                    return fallback;

                return action();
            }
            catch (AbandonedMutexException)
            {
                return action();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AccountPresenceTracker file operation failed: {ex.Message}");
                return fallback;
            }
            finally
            {
                if (lockTaken)
                    _fileMutex.ReleaseMutex();
            }
        }

        private T SafeGet<T>(Func<T> getter, T fallback)
        {
            try
            {
                return getter();
            }
            catch
            {
                return fallback;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            SafeMarkOffline();

            _fileMutex?.Dispose();
        }
    }
}
