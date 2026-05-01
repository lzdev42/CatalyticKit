using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using CatalyticKit;

namespace CatalyticSerialPort;

/// <summary>
/// 插件业务异常
/// </summary>
public sealed class SerialPluginException : Exception
{
    public SerialPluginException(string message) : base(message) { }
    public SerialPluginException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// 串口连接包装器
/// </summary>
internal sealed class PortWrapper : IDisposable
{
    public SerialPort Port { get; }
    public string PortName { get; }
    public StringBuilder Buffer { get; } = new();
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
        if (_dataReceivedHandler != null)
        {
            try { Port.DataReceived -= _dataReceivedHandler; } catch { }
            _dataReceivedHandler = null;
        }

        try { if (Port.IsOpen) Port.Close(); } catch { }
        try { Port.Dispose(); } catch { }
    }
}

/// <summary>
/// 串口通讯插件 - 符合 SDK v0.4.5+ 规范
/// </summary>
public class SerialPortPlugin : ICommunicator
{
    private ICommChannel? _channel;
    private readonly ConcurrentDictionary<string, PortWrapper> _ports = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _portLocks = new();

    public string Id => "catalytic.serial-port";
    public string Protocol => "serial";

    public Task ActivateAsync(ICommChannel channel)
    {
        _channel = channel;
        Host.AddPluginLog(Id, "串口通讯插件已激活");
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        foreach (var wrapper in _ports.Values) wrapper.Dispose();
        _ports.Clear();

        foreach (var semaphore in _portLocks.Values) semaphore.Dispose();
        _portLocks.Clear();

        Host.AddPluginLog(Id, "串口通讯插件已停用");
        return Task.CompletedTask;
    }

    public async Task Execute(
        int slotIndex,
        string address,
        CommAction action,
        string payload,
        CommOptions options,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("地址不能为空");

        var (portName, baudRate) = ParseAddress(address);
        var semaphore = _portLocks.GetOrAdd(portName, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            switch (action)
            {
                case CommAction.Connect:
                    await HandleConnectInternal(slotIndex, portName, baudRate, options.ResponseTerminator);
                    break;

                case CommAction.Disconnect:
                    HandleDisconnectInternal(slotIndex, portName);
                    break;

                case CommAction.Send:
                    var sendPort = GetWrapperOrThrow(slotIndex, portName).Port;
                    sendPort.DiscardInBuffer();
                    var payloadToSend = (payload ?? "") + (options.CommandTerminator ?? "");
                    sendPort.Write(payloadToSend);
                    Host.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已发送 {payloadToSend.Length} 字符");
                    break;

                case CommAction.Read:
                    var readPort = GetWrapperOrThrow(slotIndex, portName).Port;
                    var data = readPort.ReadExisting();
                    Host.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已读取 {data?.Length ?? 0} 字符");
                    _channel?.ReportData(slotIndex, portName, data ?? "");
                    break;

                case CommAction.Query:
                    var queryPort = GetWrapperOrThrow(slotIndex, portName).Port;
                    queryPort.DiscardInBuffer();
                    var queryPayload = (payload ?? "") + (options.CommandTerminator ?? "");
                    queryPort.Write(queryPayload);
                    Host.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已发送 (Query) {queryPayload.Length} 字符");

                    await Task.Delay(options.TimeoutMs > 0 ? options.TimeoutMs : 100, ct);
                    var queryData = queryPort.ReadExisting();
                    _channel?.ReportData(slotIndex, portName, queryData ?? "");
                    break;

                case CommAction.Status:
                    bool isOpen = _ports.TryGetValue(portName, out var w) && w.Port.IsOpen;
                    _channel?.ReportData(slotIndex, portName, isOpen ? "1" : "0");
                    break;
                
                default:
                    throw new ArgumentException($"不支持的动作类型: {action}");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Host.AddPluginLog(Id, $"[Slot {slotIndex}] [Error] 在 {address} 执行 '{action}' 失败: {ex.Message}");
            throw new SerialPluginException($"串口操作失败: {ex.Message}", ex);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task HandleConnectInternal(int slotIndex, string portName, int baudRate, string? terminator)
    {
        if (_ports.TryGetValue(portName, out var existing) && existing.Port.IsOpen)
        {
            Host.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已处于连接状态");
            return;
        }
        
        var port = new SerialPort(portName, baudRate) { ReadTimeout = 5000, WriteTimeout = 5000 };
        var wrapper = new PortWrapper(port, portName);

        if (!string.IsNullOrEmpty(terminator))
        {
            wrapper.SetDataReceivedHandler((sender, e) => {
                if (wrapper.IsClosing) return;
                lock (wrapper.Buffer) {
                    string incoming = ((SerialPort)sender!).ReadExisting();
                    if (string.IsNullOrEmpty(incoming)) return;
                    wrapper.Buffer.Append(incoming);
                    while (true) {
                        string snapshot = wrapper.Buffer.ToString();
                        int termIdx = snapshot.IndexOf(terminator);
                        if (termIdx < 0) break;
                        string resultLine = snapshot.Substring(0, termIdx + terminator.Length);
                        wrapper.Buffer.Remove(0, termIdx + terminator.Length);
                        _channel?.ReportData(slotIndex, portName, resultLine);
                    }
                }
            });
        }

        port.Open();
        _ports[portName] = wrapper;
        Host.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已连接 (Baud: {baudRate})");
        _channel?.NotifyState(portName, DeviceState.Connected);
    }

    private void HandleDisconnectInternal(int slotIndex, string portName)
    {
        if (_ports.TryRemove(portName, out var wrapper))
        {
            wrapper.Dispose();
            Host.AddPluginLog(Id, $"[Slot {slotIndex}] [{portName}] 已断开连接");
            _channel?.NotifyState(portName, DeviceState.Disconnected);
        }
    }

    private PortWrapper GetWrapperOrThrow(int slotIndex, string portName)
    {
        if (_ports.TryGetValue(portName, out var wrapper) && wrapper.Port.IsOpen) return wrapper;
        throw new SerialPluginException($"串口 [{portName}] 未连接");
    }

    private (string portName, int baudRate) ParseAddress(string address)
    {
        var parts = address.Split(':');
        return (parts[0], parts.Length >= 2 && int.TryParse(parts[1], out var b) ? b : 9600);
    }
}
