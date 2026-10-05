using ZSEBot.Enums;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using System.Text;
using Terraria;
using TShockAPI;

namespace ZSEBot.Common;

public static class WebsocketManager
{
    public static ClientWebSocket? WebSocket;

    private static string BotServerUrl => Config.Settings.ServerUrl;

    private static string HttpScheme => Config.Settings.UseTls ? "https" : "http";

    private static string WsScheme => Config.Settings.UseTls ? "wss" : "ws";

    internal static bool IsWebsocketConnected => WebSocket?.State == WebSocketState.Open;
    private static bool _isStopWebsocket;

    public static void Init()
    {
        Task.Factory.StartNew(StartZseApi, TaskCreationOptions.LongRunning);
        Task.Factory.StartNew(StartHeartBeat, TaskCreationOptions.LongRunning);
        _isStopWebsocket = false;
    }

    public static void StopWebsocket()
    {
        _isStopWebsocket = true;
        WebSocket?.Dispose();
    }

    private static async Task? StartHeartBeat()
    {
        while (!_isStopWebsocket)
        {
            await Task.Delay(TimeSpan.FromSeconds(60));
            try
            {
                if (WebSocket?.State == WebSocketState.Open)
                {
                    var packetWriter = new PackageWriter(PackageType.Heartbeat, false, null);
                    packetWriter.Send();
                }
            }
            catch
            {
                TShock.Log.ConsoleInfo("[starZSEbot]心跳包发送失败!");
            }
        }
    }

    private static async Task? StartZseApi()
    {
        while (!_isStopWebsocket)
        {
            try
            {
                WebSocket = new ClientWebSocket();
                if (Config.Settings.UseTls)
                {
                    // 用 IP 直连时 .NET 不发 SNI → 绕开机房的域名过白拦截；
                    // 代价是证书域名与 IP 不匹配，所以这里改为固定证书指纹校验（见 ValidateServerCertificate）
                    WebSocket.Options.RemoteCertificateValidationCallback =
                        (sender, certificate, chain, errors) => ValidateServerCertificate(certificate, errors);
                }

                while (string.IsNullOrEmpty(Config.Settings.Token))
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    // 轮询取 token 的 HttpClient 也必须走同一套证书校验：
                    // 自签/指纹模式下默认校验会失败（域名不匹配），否则重新绑定时 HTTPS 轮询会直接报错
                    HttpClientHandler handler = new ();
                    handler.ServerCertificateCustomValidationCallback =
                        (msg, cert, chain, errors) => ValidateServerCertificate(cert, errors);
                    HttpClient client = new (handler);
                    client.Timeout = TimeSpan.FromSeconds(5.0);
                    var response = await client.GetAsync($"{HttpScheme}://{BotServerUrl}/server/token/{StarZSEBot.InitCode}");
                    if (response.StatusCode != HttpStatusCode.OK || Config.Settings.Token != "")
                    {
                        continue;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(responseBody);
                    var token = json["token"]!.ToString();
                    var groupOpenId = json["group_open_id"]!.ToString();
                    Config.Settings.Token = token;
                    Config.Settings.GroupOpenId = groupOpenId;
                    Config.Settings.Write();
                    TShock.Log.ConsoleInfo("[starZSEbot]被动绑定成功!");
                }

                WebSocket.Options.SetRequestHeader("authorization", $"Bearer {Config.Settings.Token}");
                await WebSocket.ConnectAsync(new Uri($"{WsScheme}://{BotServerUrl}/server/ws/{Config.Settings.GroupOpenId}/tshock/"), CancellationToken.None);


                new PackageWriter(PackageType.Hello, false, null)
                    .Write("server_core_version", TShock.VersionNum.ToString())
                    .Write("plugin_version", StarZSEBot.VersionNum)
                    .Write("game_version", Main.versionNumber)
                    .Write("enable_whitelist", Config.Settings.WhiteList)
                    .Write("system", RuntimeInformation.RuntimeIdentifier)
                    .Write("server_name", string.IsNullOrEmpty(TShock.Config.Settings.ServerName) ? Main.worldName : TShock.Config.Settings.ServerName)
                    .Write("settings", new Dictionary<string, object>())
                    .Send();

                TShock.Log.ConsoleInfo("[starZSEbot]Bot连接成功...");

                while (true)
                {
                    var buffer = new byte[1024];
                    using var memoryStream = new MemoryStream();

                    WebSocketReceiveResult result;
                    do
                    {
                        result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        await memoryStream.WriteAsync(buffer.AsMemory(0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // 连接关闭时获取原因
                        await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        var statusCode = (int) result.CloseStatus!;
                        switch (statusCode)
                        {
                            case 4003:
                                Config.Settings.Token = "";
                                Config.Settings.Write();
                                TShock.Log.ConsoleError("[starZSEbot]服务器认证失败, 请重新绑定!");
                                TShock.Log.ConsoleError($"原因({statusCode}): {result.CloseStatusDescription}");
                                StarZSEBot.GenBindCode(null);
                                break;
                            default:
                                TShock.Log.ConsoleError("[starZSEbot]Bot主动断开连接!");
                                TShock.Log.ConsoleError($"原因({statusCode}): {result.CloseStatusDescription}");
                                break;
                        }

                        break;
                    }

                    // 多帧消息必须用累积后的 memoryStream 解码，仅用 buffer/最后一帧会截断 >1024 字节的包
                    var receivedData = Encoding.UTF8.GetString(memoryStream.ToArray());
                    if (StarZSEBot.DebugMode)
                    {
                        TShock.Log.ConsoleInfo($"[starZSEbot]收到BOT数据包: {receivedData}");
                    }

                    _ = Task.Run(() =>
                    {
                        try
                        {
                            ZSEBotApi.HandleMessage(receivedData);
                        }
                        catch (Exception e)
                        {
                            TShock.Log.ConsoleError("[starZSEbot]处理消息时发生错误: \n" +
                                                   $"{e}");
                        }
                       
                    });


                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleInfo("[starZSEbot]Bot断开连接...");
                if (!_isStopWebsocket)
                {
                    TShock.Log.ConsoleError(ex.ToString());
                }
            }

            await Task.Delay(5000);
        }
    }

    /// <summary>
    /// 服务器证书校验。固定指纹模式（默认）下：
    ///   首次连接（指纹为空）→ 记住证书 SHA-256 指纹并放行（TOFU）；
    ///   之后必须与记录完全一致，否则拒绝连接（防中间人）。
    /// 关闭固定指纹时回退到系统默认校验结果（适用于机房已过白、用域名 + Let's Encrypt 证书的场景）。
    /// </summary>
    private static bool ValidateServerCertificate(X509Certificate? certificate, SslPolicyErrors errors)
    {
        if (!Config.Settings.PinCertificate)
        {
            return errors == SslPolicyErrors.None;
        }

        if (certificate is null)
        {
            TShock.Log.ConsoleError("[starZSEbot]TLS 校验失败：对端未提供证书");
            return false;
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
        var pinned = (Config.Settings.CertificateFingerprint ?? "")
            .Replace(":", "").Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(pinned))
        {
            Config.Settings.CertificateFingerprint = fingerprint;
            Config.Settings.Write();
            TShock.Log.ConsoleInfo($"[starZSEbot]首次连接，已固定服务器证书指纹: {fingerprint}");
            return true;
        }

        if (fingerprint == pinned)
        {
            return true;
        }

        TShock.Log.ConsoleError("[starZSEbot]服务器证书指纹不匹配，已拒绝连接！");
        TShock.Log.ConsoleError($"  收到: {fingerprint}");
        TShock.Log.ConsoleError($"  期望: {pinned}");
        TShock.Log.ConsoleError("  若服务器确实换过证书，请清空 starZSEbot.json 里的「证书指纹」后重连。");
        return false;
    }
}