using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using CatalyticKit;
using SocketClient.Core;

namespace SocketClient.Plugin;

public class SocketCommunicator : ICommunicator
{
    private IPluginContext? _context;
    
    // 管理多个连接: Key = Address (IP:Port)
    private readonly ConcurrentDictionary<string, AsyncBaseClient> _clients = new();
    
    // 管理背景读取任务的取消令牌: Key = Address
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _readerCts = new();
    
    // 管理地址到 SlotIndex 的映射 (仅用于 isShared = false)
    private readonly ConcurrentDictionary<string, int> _addressToSlot = new();

    private readonly Regex _slotRegex = new(@"\d+", RegexOptions.Compiled);

    public string Id => "catalytic.socket-client";
    public string Protocol => "tcp";

    public Task ActivateAsync(IPluginContext context)
    {
        _context = context;
        Service.AddPluginLog(Id, "Socket 通讯插件已激活");
        return Task.CompletedTask;
    }

    public async Task DeactivateAsync()
    {
        Service.AddPluginLog(Id, "Socket 通讯插件正在停用...");
        
        // 停止所有背景读取任务
        foreach (var cts in _readerCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _readerCts.Clear();

        // 释放所有连接
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();
        _addressToSlot.Clear();

        await Task.CompletedTask;
    }

    public async Task ExecuteTask(
        int slotIndex,

        address,
        CommAction action,
        string payload,
        ExecuteOptions options,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("地址不能为空");

        try
        {
            switch (action)
            {
                case CommAction.Connect:
                    await HandleConnect(slotIndex, address, options, ct);
                    break;

                case CommAction.Disconnect:
                    await HandleDisconnect(address);
                    break;

                case CommAction.Send:
                case CommAction.Query:
                    // 无论是 Send 还是 Query，在 SocketClient 中由于是异步解析返回，
                    // 这里只负责发送。Query 的结果会由 BackgroundReadLoop 通过 PushEvent 返回。
                    await HandleSend(slotIndex, address, payload);
                    break;

                case CommAction.Read:
                    // 由于后台读取循环一直在运行，Read 动作在这里通常不需要额外操作。
                    Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] Read 动作已由后台循环处理");
                    break;

                case CommAction.Status:
                    bool isConnected = _clients.TryGetValue(address, out _);
                    // 注意：Status 通常不通过 PushEvent 返回 Result，而是直接查询或由 NotifyConnectionStateChanged 维护
                    break;
            }
        }
        catch (Exception ex)
        {
            Service.AddPluginLog(Id, $"[Slot {slotIndex}] [Error] 执行动作 '{action}' 失败: {ex.Message}");
            throw;
        }
    }

    private async Task HandleConnect(int slotIndex, string address, ExecuteOptions options, CancellationToken ct)
    {
        if (_clients.TryGetValue(address, out _))
        {
            Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] 已经处于连接状态");
            return;
        }

        var client = new AsyncBaseClient(address, options.Terminator ?? "\n", options.Terminator ?? "\n");
        await client.ConnectAsync();

        if (_clients.TryAdd(address, client))
        {
            // 记录地址到槽位的映射 (仅用于非共享模式分发)
            if (!options.IsShared)
            {
                _addressToSlot[address] = slotIndex;
            }

            // 启动背景读取任务
            var cts = new CancellationTokenSource();
            if (_readerCts.TryAdd(address, cts))
            {
                _ = Task.Run(() => BackgroundReadLoop(address, client, options.IsShared, cts.Token), cts.Token);
            }

            _context?.NotifyConnectionStateChanged(address, PluginDeviceConnectionState.Connected);
            Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] 连接成功 (Shared={options.IsShared})");
        }
    }

    private async Task HandleDisconnect(string address)
    {
        if (_readerCts.TryRemove(address, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (_clients.TryRemove(address, out var client))
        {
            client.Dispose();
            _addressToSlot.TryRemove(address, out _);
            _context?.NotifyConnectionStateChanged(address, PluginDeviceConnectionState.Disconnected);
            Service.AddPluginLog(Id, $"[{address}] 已断开连接");
        }

        await Task.CompletedTask;
    }

    private async Task HandleSend(int slotIndex, string address, string payload)
    {
        if (_clients.TryGetValue(address, out var client))
        {
            await client.SendAsync(payload);
            Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] 已发送指令: {payload}");
        }
        else
        {
            throw new InvalidOperationException($"设备 [{address}] 未连接，请先执行 Connect");
        }
    }

    private async Task BackgroundReadLoop(string address, AsyncBaseClient client, bool isShared, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string msg = await client.ReceiveNextAsync();
                int targetSlot = -1;

                if (isShared)
                {
                    // 共享模式：使用正则表达式提取第一个数字 (1-based -> 0-based)
                    var match = _slotRegex.Match(msg);
                    if (match.Success && int.TryParse(match.Value, out int slotNum))
                    {
                        targetSlot = slotNum - 1;
                    }
                }
                else
                {
                    // 独占模式：使用建立连接时记录的槽位
                    _addressToSlot.TryGetValue(address, out targetSlot);
                }

                if (targetSlot >= 0)
                {
                    _context?.PushEvent(targetSlot, address, PluginEventType.Result, msg);
                }
                else
                {
                    Service.AddPluginLog(Id, $"[{address}] 无法识别目标槽位的返回数据: {msg}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Service.AddPluginLog(Id, $"[{address}] 背景读取循环发生异常: {ex.Message}");
            // 如果连接意外断开，触发清理
            _ = HandleDisconnect(address);
        }
    }
}
