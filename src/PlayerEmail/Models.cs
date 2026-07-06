using Newtonsoft.Json;

namespace PlayerMail
{
    public class PlayerMailData
    {
        [JsonProperty("邮箱")]
        public string Email = "";

        [JsonProperty("绑定时间戳")]
        public long BindTime = 0;
    }

    public class InboxMessage
    {
        [JsonProperty("id")]
        public string Id = "";

        [JsonProperty("from")]
        public string FromPlayer = "";

        [JsonProperty("to")]
        public string ToPlayer = "";

        [JsonProperty("content")]
        public string Content = "";

        [JsonProperty("time")]
        public string SendTime = "";

        [JsonProperty("read")]
        public bool IsRead = false;
    }
}
