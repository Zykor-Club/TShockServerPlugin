using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Terraria;
using TShockAPI;

namespace PlayerMail
{
    public class MailSender
    {
        private readonly MailConfig Config;
        private static readonly Regex EmailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        public MailSender(MailConfig config)
        {
            Config = config;
        }

        public bool IsValidEmail(string email)
        {
            return EmailRegex.IsMatch(email);
        }

        public void SendVerifyEmail(string toEmail, string playerName, string code)
        {
            var serverName = GetServerName();
            var subject = ReplaceTemplate(Config.验证码邮件标题模板, new Dictionary<string, string>
            {
                { "ServerName", serverName },
                { "ServerDisplayName", Config.发件人显示名称 },
                { "ToPlayer", playerName },
                { "VerifyCode", code }
            });
            var body = ReplaceTemplate(Config.验证码邮件正文模板, new Dictionary<string, string>
            {
                { "ServerName", serverName },
                { "ServerDisplayName", Config.发件人显示名称 },
                { "ToPlayer", playerName },
                { "VerifyCode", code }
            });
            Send(toEmail, playerName, Config.发件人显示名称, null, subject, body);
        }

        public void SendPlayerEmail(string toEmail, string toPlayer, string fromPlayer, string fromGroup, string fromEmail, string content, string sendTime)
        {
            var serverName = GetServerName();
            var subject = ReplaceTemplate(Config.邮件标题模板, new Dictionary<string, string>
            {
                { "ServerName", serverName },
                { "ServerDisplayName", Config.发件人显示名称 },
                { "FromPlayer", fromPlayer },
                { "ToPlayer", toPlayer },
                { "FromGroup", fromGroup },
                { "FromEmail", fromEmail },
                { "Content", content },
                { "SendTime", sendTime }
            });
            var body = ReplaceTemplate(Config.邮件正文模板, new Dictionary<string, string>
            {
                { "ServerName", serverName },
                { "ServerDisplayName", Config.发件人显示名称 },
                { "FromPlayer", fromPlayer },
                { "ToPlayer", toPlayer },
                { "FromGroup", fromGroup },
                { "FromEmail", fromEmail },
                { "Content", content },
                { "SendTime", sendTime }
            });
            Send(toEmail, toPlayer, $"{fromPlayer} via {Config.发件人显示名称}", fromEmail, subject, body);
        }

        public void SendBroadcastEmail(string toEmail, string toPlayer, string fromPlayer, string fromGroup, string content, string sendTime)
        {
            var serverName = GetServerName();
            var subject = ReplaceTemplate(Config.广播邮件标题模板, new Dictionary<string, string>
            {
                { "ServerName", serverName },
                { "ServerDisplayName", Config.发件人显示名称 },
                { "FromPlayer", fromPlayer },
                { "FromGroup", fromGroup },
                { "Content", content },
                { "SendTime", sendTime }
            });
            var body = ReplaceTemplate(Config.广播邮件正文模板, new Dictionary<string, string>
            {
                { "ServerName", serverName },
                { "ServerDisplayName", Config.发件人显示名称 },
                { "FromPlayer", fromPlayer },
                { "FromGroup", fromGroup },
                { "Content", content },
                { "SendTime", sendTime }
            });
            Send(toEmail, toPlayer, $"{fromPlayer} via {Config.发件人显示名称}", null, subject, body);
        }

        private static string GetServerName()
        {
            return TShock.Config.Settings.ServerName ?? Main.worldName ?? "泰拉瑞亚服务器";
        }

        private static string ReplaceTemplate(string template, Dictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(template)) return "";
            var result = template;
            foreach (var kv in values)
            {
                result = result.Replace($"{{{kv.Key}}}", kv.Value ?? "");
            }
            return result;
        }

        private void Send(string toEmail, string toName, string fromDisplay, string replyTo, string subject, string body)
        {
            using var client = new SmtpClient(Config.SMTP服务器, Config.SMTP端口);
            client.EnableSsl = true;
            client.Credentials = new NetworkCredential(Config.SMTP账号, Config.SMTP密码或授权码);

            var msg = new MailMessage();
            msg.From = new MailAddress(Config.发件人邮箱, fromDisplay);
            if (!string.IsNullOrEmpty(replyTo))
                msg.ReplyToList.Add(new MailAddress(replyTo));
            msg.To.Add(new MailAddress(toEmail, toName));
            msg.Subject = subject;
            msg.Body = body;

            client.Send(msg);
        }
    }
}