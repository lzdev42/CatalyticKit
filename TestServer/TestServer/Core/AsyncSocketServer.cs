namespace TestServer.Core;

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

public class AsyncSocketServer
{
    private readonly int _port;
    private readonly string _name;
    
    public string ReceiveTerminator { get; set; } = "\n";
    public string SendTerminator { get; set; } = "\n";
    
    // 委托：接收(命令文本, 客户端IP) -> 返回响应
    public Func<string, string, Task<string>> OnCommandReceived;

    public AsyncSocketServer(int port, string name)
    {
        _port = port;
        _name = name;
    }

    public void Start()
    {
        TcpListener listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        Logger.Log(_name, $"Listening on port {_port}", ConsoleColor.DarkGray);

        Task.Run(() => AcceptClientsAsync(listener));
    }

    private async Task AcceptClientsAsync(TcpListener listener)
    {
        while (true)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync();
                string clientId = client.Client.RemoteEndPoint.ToString();
                Logger.Log(_name, $"Connected: {clientId}", ConsoleColor.DarkGreen);
                
                // Fire and forget: 不阻塞，立即去接待下一个连接
                _ = HandleClientAsync(client, clientId); 
            }
            catch (Exception ex)
            {
                Logger.Log(_name, $"Accept error: {ex.Message}", ConsoleColor.Red);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, string clientId)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            byte[] buffer = new byte[2048];
            StringBuilder sb = new StringBuilder();

            try
            {
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));

                    int termIndex;
                    while ((termIndex = sb.ToString().IndexOf(ReceiveTerminator)) >= 0)
                    {
                        string command = sb.ToString().Substring(0, termIndex).Trim('\r', '\n');
                        sb.Remove(0, termIndex + ReceiveTerminator.Length);

                        if (!string.IsNullOrWhiteSpace(command))
                        {
                            // 抛给业务层处理，底层继续读流
                            _ = ProcessAndReplyAsync(command, clientId, stream);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 客户端强制断开或网络异常静默处理
            }
            Logger.Log(_name, $"Disconnected: {clientId}", ConsoleColor.DarkYellow);
        }
    }

    private async Task ProcessAndReplyAsync(string command, string clientId, NetworkStream stream)
    {
        if (OnCommandReceived == null) return;

        Logger.Log(_name, $"RX <- {command}", ConsoleColor.Green);
        
        // 执行业务逻辑
        string response = await OnCommandReceived(command, clientId);

        if (response != null)
        {
            byte[] responseBytes = Encoding.ASCII.GetBytes(response + SendTerminator);
            try
            {
                // 发送时加锁防止并发写流错乱
                lock (stream)
                {
                    stream.Write(responseBytes, 0, responseBytes.Length);
                }
                Logger.Log(_name, $"TX -> {response.Replace("\n", "\\n")}", ConsoleColor.Yellow);
            }
            catch (Exception ex)
            {
                Logger.Log(_name, $"Send error: {ex.Message}", ConsoleColor.Red);
            }
        }
    }
}