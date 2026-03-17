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
        _context.Log(LogLevel.Info, "通用 Socket 客户端插件 (线程安全版) 已激活");
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

        _context?.Log(LogLevel.Info, "通用 Socket 客户端插件已停用");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 核心执行方法 (Thread-Safe)
    /// </summary>
    public async Task<byte[]> ExecuteAsync(
        string address,
        string action,
        byte[] payload,
        int timeoutMs,
        CancellationToken ct)
    {
        return await ExecuteAsync(address, action, payload, new ExecuteOptions { TimeoutMs = timeoutMs }, ct);
    }

    /// <summary>
    /// 执行通讯动作（带高级选项，全量加锁保护）
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

        // 1. 获取该地址的专用信号量
        var semaphore = _addressLocks.GetOrAdd(address, _ => new SemaphoreSlim(1, 1));

        // 2. 获取锁 (排队时间受 CancellationToken 控制，不占用 options.TimeoutMs)
        await semaphore.WaitAsync(ct);

        try
        {
            if (Enum.TryParse<CommAction>(action, true, out var commAction))
            {
                switch (commAction)
                {
                    case CommAction.Connect:
                        return await HandleConnectInternal(address, options.TimeoutMs);

                    case CommAction.Disconnect:
                        return HandleDisconnectInternal(address);

                    case CommAction.Send:
                        var sendClient = GetClientOrThrow(address);
                        sendClient.ReadAll(); // 补丁：发送前清理上一 Slot 可能遗留的脏数据
                        await sendClient.SendAsync(payload);
                        _context?.Log(LogLevel.Debug, $"[{address}] 已发送: {payload.ToHexStringWithSpaces()}");
                        return Array.Empty<byte>();

                    case CommAction.Read:
                        var readClient = GetClientOrThrow(address);
                        var data = readClient.ReadAll();
                        if (data.Length > 0)
                            _context?.Log(LogLevel.Debug, $"[{address}] 已读取: {data.ToHexStringWithSpaces()}");
                        return data;

                    case CommAction.Query:
                        var queryClient = GetClientOrThrow(address);
                        queryClient.ReadAll(); // 补丁：Query 前物理清空缓冲区
                        await queryClient.SendAsync(payload);
                        _context?.Log(LogLevel.Debug, $"[{address}] 已发送 (Query): {payload.ToHexStringWithSpaces()}");
                        
                        var response = await queryClient.WaitAsync(options.TimeoutMs == 0 ? -1 : options.TimeoutMs, options.Terminator, ct);
                        _context?.Log(LogLevel.Debug, $"[{address}] 已接收 (Query): {response.ToHexStringWithSpaces()}");
                        return response;

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
            _context?.Log(LogLevel.Warning, $"[{Id}] 在地址 {address} 执行动作 '{action}' 超时: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _context?.Log(LogLevel.Error, $"[{Id}] 在地址 {address} 执行动作 '{action}' 失败: {ex.Message}");
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    // --- 内部处理方法 ---

    private async Task<byte[]> HandleConnectInternal(string address, int timeoutMs)
    {
        if (_clients.TryGetValue(address, out var existing) && existing.IsConnected)
        {
            _context?.Log(LogLevel.Info, $"[{address}] 设备已处于连接状态");
            return Array.Empty<byte>();
        }

        if (!TryParseAddress(address, out var host, out var port))
            throw new ArgumentException($"地址格式错误: {address}。应为 IP:Port 分隔格式");

        var client = new GenSocketClient();

        client.DataReceived += (data) =>
        {
            _context?.PushEvent($"{PluginEvents.DeviceData}:{address}", data);
        };

        client.Disconnected += () =>
        {
            _context?.PushEvent(PluginEvents.DeviceDisconnected, Encoding.UTF8.GetBytes(address));
            _context?.Log(LogLevel.Warning, $"[{address}] 远端异常断开连接");
        };

        await client.ConnectAsync(host, port, timeoutMs);

        _clients[address] = client;
        _context?.Log(LogLevel.Info, $"[{address}] 连接成功");
                        _context?.Log(LogLevel.Debug, $"[{address}] 执行连接流程 (线程安全模式)");
        return Array.Empty<byte>();
    }

    private byte[] HandleDisconnectInternal(string address)
    {
        if (_clients.TryRemove(address, out var client))
        {
            client.Disconnect();
            client.Dispose();
            _context?.Log(LogLevel.Info, $"[{address}] 已断开连接");
        }
        return Array.Empty<byte>();
    }

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
