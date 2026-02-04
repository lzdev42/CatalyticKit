using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace SocketClient.Core;

/// <summary>
/// 高性能通用异步 Socket 客户端
/// 负责 TCP 连接管理、异步收发和缓冲
/// </summary>
public sealed class GenSocketClient : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private volatile bool _isRunning; // 运行标志
    private readonly CancellationTokenSource _cts = new(); // 全局 CTS
    
    // 线程安全的内部数据缓冲区 (Receive Buffer)
    // 生产者: ReceiveLoop
    // 消费者: ReadAsync / WaitAsync
    private readonly ConcurrentQueue<byte> _buffer = new();
    
    // 等待数据的信号量 (Wait 实现机制)
    // 当有新数据到达时，释放信号量，唤醒等待者
    private readonly AsyncAutoResetEvent _dataSignal = new();

    /// <summary>
    /// 当收到数据后触发，用于 PushEvent
    /// </summary>
    public event Action<byte[]>? DataReceived;

    /// <summary>
    /// 当连接断开时触发
    /// </summary>
    public event Action? Disconnected;

    public bool IsConnected => _client?.Connected == true;

    /// <summary>
    /// 连接服务器
    /// </summary>
    public async Task ConnectAsync(string host, int port, int timeoutMs = 5000)
    {
        Disconnect(); // 先断开旧连接

        _client = new TcpClient();
        
        // 使用 Task.WhenAny 实现带超时的连接
        var connectTask = _client.ConnectAsync(host, port);
        var timeoutTask = Task.Delay(timeoutMs);

        if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
        {
            _client.Dispose();
            throw new TimeoutException($"连接 {host}:{port} 超时 ({timeoutMs}ms)");
        }
        
        // 重新抛出可能的异常
        await connectTask;

        _stream = _client.GetStream();
        _isRunning = true;

        // 启动后台接收循环 (Fire & Forget)
        _ = ReceiveLoopAsync(_cts.Token);
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public void Disconnect()
    {
        _isRunning = false;
        try { _stream?.Dispose(); } catch { }
        try { _client?.Close(); } catch { } // dispose tcpclient
        _client = null;
        _stream = null;
        
        // 我们不清除 buffer，允许用户读取残留数据? 
        // 策略: 断线通常意味着 Session 结束，清除 Buffer 比较安全，防止脏读
        // 但如果是在“断网重连”场景，可能希望保留。
        // MVP 策略：为了简单，不主动清除 Buffer，让用户决定。
        
        Disconnected?.Invoke();
    }

    /// <summary>
    /// 发送数据
    /// </summary>
    public async Task SendAsync(byte[] data)
    {
        // 使用局部变量避免并发下的空引用
        var stream = _stream;
        if (stream == null) return; // 静默失败或由上层处理状态

        await stream.WriteAsync(data);
        await stream.FlushAsync();
    }

    /// <summary>
    /// 读取当前缓冲区的所有数据 (非阻塞)
    /// </summary>
    public byte[] ReadAll()
    {
        if (_buffer.IsEmpty) return Array.Empty<byte>();

        // 一次性取出所有数据
        // 注意：ConcurrentQueue 只要没有并发 Dequeue，是相对安全的。
        // 但为了高性能，这里用一个循环取。
        var count = _buffer.Count;
        var result = new byte[count];
        int actual = 0;
        
        while (actual < count && _buffer.TryDequeue(out var b))
        {
            result[actual++] = b;
        }

        // 如果 actual < count (极少见，除非并发)，Resize
        if (actual < count)
        {
            return result[..actual];
        }
        
        return result;
    }

    /// <summary>
    /// [核心] 异步等待数据
    /// 如果缓冲区有数据，立即返回。
    /// 否则挂起等待，直到收到数据或超时。
    /// </summary>
    public async Task<byte[]> WaitAsync(int timeoutMs, CancellationToken ct)
    {
        return await WaitAsync(timeoutMs, null, ct);
    }

    /// <summary>
    /// [核心] 异步等待数据（带结束符）
    /// 如果指定 terminator，等待直到收到该字符序列。
    /// </summary>
    public async Task<byte[]> WaitAsync(int timeoutMs, string? terminator, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeoutMs > 0)
        {
            linkedCts.CancelAfter(timeoutMs);
        }

        try
        {
            if (terminator == null)
            {
                // 无 terminator：收到任意数据即返回
                if (!_buffer.IsEmpty)
                {
                    return ReadAll();
                }
                await _dataSignal.WaitAsync(linkedCts.Token);
                return ReadAll();
            }
            else
            {
                // 有 terminator：等待直到收到结束符
                var terminatorBytes = Encoding.UTF8.GetBytes(terminator);
                var collected = new List<byte>();
                
                while (true)
                {
                    // 检查缓冲区
                    while (_buffer.TryDequeue(out var b))
                    {
                        collected.Add(b);
                        
                        // 检查是否以 terminator 结尾
                        if (collected.Count >= terminatorBytes.Length)
                        {
                            bool match = true;
                            for (int i = 0; i < terminatorBytes.Length; i++)
                            {
                                if (collected[collected.Count - terminatorBytes.Length + i] != terminatorBytes[i])
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match)
                            {
                                return collected.ToArray();
                            }
                        }
                    }
                    
                    // 缓冲区空了但还没收到 terminator，等待更多数据
                    await _dataSignal.WaitAsync(linkedCts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested) 
            {
                throw;
            }
            throw new TimeoutException($"等待数据超时 ({timeoutMs}ms)");
        }
    }

    /// <summary>
    /// 后台接收循环
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            while (_isRunning && _stream != null && !ct.IsCancellationRequested)
            {
                // 异步读取 (Non-blocking I/O)
                // 如果对方断开，ReadAsync 返回 0
                int bytesRead = await _stream.ReadAsync(buffer, ct);
                
                if (bytesRead == 0)
                {
                    // 对端关闭
                    Disconnect();
                    break;
                }

                // 处理接收到的数据
                var chunk = new byte[bytesRead];
                Array.Copy(buffer, chunk, bytesRead);

                // 1. 存入缓冲区
                foreach (var b in chunk) _buffer.Enqueue(b);

                // 2. 唤醒等待者 (Ask-Wait 流程)
                _dataSignal.Set();

                // 3. 触发事件 (Push 流程)
                // 这里的 catch 是为了防止用户回调报错搞挂 Loop
                try 
                { 
                    DataReceived?.Invoke(chunk); 
                } 
                catch (Exception ex) 
                { 
                    Debug.WriteLine($"Error in DataReceived handler: {ex}"); 
                }
            }
        }
        catch (Exception ex)
        {
            // Log error
            Debug.WriteLine($"Socket loop error: {ex.Message}");
            if (_isRunning) Disconnect();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        Disconnect();
        _cts.Dispose();
    }
}

/// <summary>
/// 简单的异步信号量封装，用于单次唤醒
/// </summary>
internal class AsyncAutoResetEvent
{
    private readonly SemaphoreSlim _semaphore = new(0);

    public void Set()
    {
        // 只有当计数为0时才释放，防止积压
        if (_semaphore.CurrentCount == 0)
        {
            try { _semaphore.Release(); } catch { }
        }
    }

    public Task WaitAsync(CancellationToken ct)
    {
        return _semaphore.WaitAsync(ct);
    }
}
