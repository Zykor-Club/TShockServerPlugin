using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayerMail
{
    public class InboxManager
    {
        private readonly IDataStore Store;

        public InboxManager(IDataStore store)
        {
            Store = store;
        }

        public void Add(InboxMessage msg)
        {
            Store.AddInboxMessage(msg);
        }

        public List<InboxMessage> GetForPlayer(string playerName)
        {
            return Store.GetInboxForPlayer(playerName);
        }

        public InboxMessage GetLatestUnread(string playerName)
        {
            return Store.GetInboxForPlayer(playerName).Where(m => !m.IsRead).OrderByDescending(m => m.SendTime).FirstOrDefault();
        }

        public void MarkRead(string id)
        {
            Store.MarkInboxRead(id);
        }
    }

    public class VerifyManager
    {
        private readonly Dictionary<string, VerifyEntry> Codes = new Dictionary<string, VerifyEntry>();
        private readonly Dictionary<string, int> AttemptCounts = new Dictionary<string, int>();

        public class VerifyEntry
        {
            public string Code;
            public string Email;
            public DateTime ExpireTime;
        }

        public bool HasPending(string playerName)
        {
            return Codes.ContainsKey(playerName) && Codes[playerName].ExpireTime > DateTime.Now;
        }

        public string Generate(string playerName, string email, int expireSeconds)
        {
            var code = new Random().Next(1000, 9999).ToString();
            Codes[playerName] = new VerifyEntry
            {
                Code = code,
                Email = email,
                ExpireTime = DateTime.Now.AddSeconds(expireSeconds)
            };
            if (!AttemptCounts.ContainsKey(playerName))
                AttemptCounts[playerName] = 0;
            AttemptCounts[playerName]++;
            return code;
        }

        public bool TryVerify(string playerName, string inputCode, out string email)
        {
            email = null;
            if (!Codes.TryGetValue(playerName, out var entry))
                return false;

            if (DateTime.Now > entry.ExpireTime)
            {
                Codes.Remove(playerName);
                return false;
            }

            if (entry.Code != inputCode)
                return false;

            email = entry.Email;
            Codes.Remove(playerName);
            AttemptCounts.Remove(playerName);
            return true;
        }

        public int GetAttemptCount(string playerName)
        {
            return AttemptCounts.TryGetValue(playerName, out var count) ? count : 0;
        }

        public void Clear(string playerName)
        {
            Codes.Remove(playerName);
            AttemptCounts.Remove(playerName);
        }
    }

    public class BlacklistManager
    {
        private readonly IDataStore Store;

        public BlacklistManager(IDataStore store)
        {
            Store = store;
        }

        public bool IsBlacklisted(string playerName)
        {
            return Store.IsBlacklisted(playerName);
        }

        public bool Add(string playerName)
        {
            return Store.AddBlacklist(playerName);
        }

        public bool Remove(string playerName)
        {
            return Store.RemoveBlacklist(playerName);
        }

        public List<string> GetList()
        {
            return Store.GetBlacklist();
        }
    }
}
