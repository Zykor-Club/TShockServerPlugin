using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TShockAPI;

namespace AIAgent;

public static class ChatUtils
{
    public static long EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        double count = 0;
        foreach (var c in text)
        {
            if (c > 127)
                count += 1.5;
            else if (char.IsLetter(c))
                count += 0.3;
            else
                count += 0.2;
        }
        return Math.Max(1, (long)Math.Ceiling(count));
    }

    public static int EstimateTokensFromChars(int charCount) => (int)(charCount * 0.5) + 10;

    public static int CountCharacters(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : text.Length;
    }

    public static string TruncateToChars(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        var cutIndex = maxChars;
        var lastPeriod = text.LastIndexOfAny(new[] { '\u3002', '\uff01', '\uff1f', '.', '!', '?' }, cutIndex);
        if (lastPeriod > cutIndex * 3 / 4)
            cutIndex = lastPeriod + 1;

        return text.Substring(0, Math.Min(cutIndex, text.Length)) + "...";
    }

    public static string GetChatCompletionsUrl()
    {
        var baseUrl = PluginState.Config.BaseUrl.TrimEnd('/');
        if (baseUrl.EndsWith("/chat/completions"))
            return baseUrl;
        if (baseUrl.EndsWith("/v1"))
            return $"{baseUrl}/chat/completions";
        return $"{baseUrl}/v1/chat/completions";
    }

    public static string BuildSystemPrompt(string playerName)
    {
        var cfg = PluginState.Config;
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(cfg.Persona))
        {
            sb.AppendLine($"你是{cfg.AIName}。{cfg.Persona}");
            var strictDesc = cfg.PersonaStrictness switch
            {
                <= 3 => "你可以在遵循人设核心特征的基础上，根据对话情境自然发挥。",
                <= 6 => "你需要保持人设的一致性，在回答中体现人设的核心特征。",
                <= 8 => "你必须严格遵守人设设定，你的性格、说话方式和行为准则都必须与人设完全一致。",
                _ => "你必须绝对遵守人设设定，每一句话都必须严格符合人设，绝不允许偏离。"
            };
            sb.AppendLine($"[人设遵守等级{cfg.PersonaStrictness}/10] {strictDesc}");
        }
        else
        {
            sb.AppendLine($"你是{cfg.AIName}，Terraria服务器的智能AI助手。");
        }

        if (!string.IsNullOrWhiteSpace(cfg.AIKeywords))
            sb.AppendLine($"[核心关键词/提示词] 在回答玩家问题时，请围绕以下内容进行输出或搜索: {cfg.AIKeywords}");

        sb.AppendLine($"当前与你对话的玩家是 \"{playerName}\"。");

        if (cfg.EnableMailFeature)
        {
            sb.AppendLine("你拥有邮件发送能力。当玩家要求你发送邮件给某个邮箱时，你可以直接帮玩家发送邮件。");
            sb.AppendLine("发送邮件格式（隐藏在回复中，玩家看不到）：");
            sb.AppendLine(">>>SEND_EMAIL<<<");
            sb.AppendLine("收件人邮箱: xxx@xxx.com");
            sb.AppendLine("邮件内容: 这里是邮件的具体内容");
            sb.AppendLine(">>>END_EMAIL<<<");
            sb.AppendLine("在邮件标记外的内容是对玩家的正常回复，邮件标记会被系统自动处理。");
        }

        if (cfg.MaxResponseLength > 0)
        {
            if (cfg.ResponseLimitMode == "truncate")
                sb.AppendLine($"你的回答应尽量简洁，建议控制在{cfg.MaxResponseLength}字以内。");
            else
                sb.AppendLine($"你必须将回答严格控制在{cfg.MaxResponseLength}字以内。");
        }

        return sb.ToString();
    }

    public static bool SendEmail(TSPlayer plr, string toEmail, string content)
    {
        if (!PluginState.Config.EnableMailFeature)
        {
            plr.SendErrorMessage("[AIAgent] 邮件功能已禁用。");
            return false;
        }

        if (!PluginState.PlayerMailDetected || PluginState.PlayerMailPluginInstance == null)
        {
            plr.SendErrorMessage("[AIAgent] 邮件插件未就绪，正在尝试重新检测...");
            TryDetectPlayerMailPlugin();
            if (!PluginState.PlayerMailDetected)
            {
                plr.SendErrorMessage("[AIAgent] 邮件插件检测失败，无法发送邮件。");
                return false;
            }
        }

        try
        {
            var senderProp = PluginState.PlayerMailPluginInstance.GetType()
                .GetProperty("Sender", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (senderProp == null) { plr.SendErrorMessage("[AIAgent] 无法获取邮件发送器属性。"); return false; }

            var sender = senderProp?.GetValue(PluginState.PlayerMailPluginInstance);
            if (sender == null) { plr.SendErrorMessage("[AIAgent] 邮件发送器未初始化。"); return false; }

            var sendTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var group = plr.Group.Name;
            var fromEmail = "aiagent@server.com";

            if (PluginState.MailSenderMethod != null)
            {
                PluginState.MailSenderMethod.Invoke(sender, new object[] { toEmail, plr.Name, plr.Name, group, fromEmail, content, sendTime });
                TShock.Log.Info($"[AIAgent] 玩家 {plr.Name} 通过AI发送邮件至 {toEmail}");
                return true;
            }
            else
            {
                var senderType = sender.GetType();
                var method = senderType.GetMethod("SendPlayerEmail",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string) }, null);
                if (method != null)
                {
                    PluginState.MailSenderMethod = method;
                    method.Invoke(sender, new object[] { toEmail, plr.Name, plr.Name, group, fromEmail, content, sendTime });
                    TShock.Log.Info($"[AIAgent] 玩家 {plr.Name} 通过AI发送邮件至 {toEmail}");
                    return true;
                }
                plr.SendErrorMessage("[AIAgent] 邮件发送方法不可用。");
                return false;
            }
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[AIAgent] 邮件发送失败: {ex.Message}");
            plr.SendErrorMessage($"[AIAgent] 邮件发送失败: {ex.Message}");
            return false;
        }
    }

    public static void TryDetectPlayerMailPlugin()
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var types = assembly.GetTypes();
                    var mailPluginType = types.FirstOrDefault(t => t.FullName == "PlayerMail.PlayerMailPlugin");
                    if (mailPluginType == null) continue;

                    var instanceProp = mailPluginType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (instanceProp == null) continue;

                    var instance = instanceProp.GetValue(null);
                    if (instance == null) continue;

                    PluginState.PlayerMailPluginInstance = instance;

                    var senderProp = mailPluginType.GetProperty("Sender", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (senderProp == null) continue;

                    var sender = senderProp.GetValue(instance);
                    if (sender == null) continue;

                    var senderType = sender.GetType();
                    PluginState.MailSenderMethod = senderType.GetMethod("SendPlayerEmail",
                        BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string) }, null);

                    PluginState.PlayerMailDetected = true;
                    TShock.Log.Info("[AIAgent] PlayerMail 邮件联动功能已启用");
                    return;
                }
                catch { continue; }
            }
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[AIAgent] 检测 PlayerMail 失败: {ex.Message}");
        }
    }

    public static (string? email, string? content) ExtractEmailInfo(string aiResp)
    {
        var start = aiResp.IndexOf(">>>SEND_EMAIL<<<");
        var end = aiResp.IndexOf(">>>END_EMAIL<<<");
        if (start == -1 || end <= start) return (null, null);

        var block = aiResp.Substring(start + 16, end - start - 16).Trim();
        string? email = null;
        string? content = null;

        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.Trim();
            var emailMatch = Regex.Match(trimmed,
                @"(?:收件人邮箱|收件人|邮箱|收件邮箱|TO|To|to)\s*[：:]\s*(.+@.+\..+)", RegexOptions.IgnoreCase);
            if (emailMatch.Success) { email = emailMatch.Groups[1].Value.Trim(); continue; }

            var contentMatch = Regex.Match(trimmed,
                @"(?:邮件内容|内容|正文|Content|content)\s*[：:]\s*(.+)", RegexOptions.IgnoreCase);
            if (contentMatch.Success) content = contentMatch.Groups[1].Value.Trim();
        }

        if (string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(email))
        {
            var contentLines = block.Split('\n')
                .Where(l => !Regex.IsMatch(l.Trim(),
                    @"(?:收件人邮箱|收件人|邮箱|收件邮箱|TO|To|to)\s*[：:]\s*.+@.+\..+", RegexOptions.IgnoreCase))
                .ToList();
            if (contentLines.Count > 0)
                content = string.Join("\n", contentLines).Trim();
        }

        return (email, content);
    }
}