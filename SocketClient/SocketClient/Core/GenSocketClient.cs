using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace SocketClient.Core;

/// <summary>
/// 通用字符串 TCP 客户端
/// 只负责连接、发送、接收字符串，不处理任何业务逻辑
/// </summary>
public sealed class GenSocketClient : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource _cts = new();

    private readonly StringBuilder _buffer = new();
    private readonly object _bufferLock = new();
    private readonly SemaphoreSlim _dataSignal = new(0);

    private readonly Encoding _encoding;
    private readonly string _terminator;

    public bool IsConnected => _client?.Connected == true;

    /// <summary>连接断开时触发</summary>
    public event Action? Disconnected;

    /// <param name="terminator">结束符，如 "\r\n"，发送时自动拼接，接收时自动识别</param>
    /// <param name="encoding">字符编码，默认 UTF-8</param>
    public GenSocketClient(string terminator = "\r\n", Encoding? encoding = null)
    {
        _terminator = terminator;
        _encoding = encoding ?? Encoding.UTF8;
    }

    // -------------------------------------------------------------------------
    // 连接管理
    // -------------------------------------------------------------------------

    public async Task ConnectAsync(string host, int port, int timeoutMs = 5000)
    {
        Disconnect();

        _cts = new CancellationTokenSource();
        _client = new TcpClient();

        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        try
        {
            await _client.ConnectAsync(host, port, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"连接 {host}:{port} 超时 ({timeoutMs}ms)");
        }

        _stream = _client.GetStream();

        _ = ReceiveLoopAsync(_cts.Token);
    }

    public void Disconnect()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();

        try { _stream?.Dispose(); } catch { }
        try { _client?.Close(); } catch { }

        _client = null;
        _stream = null;

        Disconnected?.Invoke();
    }

    /// <summary>
    /// 强制清空当前接收缓冲区，防止上次请求的残留数据干扰后续请求
    /// </summary>
    public void ClearBuffer()
    {
        lock (_bufferLock)
        {
            _buffer.Clear();
        }
    }

    // -------------------------------------------------------------------------
    // 发送
    // -------------------------------------------------------------------------

    /// <summary>
    /// 发送字符串，自动拼接结束符
    /// </summary>
    /// <exception cref="InvalidOperationException">未连接时抛出</exception>
    public async Task SendAsync(string data)
    {
        if (_stream is null)
            throw new InvalidOperationException("未连接，无法发送数据");

        var bytes = _encoding.GetBytes(data + _terminator);
        await _stream.WriteAsync(bytes);
        await _stream.FlushAsync();
    }

    // -------------------------------------------------------------------------
    // 接收
    // -------------------------------------------------------------------------

    /// <summary>
    /// 等待收到完整响应（以结束符结尾），返回去掉结束符的内容
    /// 收到完整响应后自动清空缓冲区
    /// </summary>
    /// <param name="timeoutMs">超时毫秒</param>
    /// <param name="ct">外部取消令牌</param>
    /// <returns>去掉结束符的响应字符串</returns>
    /// <exception cref="TimeoutException">超时未收到完整响应</exception>
    public async Task<string> WaitAsync(int timeoutMs, CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeoutMs);

        try
        {
            while (true)
            {
                lock (_bufferLock)
                {
                    var content = _buffer.ToString();
                    var idx = content.IndexOf(_terminator, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        var result = content[..idx];
                        _buffer.Clear(); // 收到完整响应，直接清空
                        return result;
                    }
                }

                await _dataSignal.WaitAsync(linkedCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException($"等待响应超时 ({timeoutMs}ms)");
        }
    }

    // -------------------------------------------------------------------------
    // 后台接收循环
    // -------------------------------------------------------------------------

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buf = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                int bytesRead = await _stream.ReadAsync(buf, ct);

                if (bytesRead == 0)
                {
                    Disconnect();
                    break;
                }

                var text = _encoding.GetString(buf, 0, bytesRead);

                lock (_bufferLock)
                    _buffer.Append(text);

                _dataSignal.Release();
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Debug.WriteLine($"[GenSocketClient] 接收异常: {ex.Message}");
            Disconnect();
        }
    }

    // -------------------------------------------------------------------------

    public void Dispose()
    {
        Disconnect();
        _cts.Dispose();
        _dataSignal.Dispose();
    }
}