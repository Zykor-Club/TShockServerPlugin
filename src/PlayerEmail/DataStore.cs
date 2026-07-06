using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using TShockAPI;

namespace PlayerMail
{
    public interface IDataStore : IDisposable
    {
        PlayerMailData GetPlayerData(string playerName);
        void SetPlayerData(string playerName, PlayerMailData data);
        bool RemovePlayerData(string playerName);
        Dictionary<string, PlayerMailData> GetAllPlayerData();
        void ClearAllPlayerData();

        List<InboxMessage> GetInboxForPlayer(string playerName);
        void AddInboxMessage(InboxMessage msg);
        void MarkInboxRead(string messageId);

        bool IsBlacklisted(string playerName);
        bool AddBlacklist(string playerName);
        bool RemoveBlacklist(string playerName);
        List<string> GetBlacklist();
    }

    public class JsonDataStore : IDataStore
    {
        private readonly string SaveDir;
        private readonly string PlayersPath;
        private readonly string InboxPath;
        private readonly string BlacklistPath;
        private Dictionary<string, PlayerMailData> Players;
        private List<InboxMessage> InboxMessages;
        private HashSet<string> Blacklist;

        public JsonDataStore()
        {
            SaveDir = Path.Combine(TShock.SavePath, "PlayerMail");
            PlayersPath = Path.Combine(SaveDir, "players.json");
            InboxPath = Path.Combine(SaveDir, "inbox.json");
            BlacklistPath = Path.Combine(SaveDir, "blacklist.json");
            LoadAll();
        }

        private void LoadAll()
        {
            Directory.CreateDirectory(SaveDir);

            if (File.Exists(PlayersPath))
            {
                var json = File.ReadAllText(PlayersPath);
                var raw = JsonConvert.DeserializeObject(json, typeof(Dictionary<string, PlayerMailData>)) as Dictionary<string, PlayerMailData>;
                Players = new Dictionary<string, PlayerMailData>(raw ?? new Dictionary<string, PlayerMailData>(), StringComparer.OrdinalIgnoreCase);
            }
            else Players = new Dictionary<string, PlayerMailData>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(InboxPath))
            {
                var json = File.ReadAllText(InboxPath);
                InboxMessages = JsonConvert.DeserializeObject(json, typeof(List<InboxMessage>)) as List<InboxMessage> ?? new List<InboxMessage>();
            }
            else InboxMessages = new List<InboxMessage>();

            if (File.Exists(BlacklistPath))
            {
                var json = File.ReadAllText(BlacklistPath);
                var list = JsonConvert.DeserializeObject(json, typeof(List<string>)) as List<string> ?? new List<string>();
                Blacklist = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
            else Blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private void SavePlayers() => File.WriteAllText(PlayersPath, JsonConvert.SerializeObject(Players, Formatting.Indented));
        private void SaveInbox() => File.WriteAllText(InboxPath, JsonConvert.SerializeObject(InboxMessages, Formatting.Indented));
        private void SaveBlacklist() => File.WriteAllText(BlacklistPath, JsonConvert.SerializeObject(Blacklist.ToList(), Formatting.Indented));

        public PlayerMailData GetPlayerData(string playerName) => Players.TryGetValue(playerName, out var data) ? data : null;
        public void SetPlayerData(string playerName, PlayerMailData data) { Players[playerName] = data; SavePlayers(); }
        public bool RemovePlayerData(string playerName) { if (Players.Remove(playerName)) { SavePlayers(); return true; } return false; }
        public Dictionary<string, PlayerMailData> GetAllPlayerData() => new Dictionary<string, PlayerMailData>(Players, StringComparer.OrdinalIgnoreCase);
        public void ClearAllPlayerData() { Players.Clear(); SavePlayers(); }

        public List<InboxMessage> GetInboxForPlayer(string playerName) => InboxMessages.Where(m => m.ToPlayer == playerName).OrderByDescending(m => m.SendTime).ToList();
        public void AddInboxMessage(InboxMessage msg) { InboxMessages.Add(msg); SaveInbox(); }
        public void MarkInboxRead(string messageId) { var msg = InboxMessages.FirstOrDefault(m => m.Id == messageId); if (msg != null) { msg.IsRead = true; SaveInbox(); } }

        public bool IsBlacklisted(string playerName) => Blacklist.Contains(playerName);
        public bool AddBlacklist(string playerName) { if (Blacklist.Add(playerName)) { SaveBlacklist(); return true; } return false; }
        public bool RemoveBlacklist(string playerName) { if (Blacklist.Remove(playerName)) { SaveBlacklist(); return true; } return false; }
        public List<string> GetBlacklist() => Blacklist.ToList();

        public void Dispose() { }
    }

    public class MySqlDataStore : IDataStore
    {
        private readonly string ConnectionString;
        private readonly string ServerId;

        public MySqlDataStore(string connectionString)
        {
            ConnectionString = connectionString;
            ServerId = TShock.Config.Settings.ServerName ?? Environment.MachineName;
            InitTables();
        }

        private void InitTables()
        {
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS playermail_players (
                    PlayerName VARCHAR(50) PRIMARY KEY,
                    Email VARCHAR(100) NOT NULL,
                    BindTime BIGINT NOT NULL,
                    ServerId VARCHAR(50),
                    LastUpdate TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                CREATE TABLE IF NOT EXISTS playermail_inbox (
                    Id VARCHAR(32) PRIMARY KEY,
                    FromPlayer VARCHAR(50) NOT NULL,
                    ToPlayer VARCHAR(50) NOT NULL,
                    Content TEXT NOT NULL,
                    SendTime VARCHAR(20) NOT NULL,
                    IsRead BOOLEAN DEFAULT FALSE,
                    ServerId VARCHAR(50),
                    INDEX idx_toPlayer (ToPlayer),
                    INDEX idx_isRead (IsRead)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                CREATE TABLE IF NOT EXISTS playermail_blacklist (
                    PlayerName VARCHAR(50) PRIMARY KEY,
                    AddedBy VARCHAR(50),
                    AddTime TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    ServerId VARCHAR(50)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
            cmd.ExecuteNonQuery();
        }

        public PlayerMailData GetPlayerData(string playerName)
        {
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Email, BindTime FROM playermail_players WHERE PlayerName = @name";
            cmd.Parameters.AddWithValue("@name", playerName);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return new PlayerMailData { Email = reader.GetString("Email"), BindTime = reader.GetInt64("BindTime") };
            return null;
        }

        public void SetPlayerData(string playerName, PlayerMailData data)
        {
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO playermail_players (PlayerName, Email, BindTime, ServerId)
                VALUES (@name, @email, @time, @server)
                ON DUPLICATE KEY UPDATE Email = @email, BindTime = @time, ServerId = @server";
            cmd.Parameters.AddWithValue("@name", playerName);
            cmd.Parameters.AddWithValue("@email", data.Email);
            cmd.Parameters.AddWithValue("@time", data.BindTime);
            cmd.Parameters.AddWithValue("@server", ServerId);
            cmd.ExecuteNonQuery();
        }

        public bool RemovePlayerData(string playerName)
        {
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM playermail_players WHERE PlayerName = @name";
            cmd.Parameters.AddWithValue("@name", playerName);
            return cmd.ExecuteNonQuery() > 0;
        }

        public Dictionary<string, PlayerMailData> GetAllPlayerData()
        {
            var dict = new Dictionary<string, PlayerMailData>(StringComparer.OrdinalIgnoreCase);
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT PlayerName, Email, BindTime FROM playermail_players";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString("PlayerName");
                dict[name] = new PlayerMailData { Email = reader.GetString("Email"), BindTime = reader.GetInt64("BindTime") };
            }
            return dict;
        }

        public void ClearAllPlayerData()
        {
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM playermail_players";
            cmd.ExecuteNonQuery();
        }

        public List<InboxMessage> GetInboxForPlayer(string playerName)
        {
            var list = new List<InboxMessage>();
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM playermail_inbox WHERE ToPlayer = @name ORDER BY SendTime DESC";
            cmd.Parameters.AddWithValue("@name", playerName);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new InboxMessage
                {
                    Id = reader.GetString("Id"),
                    FromPlayer = reader.GetString("FromPlayer"),
                    ToPlayer = reader.GetString("ToPlayer"),
                    Content = reader.GetString("Content"),
                    SendTime = reader.GetString("SendTime"),
                    IsRead = reader.GetBoolean("IsRead")
                });
            }
            return list;
        }

        public void AddInboxMessage(InboxMessage msg)
        {
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO playermail_inbox (Id, FromPlayer, ToPlayer, Content, SendTime, IsRead, ServerId)
                VALUES (@id, @from, @to, @content, @time, @read, @server)";
            cmd.Parameters.AddWithValue("@id", msg.Id);
            cmd.Parameters.AddWithValue("@from", msg.FromPlayer);
            cmd.Parameters.AddWithValue("@to", msg.ToPlayer);
            cmd.Parameters.AddWithValue("@content", msg.Content);
            cmd.Parameters.AddWithValue("@time", msg.SendTime);
            cmd.Parameters.AddWithValue("@read", msg.IsRead);
            cmd.Parameters.AddWithValue("@server", ServerId);
            cmd.ExecuteNonQuery();
        }

        public void MarkInboxRead(string messageId)
        {
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE playermail_inbox SET IsRead = TRUE WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", messageId);
            cmd.ExecuteNonQuery();
        }

        public bool IsBlacklisted(string playerName)
        {
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM playermail_blacklist WHERE PlayerName = @name";
            cmd.Parameters.AddWithValue("@name", playerName);
            return cmd.ExecuteScalar() != null;
        }

        public bool AddBlacklist(string playerName)
        {
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO playermail_blacklist (PlayerName, ServerId) VALUES (@name, @server)";
                cmd.Parameters.AddWithValue("@name", playerName);
                cmd.Parameters.AddWithValue("@server", ServerId);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch { return false; }
        }

        public bool RemoveBlacklist(string playerName)
        {
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM playermail_blacklist WHERE PlayerName = @name";
            cmd.Parameters.AddWithValue("@name", playerName);
            return cmd.ExecuteNonQuery() > 0;
        }

        public List<string> GetBlacklist()
        {
            var list = new List<string>();
            using var conn = new MySqlConnection(ConnectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT PlayerName FROM playermail_blacklist";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(reader.GetString("PlayerName"));
            return list;
        }

        public void Dispose() { }
    }
}
