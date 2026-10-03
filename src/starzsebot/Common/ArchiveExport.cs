using System.IO.Compression;
using System.Security.Cryptography;
using Terraria;
using Terraria.IO;
using TShockAPI;
using TShockAPI.DB;

namespace ZSEBot.Common;

// 存档导出：导出全部账号角色 + 重置前世界文件，按「地图文件 / 玩家存档」
// 两个子文件夹打包为 zip 并 gzip+base64 回传。不依赖 AutoResetPlus。
internal static class ArchiveExport
{
    private const int PlayerFileVersion = 279; // Terraria 1.4.5 角色存档版本号

    internal static (string? Name, string? Base64, string? Error) Export()
    {
        var stageDir = "";
        try
        {
            var worldName = string.IsNullOrWhiteSpace(Main.worldName) ? "world" : Main.worldName;
            var safeWorldName = SanitizeFileName(worldName);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var exportRoot = Path.Combine(TShock.SavePath, "starZSEbot", "Exports");
            Directory.CreateDirectory(exportRoot);

            stageDir = Path.Combine(exportRoot, $"{safeWorldName}_{timestamp}");
            if (Directory.Exists(stageDir))
            {
                Directory.Delete(stageDir, true);
            }

            Directory.CreateDirectory(stageDir);
            // 按需求分两个子文件夹：.wld 进「地图文件」，.plr 进「玩家存档」
            var mapDir = Path.Combine(stageDir, "地图文件");
            var playerDir = Path.Combine(stageDir, "玩家存档");
            Directory.CreateDirectory(mapDir);
            Directory.CreateDirectory(playerDir);

            var total = 0;
            var succeed = 0;
            var failed = 0;

            var accounts = new List<UserAccount>();
            using (var reader = TShock.DB.QueryReader("SELECT * FROM tsCharacter"))
            {
                while (reader.Read())
                {
                    var accountId = reader.Get<int>("Account");
                    var account = TShock.UserAccounts.GetUserAccountByID(accountId);
                    if (account != null && !string.IsNullOrWhiteSpace(account.Name))
                    {
                        accounts.Add(account);
                    }
                }
            }

            foreach (var account in accounts)
            {
                total++;
                try
                {
                    var online = TShock.Players.FirstOrDefault(p => p?.Account?.ID == account.ID);
                    var player = online != null ? online.TPlayer : BuildOfflinePlayer(account);
                    if (player == null || string.IsNullOrWhiteSpace(player.name))
                    {
                        failed++;
                        TShock.Log.ConsoleError($"[starZSEbot]导出角色失败(无角色数据): {account.Name}");
                        continue;
                    }

                    var plrPath = Path.Combine(playerDir, SanitizeFileName(player.name) + ".plr");
                    if (WritePlayerFile(player, plrPath))
                    {
                        succeed++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    TShock.Log.ConsoleError($"[starZSEbot]导出角色 {account.Name} 失败: {ex.Message}");
                }
            }

            if (!CopyWorldFile(mapDir, safeWorldName))
            {
                // 地图文件缺失/复制失败时不再假装成功：明确报错，机器人侧会中止重置并提示原因
                return (null, null, "存档导出失败: 无法包含地图文件（世界文件缺失或复制失败，详见服务器日志）");
            }

            var zipPath = Path.Combine(exportRoot, $"{safeWorldName}_{timestamp}.zip");
            ZipFile.CreateFromDirectory(stageDir, zipPath, CompressionLevel.SmallestSize, false);

            TShock.Log.ConsoleInfo($"[starZSEbot]存档导出完成: {Path.GetFileName(zipPath)} (成功 {succeed}/{total}, 失败 {failed})");

            var base64 = Utils.CompressBase64(Utils.FileToBase64String(zipPath));
            return (Path.GetFileName(zipPath), base64, null);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]存档导出失败: {ex}");
            return (null, null, $"存档导出失败: {ex.Message}");
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(stageDir) && Directory.Exists(stageDir))
                {
                    Directory.Delete(stageDir, true);
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    private static Player? BuildOfflinePlayer(UserAccount account)
    {
        var fake = new FakePlayer(account.Name)
        {
            Account = new UserAccount { ID = account.ID }
        };
        var data = TShock.CharacterDB.GetPlayerData(fake, account.ID);
        data.RestoreCharacter(fake);
        return fake.TPlayer;
    }

    private static bool WritePlayerFile(Player player, string path)
    {
        var playerFileData = new PlayerFileData
        {
            Metadata = FileMetadata.FromCurrentSettings(FileType.Player),
            Player = player,
            _isCloudSave = false
        };
        playerFileData.SetPlayTime(TimeSpan.Zero);
        Main.LocalFavoriteData.ClearEntry(playerFileData);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var cryptoStream = new CryptoStream(stream,
            Aes.Create().CreateEncryptor(Player.ENCRYPTION_KEY, Player.ENCRYPTION_KEY), CryptoStreamMode.Write);
        using var binaryWriter = new BinaryWriter(cryptoStream);
        binaryWriter.Write(PlayerFileVersion);
        playerFileData.Metadata.Write(binaryWriter);
        Player.Serialize(playerFileData, player, binaryWriter);
        binaryWriter.Flush();
        cryptoStream.FlushFinalBlock();
        stream.Flush();
        return true;
    }

    private static bool CopyWorldFile(string mapDir, string safeWorldName)
    {
        try
        {
            var worldPath = Main.worldPathName;
            if (string.IsNullOrWhiteSpace(worldPath))
            {
                TShock.Log.ConsoleError("[starZSEbot]导出世界文件失败: 世界路径为空");
                return false;
            }

            if (!Path.IsPathRooted(worldPath))
            {
                // TShock 的 worldPathName 通常已含 world 目录前缀（如 ./world/xxx.wld），
                // 先按进程工作目录直接解析；命中即用，否则回退拼接 WorldPath（兼容只给文件名的格式）
                var directPath = Path.GetFullPath(worldPath);
                worldPath = File.Exists(directPath)
                    ? directPath
                    : Path.Combine(Main.WorldPath, worldPath);
            }

            if (!File.Exists(worldPath))
            {
                TShock.Log.ConsoleError($"[starZSEbot]导出世界文件失败: 未找到 {worldPath}");
                return false;
            }

            File.Copy(worldPath, Path.Combine(mapDir, $"{safeWorldName}.wld"), true);
            return true;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]导出世界文件失败: {ex.Message}");
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrEmpty(result) ? "unknown" : result;
    }

    // 用于离线玩家的假 TSPlayer，构造时即创建 FakePlayer(TPlayer)
    private sealed class FakePlayer(string name) : TSPlayer(name);
}