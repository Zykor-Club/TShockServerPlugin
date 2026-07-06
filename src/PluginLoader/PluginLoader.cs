using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using System.Threading.Tasks;

namespace PluginLoader;

[ApiVersion(2, 1)]
public class PluginLoader : TerrariaPlugin
{
    public override string Name => "PluginLoader";
    public override string Author => "淦,星梦";
    public override string Description => "热加载插件管理";
    public override Version Version => new(2026, 7, 7, 0);

    internal static PluginManager Manager { get; private set; } = null!;
    internal static AutoUpdater Updater { get; private set; } = null!;

    public PluginLoader(Main game) : base(game)
    {
        Manager = new PluginManager(game);
        Updater = new AutoUpdater();
    }

    public override void Initialize()
    {
        Commands.ChatCommands.Add(new Command("pluginloader.manage", Manager.HandleCommand, "plugin", "pl")
        {
            HelpText = "动态插件管理: /pl 查看命令列表"
        });
        Commands.ChatCommands.Add(new Command("pluginloader.update", HandleUpdateCommand, "plupdate")
        {
            HelpText = "从仓库检查并更新插件: /plupdate"
        });

        Manager.LoadAll();
        Manager.StartWatching();

        // 启动时后台检查更新
        Task.Run(async () =>
        {
            await Task.Delay(5000);
            await CheckForUpdatesAsync();
        });
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var updates = await Updater.CheckForUpdatesAsync();
            if (updates.Count > 0)
            {
                TShock.Log.ConsoleInfo($"[PluginLoader] 发现 {updates.Count} 个插件更新。使用 /plupdate 查看并安装。");
                TSPlayer.All.SendInfoMessage($"[PluginLoader] 发现 {updates.Count} 个插件可更新。");
            }
        }
        catch { }
    }

    private static void HandleUpdateCommand(CommandArgs args)
    {
        Task.Run(async () =>
        {
            var player = args.Player ?? TSPlayer.Server;
            player.SendInfoMessage("[PluginLoader] 正在检查插件更新...");

            var updates = await Updater.CheckForUpdatesAsync();
            if (updates.Count == 0)
            {
                player.SendSuccessMessage("[PluginLoader] 所有插件已是最新版本！");
                return;
            }

            player.SendInfoMessage($"[PluginLoader] 发现 {updates.Count} 个更新:");
            foreach (var u in updates)
            {
                var status = u.IsNewInstall ? "[NEW]" : "[UPDATE]";
                player.SendInfoMessage($"  {status} {u.AssemblyName}: {u.LocalVersion} -> {u.RemoteVersion}");
            }

            player.SendInfoMessage("[PluginLoader] 正在下载并安装更新...");
            var progress = new Progress<string>(msg => player.SendInfoMessage($"[PluginLoader] {msg}"));
            var result = await Updater.DownloadAndInstallUpdatesAsync(progress);

            if (result)
                player.SendSuccessMessage("[PluginLoader] 更新完成！使用 /pl ra 重载插件或重启服务器。");
            else
                player.SendErrorMessage("[PluginLoader] 更新失败，请检查日志。");
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Manager.StopWatching();
            Manager.UnloadAll();
            Updater.Dispose();
        }
        base.Dispose(disposing);
    }
}
