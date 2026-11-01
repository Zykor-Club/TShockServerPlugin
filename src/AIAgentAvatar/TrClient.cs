extern alias TrAlias;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TrAlias.TrProtocol;

namespace FakePlayer;

public class TrClient
{
    private TcpClient client = null!;
    private BinaryReader? br;
    private BinaryWriter? bw;
    private readonly PacketSerializer mgr = new(true);
    public bool Connected => client?.Connected == true;

    public void Connect(string hostname, int port)
    {
        client = new TcpClient();
        client.Connect(hostname, port);
        br = new BinaryReader(client.GetStream());
        bw = new BinaryWriter(client.GetStream());
    }

    public void Connect(IPEndPoint server, IPEndPoint? proxy = null)
    {
        if (proxy == null)
        {
            client = new TcpClient();
            client.Connect(server);
            br = new BinaryReader(client.GetStream());
            bw = new BinaryWriter(client.GetStream());
            return;
        }

        // 代理模式
        client = new TcpClient();
        client.Connect(proxy);

        // StreamReader/Writer 被释放，但因为 leaveOpen: true，底层网络流保持打开
        var encoding = new UTF8Encoding(false, true);
        using (var sw = new StreamWriter(client.GetStream(), encoding, 4096, true) { NewLine = "\r\n" })
        using (var sr = new StreamReader(client.GetStream(), encoding, false, 4096, true))
        {
            sw.WriteLine($"CONNECT {server} HTTP/1.1");
            sw.WriteLine("User-Agent: Java/1.8.0_192");
            sw.WriteLine($"Host: {server}");
            sw.WriteLine("Accept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2");
            sw.WriteLine("Proxy-Connection: keep-alive");
            sw.WriteLine();
            sw.Flush();

            var resp = sr.ReadLine();
            Console.WriteLine("Proxy connection; " + resp);
            if (resp is null || !resp.StartsWith("HTTP/1.1 200"))
                throw new Exception("Proxy connection failed");

            while (true)
            {
                resp = sr.ReadLine();
                if (string.IsNullOrEmpty(resp))
                    break;
            }
        }

        // 重新创建 BinaryReader/BinaryWriter 使用同一个网络流
        br = new BinaryReader(client.GetStream());
        bw = new BinaryWriter(client.GetStream());
    }

    private bool _closed;

    public void Close()
    {
        if (_closed) return;
        _closed = true;

        try { client?.Close(); } catch { }
        try { client?.Dispose(); } catch { }
        br = null;
        bw = null;
    }

    public void KillServer() => client.GetStream().Write([0, 0], 0, 2);
    internal INetPacket Receive() => mgr.Deserialize(br);
    internal void Send(INetPacket packet)
    {
        if (_closed) return;
        bw?.Write(mgr.Serialize(packet));
    }
}