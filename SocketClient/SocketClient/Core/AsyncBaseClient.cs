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

        // 核心：用来处理半包和粘包的持久化缓存
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly byte[] _readBuffer = new byte[4096];

        // 底层读写锁，防止多并发直接把流写花或读串
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _readLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 实例化底层客户端
        /// </summary>
        /// <param name="endpoint">一体化地址，格式如 "127.0.0.1:12303"</param>
        public AsyncBaseClient(string endpoint)
        {
            // 自动拆分 IP 和 端口
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
            // 使用解析好的 IP 和 Port 进行连接
            await _client.ConnectAsync(_ip, _port);
            _stream = _client.GetStream();
        }

        public async Task SendAsync(string command)
        {
            if (_stream == null) throw new InvalidOperationException("尚未连接到服务器。");

            // 自动拼接发送终止符
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

        // 核心功能：按终止符提取一条完整命令
        public async Task<string> ReceiveNextAsync()
        {
            if (_stream == null) throw new InvalidOperationException("尚未连接到服务器。");

            await _readLock.WaitAsync();
            try
            {
                while (true)
                {
                    // 1. 先检查缓存里有没有完整的一帧数据
                    int termIndex = _buffer.ToString().IndexOf(RxTerminator);
                    if (termIndex >= 0)
                    {
                        // 切割出完整命令（不含终止符）
                        string msg = _buffer.ToString().Substring(0, termIndex);
                        // 从缓存中移除已读取的部分和终止符
                        _buffer.Remove(0, termIndex + RxTerminator.Length);
                        return msg;
                    }

                    // 2. 如果缓存里没有完整命令，就从网络流里读
                    int bytesRead = await _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length);
                    if (bytesRead == 0)
                    {
                        throw new Exception("远程主机已关闭连接。");
                    }

                    // 追加到缓存，继续下一轮 while 循环检查
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