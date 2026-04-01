using System.Net.Sockets;
using System.Text;

namespace SocketClient.Core;

public class AsyncBaseClient : IDisposable
{
    private readonly string _ip;
    private readonly int _port;
    public string RxTerminator { get; set; } = "\n";
    public string TxTerminator { get; set; } = "\n";

    private TcpClient? _client;
    private NetworkStream? _stream;

    private readonly StringBuilder _buffer = new StringBuilder();
    private readonly byte[] _readBuffer = new byte[4096];

    private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim _readLock = new SemaphoreSlim(1, 1);

    /// <summary>
    /// 实例化底层客户端
    /// </summary>
    /// <param name="endpoint">一体化地址，格式如 "127.0.0.1:12303"</param>
    public AsyncBaseClient(string endpoint)
    {
        var parts = endpoint.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int port))
        {
            throw new ArgumentException("Endpoint 格式错误，请使用 'IP:Port' 格式，例如 '127.0.0.1:12303'");
        }
        _ip = parts[0];
        _port = port;
    }

    public async Task ConnectAsync()
    {
        _client = new TcpClient();
        await _client.ConnectAsync(_ip, _port);
        _stream = _client.GetStream();
    }

    public async Task SendAsync(string command)
    {
        if (_stream == null) throw new InvalidOperationException("尚未连接到服务器。");

        string payload = command + TxTerminator;
        byte[] data = Encoding.ASCII.GetBytes(payload);

        await _writeLock.WaitAsync();
        try
        {
            await _stream.WriteAsync(data, 0, data.Length);
            await _stream.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 读取一帧完整数据。
    /// 简单终止符（\n \r \r\n \t）：返回值不含终止符，即 "data\n" → "data"
    /// 字符串终止符（如 "END"）：返回值含终止符，即 "data\nEND\n" → "data\nEND"
    ///                           终止符后紧跟的换行会被静默吞掉，有没有都行
    /// </summary>
    /// <param name="terminator">动态终止符，不传则使用全局 RxTerminator</param>
    public async Task<string> ReceiveNextAsync(string? terminator = null)
    {
        if (_stream == null) throw new InvalidOperationException("尚未连接到服务器。");

        await _readLock.WaitAsync();
        try
        {
            while (true)
            {
                string currentTerminator = terminator ?? RxTerminator;

                // 防御：空终止符直接返回原始数据，防止 IndexOf("") 死循环
                if (string.IsNullOrEmpty(currentTerminator))
                {
                    if (_buffer.Length > 0)
                    {
                        string msg = _buffer.ToString();
                        _buffer.Clear();
                        return msg;
                    }
                    int rawBytes = await _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length);
                    if (rawBytes == 0) throw new Exception("远程主机已关闭连接。");
                    return Encoding.ASCII.GetString(_readBuffer, 0, rawBytes);
                }

                // 1. 先查缓存里有没有完整的一帧
                string bufStr = _buffer.ToString();
                int termIndex = bufStr.IndexOf(currentTerminator);
                if (termIndex >= 0)
                {
                    bool isSimple = currentTerminator == "\n"
                                 || currentTerminator == "\r"
                                 || currentTerminator == "\r\n"
                                 || currentTerminator == "\t";

                    string result;
                    int removeLength = termIndex + currentTerminator.Length;

                    if (isSimple)
                    {
                        // "data\n" → 返回 "data"，不含终止符
                        result = bufStr.Substring(0, termIndex);
                    }
                    else
                    {
                        // "data\nEND" → 返回 "data\nEND"，含终止符
                        result = bufStr.Substring(0, termIndex + currentTerminator.Length);

                        // ★ 贪婪吞掉终止符后紧跟的换行：有就吃，没有不报错
                        // \r\n 优先判断，防止只吃半个
                        if (removeLength + 1 < _buffer.Length
                            && _buffer[removeLength] == '\r'
                            && _buffer[removeLength + 1] == '\n')
                        {
                            removeLength += 2;
                        }
                        else if (removeLength < _buffer.Length
                                 && (_buffer[removeLength] == '\n' || _buffer[removeLength] == '\r'))
                        {
                            removeLength += 1;
                        }
                    }

                    _buffer.Remove(0, removeLength);
                    return result;
                }

                // 2. 缓存里还没有完整帧，继续从流里读
                int bytesRead = await _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length);
                if (bytesRead == 0) throw new Exception("远程主机已关闭连接。");
                _buffer.Append(Encoding.ASCII.GetString(_readBuffer, 0, bytesRead));
            }
        }
        finally
        {
            _readLock.Release();
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _writeLock?.Dispose();
        _readLock?.Dispose();
    }
}