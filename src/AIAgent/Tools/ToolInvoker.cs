using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIAgent.Core;
using AIAgent.Llm;
using TShockAPI;

namespace AIAgent.Tools;

public sealed class ToolBatch
{
    public List<ChatMessage> Messages { get; } = new();
}

/// <summary>
/// 工具执行器：校验 → 策略判断 → 主线程执行 → 回填结果 + 审计。
/// </summary>
public sealed class ToolInvoker
{
    private readonly ConcurrentDictionary<string, int> _quota = new();

    public ToolInvoker(ToolRegistry registry) => Registry = registry;

    public ToolRegistry Registry { get; }

    public async Task<ToolBatch> HandleAsync(TSPlayer player, string subjectKey, IReadOnlyList<ToolCall> calls, int reqId)
    {
        var batch = new ToolBatch();

        foreach (var call in calls)
        {
            var tool = Registry.Find(call.Name);
            if (tool is null)
            {
                batch.Messages.Add(ToolMessage(call.Id, "没有这个工具"));
                Audit(player, call.Name, string.Empty, call.Arguments, "unknown_tool", reqId, 0);
                continue;
            }

            JsonElement args;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
                args = doc.RootElement.Clone();
            }
            catch
            {
                batch.Messages.Add(ToolMessage(call.Id, "参数不是合法 JSON"));
                Audit(player, call.Name, string.Empty, call.Arguments, "bad_json", reqId, 0);
                continue;
            }

            var validation = await ValidateOnMainAsync(tool, args, player).ConfigureAwait(false);
            var hasPermission = await HasPermissionAsync(player, tool.Permission).ConfigureAwait(false);

            var decision = ToolPolicy.Evaluate(
                hasPermission: hasPermission,
                requiresRealPlayer: tool.RequiresRealPlayer,
                isRealPlayer: player.RealPlayer,
                quotaExceeded: IsQuotaExceeded(subjectKey, tool.Name),
                validationOk: validation.Ok,
                validationMessage: validation.Reason);

            if (!decision.Allowed)
            {
                batch.Messages.Add(ToolMessage(call.Id, decision.Message));
                Audit(player, call.Name, tool.Describe(args), call.Arguments, decision.Reason.ToString(), reqId, 0);
                continue;
            }

            if (!ConsumeQuota(subjectKey, tool.Name))
            {
                batch.Messages.Add(ToolMessage(call.Id, "今日次数已用完"));
                Audit(player, call.Name, tool.Describe(args), call.Arguments, nameof(ToolDenyReason.QuotaExceeded), reqId, 0);
                continue;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await ExecuteOnMainAsync(tool, args, player).ConfigureAwait(false);
            watch.Stop();

            var businessFail = result.PayloadJson.Contains("\"ok\":false", StringComparison.Ordinal);
            if (!result.Ok || businessFail)
            {
                // 没真正做成，不占配额
                RefundQuota(subjectKey, tool.Name);
            }

            var payload = result.Ok
                ? result.PayloadJson
                : "{\"ok\":false,\"reason\":\"执行失败（超时或异常），可以换个方式再试\"}";
            batch.Messages.Add(ToolMessage(call.Id, payload));
            Audit(player, call.Name, tool.Describe(args), call.Arguments,
                result.Ok && !businessFail ? "ok" : "error", reqId, watch.ElapsedMilliseconds);
        }

        return batch;
    }

    /// <summary>
    /// 在主线程做权限判断：TSPlayer.HasPermission 会触发 PlayerHooks.OnPlayerPermission，
    /// 别的插件的处理器默认按主线程写，别在线程池上叫它们。
    /// </summary>
    private static async Task<ToolValidation> ValidateOnMainAsync(ITool tool, JsonElement args, TSPlayer player)
    {
        try
        {
            return await PluginState.MainThread
                .InvokeAsync(() => tool.Validate(args, player), 2000)
                .ConfigureAwait(false);
        }
        catch
        {
            return tool.Validate(args, player);
        }
    }

    private static async Task<bool> HasPermissionAsync(TSPlayer player, string permission)
    {
        try
        {
            return await PluginState.MainThread
                .InvokeAsync(() => player.HasPermission(permission), 2000)
                .ConfigureAwait(false);
        }
        catch
        {
            return player.HasPermission(permission);
        }
    }

    private bool IsQuotaExceeded(string subjectKey, string tool)
    {
        var cfg = PluginState.Config;
        if (!cfg.Tools.TryGetValue(tool, out var settings) || settings.DailyLimit <= 0) return false;

        var key = $"{subjectKey}|{tool}|{DateTime.Today:yyyyMMdd}";
        return _quota.TryGetValue(key, out var used) && used >= settings.DailyLimit;
    }

    private void RefundQuota(string subjectKey, string tool)
    {
        var cfg = PluginState.Config;
        if (!cfg.Tools.TryGetValue(tool, out var settings) || settings.DailyLimit <= 0) return;

        var key = $"{subjectKey}|{tool}|{DateTime.Today:yyyyMMdd}";
        _quota.AddOrUpdate(key, 0, (_, current) => Math.Max(0, current - 1));
    }

    private bool ConsumeQuota(string subjectKey, string tool)
    {
        var cfg = PluginState.Config;
        if (!cfg.Tools.TryGetValue(tool, out var settings) || settings.DailyLimit <= 0) return true;

        var key = $"{subjectKey}|{tool}|{DateTime.Today:yyyyMMdd}";
        var used = _quota.AddOrUpdate(key, 1, (_, current) => current + 1);
        return used <= settings.DailyLimit;
    }

    private static async Task<ToolResult> ExecuteOnMainAsync(ITool tool, JsonElement args, TSPlayer player)
    {
        try
        {
            return await PluginState.MainThread
                .InvokeAsync(() => tool.Execute(args, player), 5000)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"工具执行失败: {tool.Name}", ex);
            return new ToolResult(false, "{}");
        }
    }

    private static ChatMessage ToolMessage(string callId, string content) => new()
    {
        Role = "tool",
        ToolCallId = callId,
        Content = content,
    };

    private static void Audit(TSPlayer player, string tool, string desc, string argsJson, string result, int reqId, long ms)
    {
        try
        {
            var dir = Path.Combine(PluginState.DataDirectory, "audit");
            Directory.CreateDirectory(dir);

            var record = JsonSerializer.Serialize(new
            {
                ts = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                reqId,
                account = player.Account?.Name ?? string.Empty,
                name = player.Name,
                tool,
                desc,
                args = argsJson,
                result,
                durationMs = ms,
            });

            var file = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM") + ".jsonl");
            File.AppendAllText(file, record + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            PluginLog.Error("审计日志写入失败", ex);
        }
    }
}
