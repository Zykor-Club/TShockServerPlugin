using System.IO;
using Newtonsoft.Json;
using TShockAPI;

namespace PlayerMail
{
    public class MailConfig
    {
        [JsonProperty("SMTP服务器")]
        public string SMTP服务器 = "smtp.qq.com";

        [JsonProperty("SMTP端口")]
        public int SMTP端口 = 587;

        [JsonProperty("SMTP账号")]
        public string SMTP账号 = "";

        [JsonProperty("SMTP密码或授权码")]
        public string SMTP密码或授权码 = "";

        [JsonProperty("发件人邮箱")]
        public string 发件人邮箱 = "";

        [JsonProperty("发件人显示名称")]
        public string 发件人显示名称 = "泰拉瑞亚服务器";

        [JsonProperty("邮件标题模板")]
        public string 邮件标题模板 = "[{ServerName}] [{FromPlayer}] 给你发来了一条服务器消息";

        [JsonProperty("邮件正文模板")]
        public string 邮件正文模板 = "服务器名称: {ServerName}\n发送玩家: {FromPlayer}\n用户组: {FromGroup}\n玩家邮箱: {FromEmail}\n发送时间: {SendTime}\n\n消息内容:\n{Content}\n\n----------------\n由服务器 {ServerName} 代发\n如需回复，请直接回复此邮件或游戏内联系对方。";

        [JsonProperty("验证码邮件标题模板")]
        public string 验证码邮件标题模板 = "[{ServerName}] 邮箱绑定验证码";

        [JsonProperty("验证码邮件正文模板")]
        public string 验证码邮件正文模板 = "服务器名称: {ServerName}\n\n玩家: {ToPlayer}\n验证码: {VerifyCode}\n\n请在游戏内输入 /ml v {VerifyCode} 完成绑定。\n如非本人操作，请忽略此邮件。";

        [JsonProperty("广播邮件标题模板")]
        public string 广播邮件标题模板 = "[{ServerName}] 服务器广播消息";

        [JsonProperty("广播邮件正文模板")]
        public string 广播邮件正文模板 = "服务器名称: {ServerName}\n发送者: {FromPlayer} ({FromGroup})\n发送时间: {SendTime}\n\n广播内容:\n{Content}\n\n----------------\n由服务器 {ServerName} 群发";

        [JsonProperty("发送冷却秒数")]
        public int 发送冷却秒数 = 60;

        [JsonProperty("最大内容长度")]
        public int 最大内容长度 = 500;

        [JsonProperty("验证码过期秒数")]
        public int VerifyCodeExpireSeconds = 60;

        [JsonProperty("最大验证码申请次数")]
        public int 最大验证码申请次数 = 5;

        [JsonProperty("使用MySQL同步")]
        public bool 使用MySQL同步 = false;

        [JsonProperty("MySQL连接字符串")]
        public string MySQL连接字符串 = "Server=localhost;Database=terraria;Uid=root;Pwd=password;";

        [JsonProperty("进服前强制邮箱验证")]
        public bool CharmeleonStyle = false;

        [JsonProperty("断开提示文字")]
        public string DisconnectHint = @"你没有绑定邮箱，无法进入服务器。

请在下次连接时：
1. 在「服务器密码」框中输入你的邮箱地址
2. 系统会向该邮箱发送验证码
3. 在「服务器密码」框中输入收到的验证码
4. 完成后即可自动进入服务器

若已绑定，请直接输入服务器密码即可进入。";
    }

    public static class ConfigLoader
    {
        private static string SaveDir => Path.Combine(TShock.SavePath, "PlayerMail");
        private static string ConfigPath => Path.Combine(SaveDir, "config.json");

        public static MailConfig Load()
        {
            Directory.CreateDirectory(SaveDir);
            if (!File.Exists(ConfigPath))
            {
                var cfg = new MailConfig();
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(cfg, Formatting.Indented));
                return cfg;
            }
            var json = File.ReadAllText(ConfigPath);
            return JsonConvert.DeserializeObject<MailConfig>(json) ?? new MailConfig();
        }

        public static void Reload()
        {
            PlayerMailPlugin.Instance.Config = Load();
            PlayerMailPlugin.Instance.Sender = new MailSender(PlayerMailPlugin.Instance.Config);
        }
    }
}