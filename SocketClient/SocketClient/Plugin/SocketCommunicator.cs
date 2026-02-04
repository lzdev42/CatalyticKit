using System.Collections.Concurrent;
using CatalyticKit;
using SocketClient.Core;

namespace SocketClient.Plugin;

/// <summary>
/// 插件业务异常，用于替代系统级的 InvalidOperationException
/// </summary>
public sealed class SocketPluginException : Exception
{
    public SocketPluginException(string message) : base(message) { }
}

/// <summary>
/// 通用 Socket 插件适配器
/// 负责将 ICommunicator 接口调用映射到 GenSocketClient
/// </summary>
public class SocketCommunicator : ICommunicator
{
    private IPluginContext? _context;
    
    // 管理多路连接: Key = "IP:Port"
    // 使用线程安全字典，确保 Slot 隔离
    private readonly ConcurrentDictionary<string, GenSocketClient> _clients = new();

    public string Id => "catalytic.socket-client";
    public string Protocol => "tcp"; // 虽叫 tcp，但也支持 udp (未来)，目前 manifest 里写了 protocols: ["tcp", "udp"]

    public Task ActivateAsync(IPluginContext context)
    {
        _context = context;
        _context.Log(LogLevel.Info, "Generic Socket Client Plugin Activated");
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();
        _context?.Log(LogLevel.Info, "Generic Socket Client Plugin Deactivated");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 核心执行方法 (Thread-Safe, Non-blocking)
    /// </summary>
    public async Task<byte[]> ExecuteAsync(
        string address, 
        string action, 
        byte[] payload, 
        int timeoutMs, 
        CancellationToken ct)
    {
        try
        {
            // 1. 尝试解析为标准 CommAction
            if (Enum.TryParse<CommAction>(action, true, out var commAction))
            {
                switch (commAction)
                {
                    case CommAction.Connect:
                        return await HandleConnect(address, timeoutMs);
                    
                    case CommAction.Disconnect:
                        return await HandleDisconnect(address);
                    
                    case CommAction.Send:
                        return await HandleSend(address, payload);
                    
                    case CommAction.Read:
                        return await HandleRead(address);
                    
                    case CommAction.Query:
                        await HandleSend(address, payload);
                        return await HandleWait(address, timeoutMs, null, ct); 
                        
                    case CommAction.Wait:
                        return await HandleWait(address, timeoutMs, null, ct);
    
                    case CommAction.Status:
                        var client = GetClientOrThrow(address);
                        return client.IsConnected ? "1"u8.ToArray() : "0"u8.ToArray();
                }
            }
            
            throw new ArgumentException($"Unsupported action: {action}");
        }
        catch (OperationCanceledException)
        {
            // 正常取消或超时（如果 GenSocketClient 抛出 TimeoutException，会走下面 Exception catch? 不，TimeoutException 是 Exception）
            // 如果是用户取消，不 Log Error
            throw;
        }
        catch (TimeoutException ex)
        {
             _context?.Log(LogLevel.Warning, $"[{Id}] Action '{action}' on {address} timed out: {ex.Message}");
             throw; // Host 会处理为 SubmitTimeout
        }
        catch (Exception ex)
        {
            // 记录详细上下文，防止 Host 吞掉细节
            _context?.Log(LogLevel.Error, $"[{Id}] Action '{action}' on {address} failed: {ex.Message}");
            throw; // 重新抛出，让 Host 提交 SubmitError
        }
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
        try
        {
            if (Enum.TryParse<CommAction>(action, true, out var commAction))
            {
                switch (commAction)
                {
                    case CommAction.Connect:
                        return await HandleConnect(address, options.TimeoutMs);
                    
                    case CommAction.Disconnect:
                        return await HandleDisconnect(address);
                    
                    case CommAction.Send:
                        return await HandleSend(address, payload);
                    
                    case CommAction.Read:
                        return await HandleRead(address);
                    
                    case CommAction.Query:
                        await HandleSend(address, payload);
                        return await HandleWait(address, options.TimeoutMs, options.Terminator, ct); 
                        
                    case CommAction.Wait:
                        return await HandleWait(address, options.TimeoutMs, options.Terminator, ct);
    
                    case CommAction.Status:
                        var client = GetClientOrThrow(address);
                        return client.IsConnected ? "1"u8.ToArray() : "0"u8.ToArray();
                }
            }
            
            throw new ArgumentException($"Unsupported action: {action}");
        }
        catch (OperationCanceledException)
        {
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
            throw;
        }
    }

    private async Task<byte[]> HandleConnect(string address, int timeoutMs)
    {
        if (_clients.TryGetValue(address, out var existing) && existing.IsConnected)
        {
            _context?.Log(LogLevel.Info, $"[{address}] Already connected");
            return Array.Empty<byte>();
        }

        // 解析 IP:Port
        if (!TryParseAddress(address, out var host, out var port))
            throw new ArgumentException($"Invalid address format: {address}. Expected IP:Port");

        var client = new GenSocketClient();
        
        // 绑定 Push 事件：当收到数据时，推送到 Host (UI 监控)
        client.DataReceived += (data) => 
        {
            // 使用约定的 EventType 格式 "DeviceData:{address}"
            // 这样 Host 端 DataManager 可以通过解析 EventType 知道是哪个设备的数据
            _context?.PushEvent($"{PluginEvents.DeviceData}:{address}", data);
        };

        client.Disconnected += () =>
        {
             _context?.PushEvent(PluginEvents.DeviceDisconnected, System.Text.Encoding.UTF8.GetBytes(address));
             _context?.Log(LogLevel.Warning, $"[{address}] Disconnected remotely");
        };

        await client.ConnectAsync(host, port, timeoutMs);
        
        _clients[address] = client;
        _context?.Log(LogLevel.Info, $"[{address}] Connected");
        _context?.LogTraffic($"Host->{address}", "CONNECT");
        return Array.Empty<byte>();
    }

    private async Task<byte[]> HandleDisconnect(string address)
    {
        if (_clients.TryRemove(address, out var client))
        {
            client.Disconnect();
            client.Dispose();
            _context?.Log(LogLevel.Info, $"[{address}] Disconnected");
        }
        return Array.Empty<byte>();
    }

    private async Task<byte[]> HandleSend(string address, byte[] payload)
    {
        var client = GetClientOrThrow(address);
        await client.SendAsync(payload);
        _context?.LogTraffic($"Host->{address}", HexUtil.ToHexString(payload));
        return Array.Empty<byte>();
    }

    private Task<byte[]> HandleRead(string address)
    {
        var client = GetClientOrThrow(address);
        var data = client.ReadAll();
        if (data.Length > 0)
            _context?.LogTraffic($"{address}->Host", HexUtil.ToHexString(data));
        return Task.FromResult(data);
    }

    private async Task<byte[]> HandleWait(string address, int timeoutMs, string? terminator, CancellationToken ct)
    {
        var client = GetClientOrThrow(address);
        return await client.WaitAsync(timeoutMs == 0 ? -1 : timeoutMs, terminator, ct);
    }
    
    // --- Helper Methods ---

    private GenSocketClient GetClientOrThrow(string address)
    {
        if (_clients.TryGetValue(address, out var client) && client.IsConnected)
            return client;
        
        throw new SocketPluginException($"设备 [{address}] 未连接，请先执行 Connect 动作");
    }

    private bool TryParseAddress(string address, out string host, out int port)
    {
        host = "";
        port = 0;
        var parts = address.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[1], out port)) return false;
        host = parts[0];
        return true;
    }
}
