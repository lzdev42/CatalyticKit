namespace TestServer.Devices;

using System.Net;
using System.Net.Sockets;
using System.Text;
using TestServer.Core;

/// <summary>
/// Mock 运动控制上层 — 模拟机械手控制程序
/// 主动向 Catalytic 发送 start 消息，接收 pass/fail 回复，校验一致性
/// </summary>
public class MotionControlUpper
{
    private readonly bool _batchMode;
    private readonly Random _rand = new();
    private readonly RoundTracker _tracker;
    private readonly MismatchLogger _mismatchLogger;

    private TcpListener _listener = null!;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly object _streamLock = new();

    private int _roundNumber;

    // 接收缓冲区
    private readonly StringBuilder _rxBuffer = new();
    private readonly object _rxLock = new();

    // 超时设置
    private static readonly TimeSpan RoundTimeout = TimeSpan.FromSeconds(120);

    public MotionControlUpper(bool batchMode = false)
    {
        _batchMode = batchMode;
        _mismatchLogger = new MismatchLogger();
        _tracker = new RoundTracker(_mismatchLogger);
    }

    /// <summary>
    /// 启动 TCP Server 并进入轮次循环
    /// </summary>
    public void Start(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Logger.Log("MotionUpper", $"Listening on port {port} (mode: {(_batchMode ? "Independent" : "Batch")})", ConsoleColor.DarkGray);

        // 启动接受连接的循环
        Task.Run(() => AcceptLoopAsync());
        // 启动轮次驱动循环
        Task.Run(() => RunLoopAsync());
    }

    /// <summary>
    /// 接受客户端连接的循环
    /// </summary>
    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                string clientId = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

                lock (_streamLock)
                {
                    // 如果已有连接，先关闭旧连接
                    if (_client != null)
                    {
                        try { _stream?.Close(); } catch { }
                        try { _client.Close(); } catch { }
                    }

                    _client = client;
                    _stream = client.GetStream();
                }

                lock (_rxLock)
                {
                    _rxBuffer.Clear();
                }

                Logger.Log("MotionUpper", $"Connected: {clientId}", ConsoleColor.DarkGreen);

                // 启动接收循环
                _ = ReceiveLoopAsync(client);
            }
            catch (Exception ex)
            {
                Logger.Log("MotionUpper", $"Accept error: {ex.Message}", ConsoleColor.Red);
                await Task.Delay(1000);
            }
        }
    }

    /// <summary>
    /// 从客户端持续接收数据，解析出完整的消息
    /// </summary>
    private async Task ReceiveLoopAsync(TcpClient client)
    {
        byte[] buffer = new byte[2048];
        var stream = client.GetStream();

        try
        {
            while (client.Connected)
            {
                int bytesRead;
                try
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                }
                catch
                {
                    break; // 连接断开
                }

                if (bytesRead == 0) break;

                string data = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                List<string> completeMessages;
                lock (_rxLock)
                {
                    _rxBuffer.Append(data);
                    completeMessages = new List<string>();
                    int termIndex;
                    while ((termIndex = _rxBuffer.ToString().IndexOf('\n')) >= 0)
                    {
                        string msg = _rxBuffer.ToString().Substring(0, termIndex).Trim('\r');
                        _rxBuffer.Remove(0, termIndex + 1);
                        if (!string.IsNullOrWhiteSpace(msg))
                            completeMessages.Add(msg);
                    }
                }

                foreach (var msg in completeMessages)
                {
                    Logger.Log("MotionUpper", $"RX <- {msg}", ConsoleColor.Green);
                    HandleIncomingMessage(msg);
                }
            }
        }
        catch { }
        finally
        {
            Logger.Log("MotionUpper", $"Disconnected: {client.Client.RemoteEndPoint}", ConsoleColor.DarkYellow);
        }
    }

    /// <summary>
    /// 处理从 Catalytic 收到的 pass/fail 消息
    /// </summary>
    private void HandleIncomingMessage(string message)
    {
        var parts = message.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            Logger.Log("MotionUpper", $"Invalid message format: {message}", ConsoleColor.Red);
            return;
        }

        string action = parts[0].ToLower();
        var slots = new HashSet<int>();

        for (int i = 1; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out int slot))
                slots.Add(slot);
            else
                Logger.Log("MotionUpper", $"Invalid slot value: {parts[i]}", ConsoleColor.Red);
        }

        switch (action)
        {
            case "pass":
                _tracker.RecordPass(slots);
                break;
            case "fail":
                _tracker.RecordFail(slots);
                break;
            default:
                Logger.Log("MotionUpper", $"Unknown action: {action}", ConsoleColor.Red);
                break;
        }
    }

    /// <summary>
    /// 主动向 Catalytic 发送消息
    /// </summary>
    private void SendMessage(string message)
    {
        lock (_streamLock)
        {
            if (_stream == null || !_stream.CanWrite)
            {
                Logger.Log("MotionUpper", $"No client connected, skipping: {message}", ConsoleColor.DarkYellow);
                return;
            }

            try
            {
                byte[] bytes = Encoding.ASCII.GetBytes(message + "\n");
                _stream.Write(bytes, 0, bytes.Length);
                Logger.Log("MotionUpper", $"TX -> {message}", ConsoleColor.Yellow);
            }
            catch (Exception ex)
            {
                Logger.Log("MotionUpper", $"Send error: {ex.Message}", ConsoleColor.Red);
            }
        }
    }

    /// <summary>
    /// 轮次驱动主循环
    /// </summary>
    private async Task RunLoopAsync()
    {
        // 等待客户端连接
        Logger.Log("MotionUpper", "Waiting for Catalytic to connect...", ConsoleColor.White);
        while (true)
        {
            lock (_streamLock)
            {
                if (_stream != null && _stream.CanWrite) break;
            }
            await Task.Delay(500);
        }

        Logger.Log("MotionUpper", "Catalytic connected, starting round loop", ConsoleColor.Green);

        while (true)
        {
            _roundNumber++;

            // 随机生成本轮的 slot 集合（1~12个）
            var slots = GenerateRandomSlots();

            if (_batchMode)
            {
                // 独立模式：逐个发送 start 消息
                await RunIndependentRoundAsync(slots);
            }
            else
            {
                // 批量模式：一条消息发送所有 slot
                await RunBatchRoundAsync(slots);
            }

            // 轮次间随机等待 10~60 秒
            int delaySec = _rand.Next(10, 61);
            Logger.Log("MotionUpper", $"Next round in {delaySec}s...", ConsoleColor.DarkGray);
            await Task.Delay(delaySec * 1000);
        }
    }

    /// <summary>
    /// 批量模式：一条 start 消息发送所有 slot
    /// </summary>
    private async Task RunBatchRoundAsync(HashSet<int> slots)
    {
        string msg = $"start,{string.Join(",", slots.OrderBy(s => s))}";
        SendMessage(msg);

        _tracker.StartRound(_roundNumber, slots);

        // 等待轮次完成（带超时）
        bool completed = await _tracker.WaitForRoundCompleteAsync(RoundTimeout);

        if (!completed)
        {
            Logger.Log("MotionUpper", $"Round-{_roundNumber:D3} TIMEOUT — forcing completion", ConsoleColor.Red);
            _tracker.ForceComplete();
        }
        // 批量模式下，无论是否匹配都继续下一轮
    }

    /// <summary>
    /// 独立模式：逐个发送 start 消息，每个 slot 独立一条
    /// </summary>
    private async Task RunIndependentRoundAsync(HashSet<int> slots)
    {
        var slotList = slots.OrderBy(s => s).ToList();

        // 逐个发送，间隔随机 0.5~2 秒
        for (int i = 0; i < slotList.Count; i++)
        {
            SendMessage($"start,{slotList[i]}");

            if (i < slotList.Count - 1)
            {
                int delayMs = _rand.Next(500, 2001);
                await Task.Delay(delayMs);
            }
        }

        _tracker.StartRound(_roundNumber, slots);

        // 独立模式：卡住等待直到对上或超时
        bool completed = await _tracker.WaitForRoundCompleteAsync(RoundTimeout);

        if (!completed)
        {
            Logger.Log("MotionUpper", $"Round-{_roundNumber:D3} TIMEOUT after {RoundTimeout.TotalSeconds}s — forcing completion", ConsoleColor.Red);
            _tracker.ForceComplete();
        }
    }

    /// <summary>
    /// 随机生成 1~12 个不重复的 slot 编号（0-based: 0~11）
    /// </summary>
    private HashSet<int> GenerateRandomSlots()
    {
        int count = _rand.Next(1, 13); // 1~12 个
        var allSlots = Enumerable.Range(0, 12).ToList(); // 0~11

        // Fisher-Yates 洗牌取前 count 个
        for (int i = allSlots.Count - 1; i > 0; i--)
        {
            int j = _rand.Next(i + 1);
            (allSlots[i], allSlots[j]) = (allSlots[j], allSlots[i]);
        }

        return new HashSet<int>(allSlots.Take(count));
    }
}
