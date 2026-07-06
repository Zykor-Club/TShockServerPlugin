using System;
using System.Linq;
using TShockAPI;

namespace AIAgent;

public static class CommandHandler
{
    public static void SendHelp(TSPlayer plr)
    {
        plr.SendInfoMessage("[c/00FF00:========== AIAgent 智能助手 ==========]");
        plr.SendInfoMessage("[c/FFD700:基础对话:]");
        plr.SendInfoMessage("  /aig <内容>          - 向AI发送简短问题");
        plr.SendInfoMessage("  /aig say <内容>      - 向AI发送长文本内容");
        plr.SendInfoMessage("[c/FFD700:模型管理:]");
        plr.SendInfoMessage("  /aig model list      - 查看支持的AI模型列表");
        plr.SendInfoMessage("  /aig model test      - 测试当前模型连接状态");
        plr.SendInfoMessage("[c/FFD700:个人数据:]");
        plr.SendInfoMessage("  /aig stats           - 查看你的Token消耗统计");

        if (plr.HasPermission("aiagent.admin"))
        {
            plr.SendInfoMessage("[c/FF6B6B:管理员命令:]");
            plr.SendInfoMessage("  /aig set stream yes/no     - 开启或关闭流式传输");
            plr.SendInfoMessage("  /aig set simple yes/no     - 开启或关闭极简模式");
            plr.SendInfoMessage("  /aig set websearch yes/no  - 开启或关闭联网搜索");
            plr.SendInfoMessage("  /aig set memory yes/no     - 开启或关闭上下文记忆");
            plr.SendInfoMessage("  /aig set mail yes/no       - 开启或关闭邮件联动功能");
            plr.SendInfoMessage("  /aig persona set <人设>    - 设置AI人设");
            plr.SendInfoMessage("  /aig persona strict <1-10> - 设置人设遵守严格度");
            plr.SendInfoMessage("  /aig keywords set <关键词>  - 设置AI关键词提示词");
            plr.SendInfoMessage("  /aig keywords clear        - 清空AI关键词提示词");
            plr.SendInfoMessage("  /aig whitelist add <玩家>  - 添加白名单");
            plr.SendInfoMessage("  /aig whitelist remove <玩家> - 移除白名单");
            plr.SendInfoMessage("  /aig whitelist list        - 查看白名单");
            plr.SendInfoMessage("  /aig stats all             - 查看全服Token统计");
            plr.SendInfoMessage("  /aig clearstats            - 清空所有统计");
        }
        plr.SendInfoMessage("[c/808080:========================================]");
    }

    public static void HandleSet(TSPlayer plr, CommandArgs args)
    {
        if (!plr.HasPermission("aiagent.admin"))
        {
            plr.SendErrorMessage("[AIAgent] 你没有权限修改配置，需要 aiagent.admin 权限。");
            return;
        }

        if (args.Parameters.Count < 3)
        {
            plr.SendErrorMessage("[AIAgent] 用法: /aig set <配置项> <yes/no>");
            plr.SendInfoMessage("可用配置项: stream, simple, websearch, memory, mail");
            return;
        }

        var key = args.Parameters[1].ToLower();
        var val = args.Parameters[2];

        switch (key)
        {
            case "stream":
                HandleToggle(plr, val, "流式传输", v => PluginState.Config.EnableStream = v,
                    true, "流式模式: AI逐字返回，体验更好", "非流式模式: 等待完整返回，兼容性更好");
                break;
            case "simple":
                HandleToggle(plr, val, "极简模式", v => PluginState.Config.SimpleMode = v,
                    true, "极简模式: 只显示AI对话输出", "完整模式: 显示所有辅助信息");
                break;
            case "websearch":
                var wsVal = val.ToLower();
                if (wsVal is "yes" or "no")
                {
                    var wsEnabled = wsVal == "yes";
                    PluginState.Config.EnableWebSearch = wsEnabled;
                    plr.SendSuccessMessage($"[AIAgent] 联网搜索已{(wsEnabled ? "开启" : "关闭")}");
                    if (wsEnabled)
                    {
                        var model = PluginState.Config.Model;
                        if (model.ToLower().Contains("gpt-4") || model.ToLower().Contains("claude"))
                            plr.SendInfoMessage("联网搜索已开启，当前模型支持此功能。");
                        else
                            plr.SendInfoMessage($"联网搜索已开启，但当前模型\"{model}\"可能不支持此功能。");
                    }
                }
                else
                    plr.SendErrorMessage("[AIAgent] 无效值，请使用 yes 或 no。");
                break;
            case "memory":
                HandleToggle(plr, val, "上下文记忆", v => PluginState.Config.EnableContextMemory = v,
                    true, "上下文记忆已开启: AI会记住之前的对话", "上下文记忆已关闭: 每次对话独立");
                break;
            case "mail":
                HandleToggle(plr, val, "邮件联动", v => PluginState.Config.EnableMailFeature = v,
                    true, "邮件联动已开启: AI检测到邮件请求时将自动调用邮件插件", "邮件联动已关闭");
                break;
            default:
                plr.SendErrorMessage("[AIAgent] 未知设置项，可用: stream, simple, websearch, memory, mail");
                return;
        }
        ConfigManager.SaveConfig();
    }

    private static void HandleToggle(TSPlayer plr, string val, string name, Action<bool> setter,
        bool defaultShowInfo, string onDesc, string offDesc)
    {
        var v = val.ToLower();
        if (v is "yes" or "no")
        {
            var enabled = v == "yes";
            setter(enabled);
            plr.SendSuccessMessage($"[AIAgent] {name}已{(enabled ? "开启" : "关闭")}");
            if (defaultShowInfo)
                plr.SendInfoMessage(enabled ? onDesc : offDesc);
        }
        else
            plr.SendErrorMessage("[AIAgent] 无效值，请使用 yes 或 no。");
    }

    public static void HandleModel(TSPlayer plr, CommandArgs args)
    {
        if (args.Parameters.Count < 2)
        {
            plr.SendInfoMessage("[AIAgent] 用法: /aig model list 或 /aig model test [fast]");
            return;
        }

        var action = args.Parameters[1].ToLower();
        if (action == "list")
        {
            plr.SendInfoMessage("[c/00FF00:========== 支持的AI模型 ==========]");
            plr.SendInfoMessage("[c/FFD700:Moonshot Kimi:]");
            plr.SendInfoMessage("  moonshot-v1-8k   - 轻量级对话(8K上下文)");
            plr.SendInfoMessage("  moonshot-v1-32k  - 标准对话(32K上下文)");
            plr.SendInfoMessage("  moonshot-v1-128k - 长文档分析(128K上下文)");
            plr.SendInfoMessage("[c/FFD700:DeepSeek:]");
            plr.SendInfoMessage("  deepseek-chat    - 通用对话(性价比首选)");
            plr.SendInfoMessage("  deepseek-coder   - 代码与逻辑分析");
            plr.SendInfoMessage("  deepseek-reasoner- 深度推理(R1模型)");
            plr.SendInfoMessage("  deepseek-v4-pro  - DeepSeek V4 Pro旗舰");
            plr.SendInfoMessage("  deepseek-v4-flash- DeepSeek V4 Flash轻量");
            plr.SendInfoMessage("[c/FFD700:OpenAI:]");
            plr.SendInfoMessage("  gpt-4o-mini      - 轻量快速");
            plr.SendInfoMessage("  gpt-4o           - 全能旗舰");
            plr.SendInfoMessage("[c/FFD700:其他:]");
            plr.SendInfoMessage("  Claude需中转网关 | Ollama本地部署");
            if (!PluginState.Config.SimpleMode)
            {
                plr.SendInfoMessage("[c/808080:注意: API地址需包含/v1路径]");
                plr.SendInfoMessage("[c/808080:  例: https://ai.xem8k5.top/v1]");
            }
            plr.SendInfoMessage("[c/808080:========================================]");
        }
        else if (action == "test")
        {
            var fast = args.Parameters.Count > 2 && args.Parameters[2] == "fast";
            _ = AIHandler.TestModel(plr, fast);
        }
    }

    public static void HandleStats(TSPlayer plr, CommandArgs args)
    {
        if (args.Parameters.Count > 1 && args.Parameters[1].ToLower() == "all")
        {
            if (!plr.HasPermission("aiagent.admin"))
            {
                plr.SendErrorMessage("[AIAgent] 你没有权限查看全局统计，需要 aiagent.admin 权限。");
                return;
            }
            plr.SendInfoMessage("[c/00FF00:========== 全服Token消耗统计 ==========]");
            if (PluginState.TokenStats.IsEmpty)
            {
                plr.SendInfoMessage("暂无数据记录。");
                return;
            }
            foreach (var s in PluginState.TokenStats.Values.OrderByDescending(x => x.TotalTokens))
                plr.SendInfoMessage($"  {s.PlayerName}: {s.TotalTokens:N0} tokens (请求{s.RequestCount}次)");
            var grandTotal = PluginState.TokenStats.Values.Sum(x => x.TotalTokens);
            plr.SendInfoMessage($"[c/FFD700:全服总计: {grandTotal:N0} tokens]");
        }
        else
        {
            if (PluginState.TokenStats.TryGetValue(plr.Name, out var myStats))
            {
                plr.SendInfoMessage("[c/00FF00:========== 你的Token消耗统计 ==========]");
                plr.SendInfoMessage($"  请求次数: {myStats.RequestCount} 次");
                plr.SendInfoMessage($"  输入消耗: {myStats.PromptTokens:N0} tokens");
                plr.SendInfoMessage($"  输出消耗: {myStats.CompletionTokens:N0} tokens");
                plr.SendInfoMessage($"  累计消耗: {myStats.TotalTokens:N0} tokens");
                plr.SendInfoMessage($"  首次使用: {myStats.FirstUsed:yyyy年MM月dd日 HH:mm}");
                plr.SendInfoMessage($"  最后使用: {myStats.LastUsed:yyyy年MM月dd日 HH:mm}");
            }
            else
            {
                plr.SendInfoMessage("[AIAgent] 你还没有使用记录，发送一条消息给AI后开始统计。");
            }
        }
    }

    public static void HandleWhitelist(TSPlayer plr, CommandArgs args)
    {
        if (!plr.HasPermission("aiagent.admin"))
        {
            plr.SendErrorMessage("[AIAgent] 你没有权限管理白名单，需要 aiagent.admin 权限。");
            return;
        }

        if (args.Parameters.Count < 2)
        {
            plr.SendInfoMessage("[AIAgent] 用法: /aig whitelist add/remove/list <玩家>");
            return;
        }

        var action = args.Parameters[1].ToLower();
        if (action == "list")
        {
            plr.SendInfoMessage("[c/00FF00:========== AI使用白名单 ==========]");
            var list = PluginState.Config.AllowedPlayers;
            if (list == null || list.Count == 0)
                plr.SendInfoMessage("白名单为空，当前允许所有玩家使用AI功能。");
            else
            {
                plr.SendInfoMessage($"白名单玩家({list.Count}人):");
                plr.SendInfoMessage("  " + string.Join(", ", list));
            }
        }
        else if (args.Parameters.Count >= 3)
        {
            var target = args.Parameters[2];
            PluginState.Config.AllowedPlayers ??= new System.Collections.Generic.List<string>();

            if (action == "add")
            {
                if (!PluginState.Config.AllowedPlayers.Contains(target))
                {
                    PluginState.Config.AllowedPlayers.Add(target);
                    plr.SendSuccessMessage($"[AIAgent] 已将玩家 \"{target}\" 添加到白名单。");
                }
                else
                    plr.SendInfoMessage($"[AIAgent] 玩家 \"{target}\" 已在白名单中。");
            }
            else if (action == "remove")
            {
                if (PluginState.Config.AllowedPlayers.Remove(target))
                    plr.SendSuccessMessage($"[AIAgent] 已将玩家 \"{target}\" 从白名单移除。");
                else
                    plr.SendInfoMessage($"[AIAgent] 玩家 \"{target}\" 不在白名单中。");
            }
            ConfigManager.SaveConfig();
        }
    }

    public static void HandlePersona(TSPlayer plr, CommandArgs args)
    {
        if (!plr.HasPermission("aiagent.admin"))
        {
            plr.SendErrorMessage("[AIAgent] 你没有权限管理人设，需要 aiagent.admin 权限。");
            return;
        }

        if (args.Parameters.Count < 2)
        {
            plr.SendInfoMessage("[AIAgent] 用法: /aig persona set <人设描述> | strict <1-10>");
            return;
        }

        var action = args.Parameters[1].ToLower();

        if (action == "set" && args.Parameters.Count >= 3)
        {
            var persona = string.Join(" ", args.Parameters.Skip(2));
            PluginState.Config.Persona = persona;
            ConfigManager.SaveConfig();
            plr.SendSuccessMessage("[AIAgent] AI人设已更新。");
            plr.SendInfoMessage($"当前人设: \"{persona}\"");
        }
        else if (action == "strict" && args.Parameters.Count >= 3)
        {
            if (int.TryParse(args.Parameters[2], out var strict) && strict >= 1 && strict <= 10)
            {
                PluginState.Config.PersonaStrictness = strict;
                ConfigManager.SaveConfig();
                var level = strict switch
                {
                    <= 3 => "宽松（AI可在人设基础上自由发挥）",
                    <= 6 => "适中（AI需遵循人设但可灵活应对）",
                    <= 8 => "严格（AI必须严格遵守人设设定）",
                    _ => "极端严格（AI完全按照人设行动，不可偏离）"
                };
                plr.SendSuccessMessage($"[AIAgent] 人设遵守严格度已设置为{strict}/10");
                plr.SendInfoMessage($"严格度说明: {level}");
            }
            else
                plr.SendErrorMessage("[AIAgent] 严格度必须是1到10之间的整数。");
        }
        else
            plr.SendInfoMessage("[AIAgent] 用法: /aig persona set <人设描述> | strict <1-10>");
    }

    public static void HandleKeywords(TSPlayer plr, CommandArgs args)
    {
        if (!plr.HasPermission("aiagent.admin"))
        {
            plr.SendErrorMessage("[AIAgent] 你没有权限管理关键词，需要 aiagent.admin 权限。");
            return;
        }

        if (args.Parameters.Count < 2)
        {
            plr.SendInfoMessage("[AIAgent] 用法: /aig keywords set <关键词内容> | clear");
            plr.SendInfoMessage("当前关键词: " + (string.IsNullOrWhiteSpace(PluginState.Config.AIKeywords) ? "未设置" : PluginState.Config.AIKeywords));
            return;
        }

        var action = args.Parameters[1].ToLower();

        if (action == "set" && args.Parameters.Count >= 3)
        {
            var keywords = string.Join(" ", args.Parameters.Skip(2));
            PluginState.Config.AIKeywords = keywords;
            ConfigManager.SaveConfig();
            plr.SendSuccessMessage("[AIAgent] AI关键词提示词已更新。");
            plr.SendInfoMessage($"当前关键词: \"{keywords}\"");
        }
        else if (action == "clear")
        {
            PluginState.Config.AIKeywords = "";
            ConfigManager.SaveConfig();
            plr.SendSuccessMessage("[AIAgent] AI关键词提示词已清空。");
        }
        else
            plr.SendInfoMessage("[AIAgent] 用法: /aig keywords set <关键词内容> | clear");
    }
}