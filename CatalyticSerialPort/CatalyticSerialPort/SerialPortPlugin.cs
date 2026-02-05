using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using CatalyticKit;

namespace CatalyticSerialPort;

/// <summary>
/// 插件业务异常，用于替代系统级异常
/// </summary>
public sealed class SerialPluginException : Exception
{
    public SerialPluginException(string message) : base(message) { }
    public SerialPluginException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// 串口连接包装器，封装 SerialPort + 状态 + 事件处理器引用
/// </summary>
internal sealed class PortWrapper : IDisposable
{
    public SerialPort Port { get; }
    public string PortName { get; }
    public volatile bool IsClosing;
    private SerialDataReceivedEventHandler? _dataReceivedHandler;

    public PortWrapper(SerialPort port, string portName)
    {
        Port = port;
        PortName = portName;
    }

    public void SetDataReceivedHandler(SerialDataReceivedEventHandler handler)
    {
        _dataReceivedHandler = handler;
        Port.DataReceived += handler;
    }

    public void Dispose()
    {
        IsClosing = true;

        // 先移除事件处理器，防止回调访问已释放对象
        if (_dataReceivedHandler != null)
        {
            try { Port.DataReceived -= _dataReceivedHandler; } catch { }
            _dataReceivedHandler = null;
        }

        try
        {
            if (Port.IsOpen) Port.Close();
        }
        catch { }

        try
        {
            Port.Dispose();
        }
        catch { }
    }
}

/// <summary>
/// 串口通讯插件
/// 支持多串口独立管理，Event 模式和 Raw 模式读取
/// </summary>
public class SerialPortPlugin : ICommunicator
{
    private IPluginContext? _context;

    // 管理多个串口连接: Key = portName (不含波特率)
    private readonly ConcurrentDictionary<string, PortWrapper> _ports = new();

    // 用于 HandleConnect 的锁，防止同一端口并发连接
    private readonly ConcurrentDictionary<string, object> _connectLocks = new();

    public string Id => "catalytic.serial-port";
    public string Protocol => "serial";

    public Task ActivateAsync(IPluginContext context)
    {
        _context = context;
        _context.Log(LogLevel.Info, "Serial Port Plugin Activated");
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        // 先获取快照，避免迭代时并发修改
        var snapshot = _ports.Values.ToArray();
        _ports.Clear();

        foreach (var wrapper in snapshot)
        {
            try
            {
                wrapper.Dispose();
            }
            catch (Exception ex)
            {
                _context?.Log(LogLevel.Warning, $"Error closing port {wrapper.PortName}: {ex.Message}");
            }
        }

        _context?.Log(LogLevel.Info, "Serial Port Plugin Deactivated");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行通讯动作（简单版本）
    /// </summary>
    public Task<byte[]> ExecuteAsync(
        string address,
        string action,
        byte[] payload,
        int timeoutMs,
        CancellationToken ct)
    {
        return ExecuteAsync(address, action, payload, new ExecuteOptions { TimeoutMs = timeoutMs }, ct);
    }

    /// <summary>
    /// 执行通讯动作（带高级选项）
    /// </summary>
    public async Task<byte[]> ExecuteAsync(
        string address,
        string action,
        byte[] payload,
        ExecuteOptions options,
        CancellationToken ct)
    {
        // 入口校验
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Address cannot be empty");
        }

        try
        {
            if (Enum.TryParse<CommAction>(action, true, out var commAction))
            {
                switch (commAction)
                {
                    case CommAction.Connect:
                        return await HandleConnect(address, options.Terminator);

                    case CommAction.Disconnect:
                        return HandleDisconnect(address);

                    case CommAction.Send:
                        return HandleSend(address, payload);

                    case CommAction.Read:
                        return HandleRead(address);

                    case CommAction.Query:
                        HandleSend(address, payload);
                        return await HandleWait(address, options.TimeoutMs, ct);

                    case CommAction.Status:
                        return HandleStatus(address);
                }
            }

            throw new ArgumentException($"Unsupported action: {action}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SerialPluginException)
        {
            // 已经是插件异常，直接抛出
            throw;
        }
        catch (TimeoutException ex)
        {
            _context?.Log(LogLevel.Warning, $"[{Id}] Action '{action}' on {address} timed out: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _context?.Log(LogLevel.Error, $"[{Id}] Action '{action}' on {address} failed: {ex.Message}");
            throw new SerialPluginException($"串口操作失败: {ex.Message}", ex);
        }
    }

    // --- Handler Methods ---

    /// <summary>
    /// 连接串口并绑定 Event 模式（如果有 Terminator）
    /// </summary>
    private Task<byte[]> HandleConnect(string address, string? terminator)
    {
        var (portName, baudRate) = ParseAddress(address);

        // 获取或创建该端口的锁对象
        var portLock = _connectLocks.GetOrAdd(portName, _ => new object());

        lock (portLock)
        {
            // 检查是否已连接
            if (_ports.TryGetValue(portName, out var existing) && existing.Port.IsOpen)
            {
                _context?.Log(LogLevel.Info, $"[{portName}] Already connected (Terminator unchanged)");
                return Task.FromResult(Array.Empty<byte>());
            }

            try
            {
                var port = new SerialPort(portName, baudRate)
                {
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    ReadTimeout = 5000,
                    WriteTimeout = 5000
                };

                var wrapper = new PortWrapper(port, portName);

                // Event 模式：绑定 DataReceived 事件
                if (!string.IsNullOrEmpty(terminator))
                {
                    port.NewLine = terminator;

                    // 创建事件处理器，捕获 wrapper 引用以检查 IsClosing
                    SerialDataReceivedEventHandler handler = (sender, e) =>
                    {
                        // 检查是否正在关闭
                        if (wrapper.IsClosing) return;

                        try
                        {
                            var sp = (SerialPort)sender!;

                            // 再次检查状态
                            if (!sp.IsOpen || wrapper.IsClosing) return;

                            while (sp.BytesToRead > 0 && !wrapper.IsClosing)
                            {
                                try
                                {
                                    var line = sp.ReadLine();
                                    var data = Encoding.UTF8.GetBytes(line);
                                    _context?.PushEvent($"{PluginEvents.DeviceData}:{portName}", data);
                                }
                                catch (TimeoutException)
                                {
                                    // ReadLine 超时，正常情况
                                    break;
                                }
                                catch (InvalidOperationException)
                                {
                                    // 端口已关闭
                                    break;
                                }
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // 端口已释放，静默忽略
                        }
                        catch (Exception ex)
                        {
                            // 捕获所有其他异常，防止回调崩溃
                            try
                            {
                                _context?.Log(LogLevel.Warning, $"[{portName}] DataReceived error: {ex.Message}");
                            }
                            catch { }
                        }
                    };

                    wrapper.SetDataReceivedHandler(handler);
                }

                port.Open();
                _ports[portName] = wrapper;
                _context?.Log(LogLevel.Info, $"[{portName}] Connected at {baudRate} baud");
                return Task.FromResult(Array.Empty<byte>());
            }
            catch (UnauthorizedAccessException)
            {
                throw new SerialPluginException($"串口 {portName} 被占用或无权限访问");
            }
            catch (Exception ex)
            {
                throw new SerialPluginException($"打开串口 {portName} 失败: {ex.Message}", ex);
            }
        }
    }

    private byte[] HandleDisconnect(string address)
    {
        var (portName, _) = ParseAddress(address);

        if (_ports.TryRemove(portName, out var wrapper))
        {
            try
            {
                wrapper.Dispose();
                _context?.Log(LogLevel.Info, $"[{portName}] Disconnected");
            }
            catch (Exception ex)
            {
                _context?.Log(LogLevel.Warning, $"[{portName}] Error during disconnect: {ex.Message}");
            }
        }
        return Array.Empty<byte>();
    }

    private byte[] HandleSend(string address, byte[] payload)
    {
        var (portName, _) = ParseAddress(address);
        var wrapper = GetWrapperOrThrow(portName);

        try
        {
            wrapper.Port.Write(payload, 0, payload.Length);
            _context?.Log(LogLevel.Debug, $"[{portName}] Sent {payload.Length} bytes");
            return Array.Empty<byte>();
        }
        catch (InvalidOperationException ex)
        {
            throw new SerialPluginException($"串口 {portName} 已断开: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new SerialPluginException($"发送数据失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 非阻塞读取当前缓冲区数据
    /// </summary>
    private byte[] HandleRead(string address)
    {
        var (portName, _) = ParseAddress(address);
        var wrapper = GetWrapperOrThrow(portName);

        try
        {
            if (wrapper.Port.BytesToRead == 0)
                return Array.Empty<byte>();

            var data = wrapper.Port.ReadExisting();
            _context?.Log(LogLevel.Debug, $"[{portName}] Read {data.Length} chars");
            return Encoding.UTF8.GetBytes(data);
        }
        catch (InvalidOperationException ex)
        {
            throw new SerialPluginException($"串口 {portName} 已断开: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new SerialPluginException($"读取数据失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Raw 模式：等待指定时间后读取全部数据
    /// </summary>
    private async Task<byte[]> HandleWait(string address, int timeoutMs, CancellationToken ct)
    {
        var (portName, _) = ParseAddress(address);
        var wrapper = GetWrapperOrThrow(portName);

        // 等待指定的超时时间
        await Task.Delay(timeoutMs > 0 ? timeoutMs : 100, ct);

        try
        {
            // 读取全部缓冲区数据
            var data = wrapper.Port.ReadExisting();
            if (!string.IsNullOrEmpty(data))
            {
                _context?.Log(LogLevel.Debug, $"[{portName}] Wait read {data.Length} chars");
            }
            return Encoding.UTF8.GetBytes(data);
        }
        catch (InvalidOperationException ex)
        {
            throw new SerialPluginException($"串口 {portName} 已断开: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new SerialPluginException($"读取数据失败: {ex.Message}", ex);
        }
    }

    private byte[] HandleStatus(string address)
    {
        var (portName, _) = ParseAddress(address);

        if (_ports.TryGetValue(portName, out var wrapper) && wrapper.Port.IsOpen)
        {
            return "1"u8.ToArray();
        }
        return "0"u8.ToArray();
    }

    // --- Helper Methods ---

    private PortWrapper GetWrapperOrThrow(string portName)
    {
        if (_ports.TryGetValue(portName, out var wrapper) && wrapper.Port.IsOpen)
            return wrapper;

        throw new SerialPluginException($"串口 [{portName}] 未连接，请先执行 Connect 动作");
    }

    /// <summary>
    /// 解析地址格式：COMx 或 COMx:BaudRate
    /// </summary>
    private (string portName, int baudRate) ParseAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Address cannot be empty");
        }

        var parts = address.Split(':');
        var portName = parts[0];

        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ArgumentException("Port name cannot be empty");
        }

        var baudRate = 9600; // 默认波特率

        if (parts.Length >= 2 && int.TryParse(parts[1], out var parsedBaud))
        {
            baudRate = parsedBaud;
        }

        return (portName, baudRate);
    }
}
