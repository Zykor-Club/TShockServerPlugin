using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TShockAPI;
using AIAgent.Channels;
using AIAgent.Chat;
using AIAgent.Config;
using AIAgent.Core;
using AIAgent.Llm;
using AIAgent.Memory;
using AIAgent.Persona;

namespace AIAgent.Commands;

/// <summary>
/// /aig 子命令分发。M1 起，不认识的输入一律当聊天内容。
/// </summary>
public static class AigCommandRouter
{
    public const string Version = "2.0.0-alpha";

    public static void Handle(CommandArgs args)
    {
        var player = args.Player;
        if (player is null) return;

        PluginState.Players.Touch(player);

        if (args.Parameters.Count == 0)
        {
            SendHelp(player);
            return;
        }

        var sub = args.Parameters[0].ToLowerInvariant();
        switch (sub)
        {
            case "help":
                SendHelp(player);
                return;
            case "status":
                SendStatus(player);
                return;
            case "memory":
                HandleMemory(player, args);
                return;
            case "admin":
                HandleAdmin(player, args);
                return;
            default:
                // 其余全部当成聊天内容
                _ = ChatService.HandleAsync(player, string.Join(" ", args.Parameters));
                return;
        }
    }

    private static readonly Dictionary<string, string> ToolLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["get_my_status"] = "看你自己的状态",
        ["get_world_info"] = "看时间/天气/在线人数",
        ["get_world_progress"] = "看 Boss 进度",
        ["get_recipe"] = "查合成配方",
        ["find_recipes_for"] = "反查材料用途",
        ["search_wiki"] = "查游戏 Wiki",
        ["heal_player"] = "回血",
        ["give_item"] = "给物品",
        ["teleport_to_player"] = "传送到玩家",
        ["bring_player"] = "把玩家拉过来",
        ["set_time"] = "改时间",
        ["set_weather"] = "改天气",
        ["run_command"] = "执行服务器指令",
        ["list_commands"] = "查可用指令",
        ["avatar_status"] = "看分身状态",
        ["avatar_say"] = "让分身说话",
        ["avatar_follow"] = "让分身跟随",
        ["avatar_goto"] = "让分身走过去",
        ["avatar_attack"] = "让分身打怪",
        ["avatar_scan"] = "扫分身周围",
        ["avatar_guard"] = "让分身守点",
        ["avatar_patrol"] = "让分身巡逻",
        ["avatar_return"] = "让分身回城",
        ["avatar_stop"] = "让分身停下",
    };

    private static void SendHelp(TSPlayer player)
    {
        var cfg = PluginState.Config;
        player.SendInfoMessage($"[c/00FF00:===== {cfg.AIName} · AIAgent v{Version} =====]");
        player.SendInfoMessage("直接在聊天栏说话就行，不用打命令；也可以 /aig <内容> 单独找它。");

        var labels = PluginState.Tools.Registry.For(player)
            .Select(tool => ToolLabels.TryGetValue(tool.Name, out var label) ? label : tool.Name)
            .ToList();

        var summary = labels.Count == 0
            ? "聊天"
            : string.Join("、", labels.Take(8)) + (labels.Count > 8 ? $" 等 {labels.Count} 项" : string.Empty);
        player.SendInfoMessage("[c/FFD700:它现在能帮你:] " + summary);
        player.SendInfoMessage("[c/808080:能看到什么、能做什么，都取决于你所在组的权限。]");

        player.SendInfoMessage("[c/FFD700:命令:]");
        player.SendInfoMessage("  /aig <内容>     - 和它说话");
        player.SendInfoMessage("  /aig status     - 看你的身份、关系度和今日用量");
        player.SendInfoMessage("  /aig memory     - 看它记得你什么");

        if (player.HasPermission("aiagent.admin"))
        {
            player.SendInfoMessage("[c/FF6B6B:管理员:]");
            player.SendInfoMessage("  /aig admin doctor       - 环境自检");
            player.SendInfoMessage("  /aig admin probe        - 测试模型连通性");
            player.SendInfoMessage("  /aig admin usage        - 今日用量");
            player.SendInfoMessage("  /aig admin setkey <key> - 设置密钥（不进日志）");
            player.SendInfoMessage("  /aig admin model <名字> - 切换模型");
            player.SendInfoMessage("  /aig admin flushmem [玩家] - 立即整理记忆");
            player.SendInfoMessage("  /aig admin simulate <文本> - 按聊天消息处理一次");
            player.SendInfoMessage("  /aig admin simulatedeath   - 触发一次死亡回应");
            player.SendInfoMessage("  /aig admin save|reload  - 保存 / 重载配置");
        }
    }

    private static void SendStatus(TSPlayer player)
    {
        var subject = PluginState.Players.Resolve(player);
        if (subject is null)
        {
            player.SendErrorMessage("[AIAgent] 无法识别你的身份。");
            return;
        }

        var cfg = PluginState.Config;
        player.SendInfoMessage($"[AIAgent] 角色名: {subject.DisplayName}");
        player.SendInfoMessage($"[AIAgent] 账号: {(string.IsNullOrWhiteSpace(subject.AccountName) ? "未登录" : subject.AccountName)}");
        player.SendInfoMessage($"[AIAgent] 权限组: {subject.GroupName ?? "未知"}");
        player.SendInfoMessage($"[AIAgent] 记忆键: {subject.Key}");
        player.SendInfoMessage($"[AIAgent] 今日消耗: {PluginState.Usage.TokensToday(subject.Key)} / {cfg.DailyTokenBudget} tokens");

        if (cfg.Memory.Enabled)
        {
            var record = PluginState.Memory.Load(subject.Key);
            var tier = Chat.AffinityTier.Describe(record.Profile.Affinity);
            player.SendInfoMessage($"[AIAgent] 好感度: {record.Profile.Affinity}/100（{tier.Tier}，称呼「{tier.Address}」）");
        }
    }

    private static void HandleAdmin(TSPlayer player, CommandArgs args)
    {
        if (!player.HasPermission("aiagent.admin"))
        {
            player.SendErrorMessage("[AIAgent] 你没有权限执行此操作，需要 aiagent.admin。");
            return;
        }

        var action = args.Parameters.Count > 1 ? args.Parameters[1].ToLowerInvariant() : string.Empty;
        switch (action)
        {
            case "doctor":
                SendDoctor(player);
                break;
            case "save":
                TrySave(player);
                break;
            case "reload":
                TryReload(player);
                break;
            case "setkey":
                TrySetKey(player, args);
                break;
            case "model":
                TrySetModel(player, args);
                break;
            case "probe":
                _ = ProbeAsync(player);
                break;
            case "usage":
                SendUsage(player);
                break;
            case "flushmem":
                _ = FlushMemoryAsync(player, args);
                break;
            case "simulate":
                if (args.Parameters.Count < 3)
                {
                    player.SendInfoMessage("[AIAgent] 用法: /aig admin simulate <文本>");
                    break;
                }
                GameChatChannel.Process(player, string.Join(" ", args.Parameters.Skip(2)), bypassActive: true);
                player.SendSuccessMessage("[AIAgent] 已按聊天消息处理。");
                break;
            case "simulatedeath":
                GameChatChannel.SimulateDeath(player);
                player.SendSuccessMessage("[AIAgent] 已触发一次死亡回应。");
                break;
            default:
                player.SendInfoMessage("[AIAgent] 用法: /aig admin doctor | probe | setkey | model | usage | save | reload");
                break;
        }
    }

    private static void TrySetKey(TSPlayer player, CommandArgs args)
    {
        if (args.Parameters.Count < 3)
        {
            player.SendErrorMessage("[AIAgent] 用法: /aig admin setkey <密钥>");
            return;
        }

        var provider = PluginState.Config.Providers.CurrentProvider;
        if (provider is null)
        {
            player.SendErrorMessage("[AIAgent] 当前供应商配置不存在。");
            return;
        }

        var key = args.Parameters[2].Trim();
        provider.ApiKey = key;

        try
        {
            PluginState.Store.Save(PluginState.Config);
            player.SendSuccessMessage($"[AIAgent] 密钥已更新（{Mask(key)}），当前供应商 {provider.Name}。");
            PluginLog.Info($"密钥已更新 | 供应商 {PluginState.Config.Providers.Current} | {Mask(key)}");
        }
        catch (Exception ex)
        {
            PluginLog.Error("保存密钥失败", ex);
            player.SendErrorMessage("[AIAgent] 保存失败，详情见控制台日志。");
        }
    }

    private static void TrySetModel(TSPlayer player, CommandArgs args)
    {
        if (args.Parameters.Count < 3)
        {
            player.SendErrorMessage("[AIAgent] 用法: /aig admin model <模型名>");
            return;
        }

        var model = args.Parameters[2].Trim();
        PluginState.Config.Model = model;

        try
        {
            PluginState.Store.Save(PluginState.Config);
            player.SendSuccessMessage($"[AIAgent] 模型已切换为 {model}。");
        }
        catch (Exception ex)
        {
            PluginLog.Error("保存模型设置失败", ex);
            player.SendErrorMessage("[AIAgent] 保存失败，详情见控制台日志。");
        }
    }

    private static void SendUsage(TSPlayer player)
    {
        var subject = PluginState.Players.Resolve(player);
        var key = subject?.Key ?? player.Name;
        var cfg = PluginState.Config;
        player.SendInfoMessage($"[AIAgent] 今日: {PluginState.Usage.CallsToday(key)} 次请求，" +
                               $"{PluginState.Usage.TokensToday(key)} / {cfg.DailyTokenBudget} tokens。");
    }

    private static async Task ProbeAsync(TSPlayer player)
    {
        var cfg = PluginState.Config;
        var provider = cfg.Providers.CurrentProvider;
        if (provider is null)
        {
            Reply(player, "当前供应商配置不存在。");
            return;
        }

        Reply(player, $"正在测试 {cfg.Model} ...");
        var watch = Stopwatch.StartNew();

        try
        {
            var client = new OpenAiCompatClient(provider.BaseUrl, provider.ApiKey);
            var options = new ChatOptions(
                provider.BaseUrl,
                provider.ApiKey,
                cfg.Model,
                new[] { ChatMessage.User("在吗，一句话回我就行") },
                Temperature: 0.3,
                MaxTokens: 32,
                Stream: false,
                MaxTokensParam: provider.MaxTokensParam,
                SupportsTemperature: provider.SupportsTemperature,
                SupportsStreamUsage: false);

            var result = await client.SendAsync(options, null, CancellationToken.None).ConfigureAwait(false);
            watch.Stop();

            var text = OutputPostProcessor.Clean(result.Text, 60);
            Reply(player, $"{cfg.Model} 通了，{watch.ElapsedMilliseconds}ms，回了「{text}」，用量 {result.Usage.TotalTokens} tokens。");
            PluginLog.Info($"探测成功 | {cfg.Model} | {watch.ElapsedMilliseconds}ms | tokens {result.Usage.TotalTokens}");
        }
        catch (LlmException ex)
        {
            watch.Stop();
            PluginLog.Error("探测失败: " + ex.Message, ex);
            Reply(player, $"没通：{ex.UserMessage}（{watch.ElapsedMilliseconds}ms，详情见控制台）");
        }
        catch (Exception ex)
        {
            watch.Stop();
            PluginLog.Error("探测异常", ex);
            Reply(player, "没通，详情见控制台日志。");
        }
    }

    private static void SendDoctor(TSPlayer player)
    {
        var cfg = PluginState.Config;

        player.SendInfoMessage("[c/00FF00:===== AIAgent 自检 =====]");
        player.SendInfoMessage($"版本: {Version}");
        player.SendInfoMessage($"数据目录: {PluginState.DataDirectory}");
        player.SendInfoMessage($"配置文件: {PluginState.ConfigPath}");

        var exists = File.Exists(PluginState.ConfigPath);
        player.SendInfoMessage($"配置存在: {(exists ? "是" : "否")}");
        if (exists)
        {
            player.SendInfoMessage(PluginState.Store.TryValidate(out var error)
                ? "[c/00FF00:配置解析:] 通过"
                : $"[c/FF0000:配置解析:] 失败 - {error}");
        }

        player.SendInfoMessage($"目录可写: {(CanWrite(PluginState.DataDirectory) ? "是" : "否")}");
        player.SendInfoMessage($"供应商: {cfg.Providers.List.Count} 个，当前 {cfg.Providers.Current}");
        var provider = cfg.Providers.CurrentProvider;
        if (provider is not null)
        {
            player.SendInfoMessage($"  {provider.Name} · {provider.BaseUrl}");
            player.SendInfoMessage($"  密钥: {(string.IsNullOrWhiteSpace(provider.ApiKey) ? "[c/FF0000:未配置]" : "已配置 " + Mask(provider.ApiKey))}");
        }
        player.SendInfoMessage($"模型: {cfg.Model}（温度 {cfg.Temperature:0.0}）");
        player.SendInfoMessage($"记忆开关: {(cfg.Memory.Enabled ? "开" : "关")} | 记忆预算 {cfg.Memory.MemoryBudgetTokens} tokens");
        player.SendInfoMessage($"主动问候: {(cfg.Greeting.Enabled ? "开" : "关")} | 最小间隔 {cfg.Greeting.MinIntervalHours} 小时");
        player.SendInfoMessage($"主线程队列: 深度 {PluginState.MainThread.Depth} | 累计投递 {PluginState.MainThread.Posted} | 已执行 {PluginState.MainThread.Pumped}");
        player.SendInfoMessage($"在线登记: {PluginState.Players.Count} 人");

        foreach (var line in PluginState.Players.Snapshot().Take(10))
        {
            player.SendInfoMessage($"  {line}");
        }

        if (string.IsNullOrWhiteSpace(provider?.ApiKey))
        {
            player.SendWarningMessage("[AIAgent] 提醒：还没有填 API 密钥，先 /aig admin setkey <key> 或改配置文件。");
        }
    }

    private static void TrySave(TSPlayer player)
    {
        try
        {
            PluginState.Store.Save(PluginState.Config);
            player.SendSuccessMessage("[AIAgent] 配置已保存。");
        }
        catch (Exception ex)
        {
            PluginLog.Error("保存配置失败", ex);
            player.SendErrorMessage("[AIAgent] 保存失败，详情见控制台日志。");
        }
    }

    private static void TryReload(TSPlayer player)
    {
        try
        {
            PluginState.Config = PluginState.Store.Load();
            player.SendSuccessMessage($"[AIAgent] 配置已重新加载（模型: {PluginState.Config.Model}）。");
            PluginLog.Info($"配置已重新加载 | 供应商 {PluginState.Config.Providers.List.Count} 个 | 模型 {PluginState.Config.Model}");
        }
        catch (Exception ex)
        {
            PluginLog.Error("重新加载配置失败", ex);
            player.SendErrorMessage("[AIAgent] 重新加载失败，详情见控制台日志。");
        }
    }



    private static void HandleMemory(TSPlayer player, CommandArgs args)
    {
        var cfg = PluginState.Config;
        if (!cfg.Memory.Enabled)
        {
            player.SendInfoMessage("[AIAgent] 记忆功能被服主关闭了。");
            return;
        }

        var subject = PluginState.Players.Resolve(player);
        if (subject is null)
        {
            player.SendErrorMessage("[AIAgent] 无法识别你的身份。");
            return;
        }

        var key = subject.Key;
        var action = args.Parameters.Count > 1 ? args.Parameters[1].ToLowerInvariant() : string.Empty;

        if (action == "forget")
        {
            if (!cfg.Memory.PlayerCanDelete)
            {
                player.SendErrorMessage("[AIAgent] 服主关闭了「玩家可删除记忆」。");
                return;
            }

            PluginState.Memory.Delete(key);
            PluginState.Sessions.Clear(key);
            player.SendSuccessMessage("[AIAgent] 已经把我对你的记忆删干净了。");
            return;
        }

        if (action == "correct")
        {
            if (args.Parameters.Count < 3)
            {
                player.SendInfoMessage("[AIAgent] 用法: /aig memory correct <要更正的内容>");
                return;
            }

            var text = string.Join(" ", args.Parameters.Skip(2));
            var record = PluginState.Memory.Load(key);
            record.Episodes.Add(new MemoryEpisode
            {
                Text = "（玩家纠正）" + text,
                Tags = new List<string> { "纠正" },
                Importance = 10,
                Created = DateTime.Now,
            });
            PluginState.Memory.Save(record);
            player.SendSuccessMessage("[AIAgent] 记下了。");
            return;
        }

        var current = PluginState.Memory.Load(key);
        player.SendInfoMessage("[c/00FF00:===== 它记得的你 =====]");

        var profile = new List<string>();
        if (!string.IsNullOrWhiteSpace(current.Profile.Name)) profile.Add("称呼 " + current.Profile.Name);
        if (!string.IsNullOrWhiteSpace(current.Profile.Identity)) profile.Add("身份 " + current.Profile.Identity);
        if (current.Profile.Preferences.Count > 0) profile.Add("偏好 " + string.Join("、", current.Profile.Preferences));
        if (!string.IsNullOrWhiteSpace(current.Profile.Habits)) profile.Add("习惯 " + current.Profile.Habits);
        player.SendInfoMessage(profile.Count > 0 ? "档案: " + string.Join("；", profile) : "档案: 还没有");

        var last = current.Sessions.OrderByDescending(s => s.End).FirstOrDefault();
        player.SendInfoMessage(last is null ? "上次聊天: 无" : $"上次聊天: {last.Summary}");

        foreach (var thread in current.Threads.Where(x => x.IsOpen).Take(3))
        {
            player.SendInfoMessage("还没做完: " + thread.Text);
        }

        player.SendInfoMessage($"共 {current.Episodes.Count} 条记忆、{current.Sessions.Count} 次会话。");
        player.SendInfoMessage("纠正我: /aig memory correct <...> | 忘掉我: /aig memory forget");
    }

    private static async Task FlushMemoryAsync(TSPlayer player, CommandArgs args)
    {
        var subject = PluginState.Players.Resolve(player);
        var key = args.Parameters.Count > 2 ? args.Parameters[2] : subject?.Key ?? player.Name;
        var ok = await MemoryService.FinalizeAsync(key, subject?.DisplayName).ConfigureAwait(false);
        Reply(player, ok ? $"记忆已整理（{key}）。" : "这段对话太短，或者没有可记的东西。");
    }

    private static string Mask(string key)
    {
        if (string.IsNullOrEmpty(key)) return "****";
        if (key.Length <= 8) return "****";
        return key.Substring(0, 3) + "****" + key.Substring(key.Length - 4);
    }

    private static void Reply(TSPlayer player, string text)
    {
        PluginState.MainThread.Post(() =>
        {
            try
            {
                if (player.Active) player.SendInfoMessage($"[c/FF69B4:[{PluginState.Config.AIName}]] {text}");
            }
            catch
            {
                // 玩家已离线
            }
        });
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
