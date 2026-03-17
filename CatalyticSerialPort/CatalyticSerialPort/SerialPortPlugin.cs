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

    // 管理多个串口连接: Key = portName
    private readonly ConcurrentDictionary<string, PortWrapper> _ports = new();

    // 管理端口级并发锁: Key = portName
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _portLocks = new();

    public string Id => "catalytic.serial-port";
    public string Protocol => "serial";

    public Task ActivateAsync(IPluginContext context)
    {
        _context = context;
        _context.Log(LogLevel.Info, "串口通讯插件 (线程安全版) 已激活");
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        var snapshot = _ports.Values.ToArray();
        _ports.Clear();

        foreach (var wrapper in snapshot)
        {
            try { wrapper.Dispose(); } catch { }
        }

        foreach (var semaphore in _portLocks.Values)
        {
            try { semaphore.Dispose(); } catch { }
        }
        _portLocks.Clear();

        _context?.Log(LogLevel.Info, "串口通讯插件已停用");
        return Task.CompletedTask;
    }

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
    /// 执行通讯动作 (全量原子化受锁保护)
    /// </summary>
    public async Task<byte[]> ExecuteAsync(
        string address,
        string action,
        byte[] payload,
        ExecuteOptions options,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("地址不能为空");

        var (portName, baudRate) = ParseAddress(address);

        // 获取端口级信号量
        var semaphore = _portLocks.GetOrAdd(portName, _ => new SemaphoreSlim(1, 1));

        // 等待获取锁 (受 Slot ct 控制)
        await semaphore.WaitAsync(ct);

        try
        {
            if (Enum.TryParse<CommAction>(action, true, out var commAction))
            {
                switch (commAction)
                {
                    case CommAction.Connect:
                        return await HandleConnectInternal(portName, baudRate, options.Terminator);

                    case CommAction.Disconnect:
                        return HandleDisconnectInternal(portName);

                    case CommAction.Send:
                        var sendPort = GetWrapperOrThrow(portName).Port;
                        sendPort.DiscardInBuffer(); // 补丁：发送前清理残留脏数据
                        sendPort.Write(payload, 0, payload.Length);
                        _context?.Log(LogLevel.Debug, $"[{portName}] 已发送 {payload.Length} 字节");
                        return Array.Empty<byte>();

                    case CommAction.Read:
                        var readPort = GetWrapperOrThrow(portName).Port;
                        var data = readPort.ReadExisting();
                        if (!string.IsNullOrEmpty(data))
                            _context?.Log(LogLevel.Debug, $"[{portName}] 已读取 {data.Length} 字符");
                        return Encoding.UTF8.GetBytes(data);

                    case CommAction.Query:
                        var queryPort = GetWrapperOrThrow(portName).Port;
                        queryPort.DiscardInBuffer(); // 补丁：Query 前物理清空串口缓冲区
                        queryPort.Write(payload, 0, payload.Length);
                        _context?.Log(LogLevel.Debug, $"[{portName}] 已发送 (Query) {payload.Length} 字节");

                        // 等待指定的超时时间并读取
                        await Task.Delay(options.TimeoutMs > 0 ? options.TimeoutMs : 100, ct);
                        var queryData = queryPort.ReadExisting();
                        if (!string.IsNullOrEmpty(queryData))
                            _context?.Log(LogLevel.Debug, $"[{portName}] 已接收 (Query) {queryData.Length} 字符");
                        return Encoding.UTF8.GetBytes(queryData);

                    case CommAction.Status:
                        bool isOpen = _ports.TryGetValue(portName, out var w) && w.Port.IsOpen;
                        return isOpen ? "1"u8.ToArray() : "0"u8.ToArray();
                }
            }

            throw new ArgumentException($"不支持的动作类型: {action}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _context?.Log(LogLevel.Error, $"[{Id}] 在地址 {address} 执行动作 '{action}' 失败: {ex.Message}");
            throw new SerialPluginException($"串口操作失败: {ex.Message}", ex);
        }
        finally
        {
            semaphore.Release();
        }
    }

    // --- 内部处理方法 ---

    private Task<byte[]> HandleConnectInternal(string portName, int baudRate, string? terminator)
    {
        if (_ports.TryGetValue(portName, out var existing) && existing.Port.IsOpen)
        {
            _context?.Log(LogLevel.Info, $"[{portName}] 设备已处于连接状态");
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

            if (!string.IsNullOrEmpty(terminator))
            {
                port.NewLine = terminator;
                SerialDataReceivedEventHandler handler = (sender, e) =>
                {
                    if (wrapper.IsClosing) return;
                    try
                    {
                        var sp = (SerialPort)sender!;
                        if (!sp.IsOpen || wrapper.IsClosing) return;

                        while (sp.BytesToRead > 0 && !wrapper.IsClosing)
                        {
                            try
                            {
                                var line = sp.ReadLine();
                                var data = Encoding.UTF8.GetBytes(line);
                                _context?.PushEvent($"{PluginEvents.DeviceData}:{portName}", data);
                            }
                            catch (TimeoutException) { break; }
                            catch (InvalidOperationException) { break; }
                        }
                    }
                    catch { }
                };
                wrapper.SetDataReceivedHandler(handler);
            }

            port.Open();
            _ports[portName] = wrapper;
            _context?.Log(LogLevel.Info, $"[{portName}] 已连接，波特率 {baudRate} (线程安全模式)");
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

    private byte[] HandleDisconnectInternal(string portName)
    {
        if (_ports.TryRemove(portName, out var wrapper))
        {
            try
            {
                wrapper.Dispose();
                _context?.Log(LogLevel.Info, $"[{portName}] 已断开连接");
            }
            catch (Exception ex)
            {
                _context?.Log(LogLevel.Warning, $"[{portName}] 断开连接时发生错误: {ex.Message}");
            }
        }
        return Array.Empty<byte>();
    }

    private PortWrapper GetWrapperOrThrow(string portName)
    {
        if (_ports.TryGetValue(portName, out var wrapper) && wrapper.Port.IsOpen)
            return wrapper;

        throw new SerialPluginException($"串口 [{portName}] 未连接，请先执行 Connect 动作");
    }

    private (string portName, int baudRate) ParseAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("地址不能为空");

        var parts = address.Split(':');
        var portName = parts[0];
        var baudRate = 9600;

        if (parts.Length >= 2 && int.TryParse(parts[1], out var parsedBaud))
            baudRate = parsedBaud;

        return (portName, baudRate);
    }
}
