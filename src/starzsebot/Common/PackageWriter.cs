using ZSEBot.Enums;
using ZSEBot.Models;
using System.Net.WebSockets;
using System.Text;
using TShockAPI;

namespace ZSEBot.Common;

[Serializable]
public class PackageWriter(PackageType packageType, bool isRequest, string? requestId)
{
    private static bool Debug => StarZSEBot.DebugMode;
    public Package Package = new (Direction.ToBot, packageType, isRequest, requestId);

    public PackageWriter Write(string key, object value)
    {
        this.Package.Payload.Add(key, value);
        return this;
    }

    // ClientWebSocket 同一时刻只允许一个 SendAsync 在途，并发发送会抛异常并静默丢包；
    // 用信号量把所有发送（心跳、白名单请求、回包）串行化。
    private static readonly SemaphoreSlim SendLock = new (1, 1);

    public void Send()
    {
        try
        {
            var message = this.Package.ToJson();
            if (Debug)
            {
                TShock.Log.ConsoleInfo($"[starZSEbot]发送BOT数据包：{message}");
            }

            var messageBytes = Encoding.UTF8.GetBytes(message);
            // 后台线程发送，避免在游戏主线程上触碰 WebSocket
            _ = Task.Run(() => SendSerializedAsync(messageBytes));
        }
        catch (Exception e)
        {
            TShock.Log.ConsoleInfo($"[starZSEbot]发送数据包时发生错误：{e}");
        }
    }

    private static async Task SendSerializedAsync(byte[] messageBytes)
    {
        // 取锁加超时：一次发送挂死（对端 TCP 半开等）不得让后续所有发包（心跳/白名单/回包）永久排队
        if (!await SendLock.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            TShock.Log.ConsoleInfo("[starZSEbot]发送数据包超时（等锁超过10秒），已丢弃该数据包");
            return;
        }
        try
        {
            var webSocket = WebsocketManager.WebSocket;
            if (webSocket?.State == WebSocketState.Open)
            {
                await webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true,
                    CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            TShock.Log.ConsoleInfo($"[starZSEbot]发送数据包时发生错误：{e}");
        }
        finally
        {
            SendLock.Release();
        }
    }
}