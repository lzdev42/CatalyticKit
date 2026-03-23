using System.Collections.Concurrent;
using System.Text;
using CatalyticKit;
using SocketClient.Core;

namespace SocketClient.Plugin;

/// <summary>
/// 插件业务异常，用于替代系统级的 InvalidOperationException
/// </summary>
public sealed class SocketPluginException(string message) : Exception(message);

/// <summary>
/// 通用 Socket 插件适配器
/// 负责将 ICommunicator 接口调用映射到 GenSocketClient
/// </summary>
public class SocketCommunicator : ICommunicator
{
    private IPluginContext? _context;
    
    // 管理多路连接: Key = "IP:Port"
    private readonly ConcurrentDictionary<string, GenSocketClient> _clients = new();

    // 管理地址级并发锁: Key = "IP:Port"
    // 确保同一个地址的 Connect/Send/Read/Disconnect 在多 Slot 间互斥排队
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _addressLocks = new();

    public string Id => "catalytic.socket-client";
    public string Protocol => "tcp";

    public Task ActivateAsync(IPluginContext context)
    {
        _context = context;
        _context.Log(-1, LogLevel.Info, "通用 Socket 客户端插件 (线程安全版) 已激活");
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();

        foreach (var semaphore in _addressLocks.Values)
        {
            try { semaphore.Dispose(); } catch { }
        }
        _addressLocks.Clear();

        _context?.Log(-1, LogLevel.Info, "通用 Socket 客户端插件已停用");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行通讯动作（带高级选项，全量加锁保护）
    /// </summary>
    public async Task<byte[]> ExecuteAsync(
        int slotIndex,
        string address,
        string action,
        byte[] payload,
        ExecuteOptions options,
        CancellationToken ct)
    {
        var termFromHost = options.Terminator == null ? "NULL" : options.Terminator.Replace("\r", "\\r").Replace("\n", "\\n");
        var payloadText = payload == null ? "EMPTY" : Encoding.UTF8.GetString(payload).Replace("\r","\\r").Replace("\n","\\n");
        var termOnOption = options.Terminator == null ? "NULL" : options.Terminator.Replace("\r","\\r").Replace("\n","\\n");

        Service.AddPluginLog(Id, $"[Host 入口] 动作:{action}, Payload文本:[{payloadText}], 参数中的Terminator:[{termOnOption}]");
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("地址不能为空");

        // 1. 获取该地址的专用信号量
        var semaphore = _addressLocks.GetOrAdd(address, _ => new SemaphoreSlim(1, 1));

        // 2. 获取锁
        await semaphore.WaitAsync(ct);

        try
        {
            if (Enum.TryParse<CommAction>(action, true, out var commAction))
            {
                switch (commAction)
                {
                    case CommAction.Connect:
                        return await HandleConnectInternal(slotIndex, address, options.TimeoutMs, options.Terminator);

                    case CommAction.Disconnect:
                        return HandleDisconnectInternal(slotIndex, address);

                    case CommAction.Send:
                        var sendClient = GetClientOrThrow(slotIndex, address);
                        sendClient.ClearBuffer(); // 核心：请求前强制清空，解决超时残留问题
                        var sendText = Encoding.UTF8.GetString(payload);
                        Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] (发送) {sendText}");
                        await sendClient.SendAsync(sendText, options.Terminator);
                        _context?.Log(slotIndex, LogLevel.Debug, $"[{address}] 已发送: {sendText}");
                        return Array.Empty<byte>();

                    case CommAction.Read:
                        var readClient = GetClientOrThrow(slotIndex, address);
                        var data = await readClient.WaitAsync(options.TimeoutMs == 0 ? 5000 : options.TimeoutMs, options.Terminator, ct);
                        Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] (接收) {data}");
                        _context?.Log(slotIndex, LogLevel.Debug, $"[{address}] 已读取 (Wait): {data}");
                        return Encoding.UTF8.GetBytes(data);

                    case CommAction.Query:
                        var queryClient = GetClientOrThrow(slotIndex, address);
                        queryClient.ClearBuffer(); // 核心：请求前强制清空，解决超时残留问题
                        var queryText = Encoding.UTF8.GetString(payload);
                        
                        Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] (发送 Query) {queryText}");
                        await queryClient.SendAsync(queryText, options.Terminator);
                        _context?.Log(slotIndex, LogLevel.Debug, $"[{address}] 已发送 (Query): {queryText}");
                        
                        var response = await queryClient.WaitAsync(options.TimeoutMs == 0 ? 5000 : options.TimeoutMs, options.Terminator, ct);
                        Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] (接收 Query) {response}");
                        _context?.Log(slotIndex, LogLevel.Debug, $"[{address}] 已接收 (Query): {response}");
                        return Encoding.UTF8.GetBytes(response);

                    case CommAction.Status:
                        bool connected = _clients.TryGetValue(address, out var c) && c.IsConnected;
                        return connected ? "1"u8.ToArray() : "0"u8.ToArray();
                }
            }

            throw new ArgumentException($"不支持的动作类型: {action}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            _context?.Log(slotIndex, LogLevel.Warning, $"[{Id}] 在地址 {address} 执行动作 '{action}' 超时: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _context?.Log(slotIndex, LogLevel.Error, $"[{Id}] 在地址 {address} 执行动作 '{action}' 失败: {ex.Message}");
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    // --- 内部处理方法 ---

    private async Task<byte[]> HandleConnectInternal(int slotIndex, string address, int timeoutMs, string? terminator)
    {
        if (_clients.TryGetValue(address, out var existing) && existing.IsConnected)
        {
            _context?.Log(slotIndex, LogLevel.Info, $"[{address}] 设备已处于连接状态");
            return Array.Empty<byte>();
        }

        if (!TryParseAddress(address, out var host, out var port))
            throw new ArgumentException($"地址格式错误: {address}。应为 IP:Port 分隔格式");

        // 使用 options 中的结束符，若无则使用默认 \r\n
        var client = new GenSocketClient(terminator ?? "\r\n");

        client.Disconnected += () =>
        {
            // 使用新版 PushEvent，包含 slotIndex 和 address
            _context?.PushEvent(slotIndex, address, PluginEvents.DeviceDisconnected, Encoding.UTF8.GetBytes(address));
            _context?.Log(slotIndex, LogLevel.Warning, $"[{address}] 远端探测到断开连接");
        };

        await client.ConnectAsync(host, port, timeoutMs);

        _clients[address] = client;
        _context?.Log(slotIndex, LogLevel.Info, $"[{address}] 连接成功 (Terminator: {terminator ?? "\\r\\n"})");
        return Array.Empty<byte>();
    }

    private byte[] HandleDisconnectInternal(int slotIndex, string address)
    {
        if (_clients.TryRemove(address, out var client))
        {
            client.Disconnect();
            client.Dispose();
            _context?.Log(slotIndex, LogLevel.Info, $"[{address}] 已人工断开连接");
        }
        return Array.Empty<byte>();
    }

    /// <summary>
    /// 获取客户端并检查连接状态
    /// </summary>
    private GenSocketClient GetClientOrThrow(int slotIndex, string address)
    {
        if (_clients.TryGetValue(address, out var client) && client.IsConnected)
        {
            return client;
        }

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
