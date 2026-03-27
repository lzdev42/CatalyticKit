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
        Service.AddPluginLog(Id, "串口通讯插件 (线程安全版) 已激活");
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

        Service.AddPluginLog(Id, $"[Slot -1] 串口通讯插件已停用");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行通讯动作 (全量原子化受锁保护)
    /// </summary>
    public async Task ExecuteTask(
        int slotIndex,
        string address,
        CommAction action,
        string payload,
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
            switch (action)
            {
                case CommAction.Connect:
                    await HandleConnectInternal(slotIndex, portName, baudRate, options.Terminator);
                    break;

                case CommAction.Disconnect:
                    HandleDisconnectInternal(slotIndex, portName);
                    break;

                case CommAction.Send:
                    var sendPort = GetWrapperOrThrow(slotIndex, portName).Port;
                    sendPort.DiscardInBuffer(); // 补丁：发送前清理残留脏数据
                    sendPort.Write(payload ?? "");
                    Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已发送 {payload?.Length ?? 0} 字符");
                    break;

                case CommAction.Read:
                    var readPort = GetWrapperOrThrow(slotIndex, portName).Port;
                    var data = readPort.ReadExisting();
                    if (!string.IsNullOrEmpty(data))
                    {
                        Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已读取 {data.Length} 字符");
                        _context?.PushEvent(slotIndex, portName, PluginEventType.Result, data);
                    }
                    else
                    {
                        _context?.PushEvent(slotIndex, portName, PluginEventType.Result, "");
                    }
                    break;

                case CommAction.Query:
                    var queryPort = GetWrapperOrThrow(slotIndex, portName).Port;
                    queryPort.DiscardInBuffer(); // 补丁：Query 前物理清空串口缓冲区
                    queryPort.Write(payload ?? "");
                    Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已发送 (Query) {payload?.Length ?? 0} 字符");

                    // 等待指定的超时时间并读取
                    await Task.Delay(options.TimeoutMs > 0 ? options.TimeoutMs : 100, ct);
                    var queryData = queryPort.ReadExisting();
                    if (!string.IsNullOrEmpty(queryData))
                        Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已接收 (Query) {queryData.Length} 字符");
                    
                    _context?.PushEvent(slotIndex, portName, PluginEventType.Result, queryData ?? "");
                    break;

                case CommAction.Status:
                    bool isOpen = _ports.TryGetValue(portName, out var w) && w.Port.IsOpen;
                    _context?.PushEvent(slotIndex, portName, PluginEventType.Result, isOpen ? "1" : "0");
                    break;
            }

            throw new ArgumentException($"不支持的动作类型: {action}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Service.AddPluginLog(Id, $"[Slot {slotIndex}] [Error] 在地址 {address} 执行动作 '{action}' 失败: {ex.Message}");
            throw new SerialPluginException($"串口操作失败: {ex.Message}", ex);
        }
        finally
        {
            semaphore.Release();
        }
    }

    // --- 内部处理方法 ---

    private async Task HandleConnectInternal(int slotIndex, string portName, int baudRate, string? terminator)
    {
        if (_ports.TryGetValue(portName, out var existing) && existing.Port.IsOpen)
        {
            Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 设备已处于连接状态");
            return;
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
                                _context?.PushEvent(slotIndex, portName, PluginEventType.Result, line);
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
            Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已连接，波特率 {baudRate} (线程安全模式)");
            _context?.NotifyConnectionStateChanged(portName, PluginDeviceConnectionState.Connected);
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

    private void HandleDisconnectInternal(int slotIndex, string portName)
    {
        if (_ports.TryRemove(portName, out var wrapper))
        {
            try
            {
                wrapper.Dispose();
                Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已断开连接");
                _context?.NotifyConnectionStateChanged(portName, PluginDeviceConnectionState.Disconnected);
            }
            catch (Exception ex)
            {
                Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 断开连接时发生错误: {ex.Message}");
            }
        }
    }

    private PortWrapper GetWrapperOrThrow(int slotIndex, string portName)
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
