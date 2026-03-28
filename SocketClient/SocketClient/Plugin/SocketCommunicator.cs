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

    // 管理地址到 SlotIndex 的映射 (仅用于独占模式，在 Send 阶段写入)
    private readonly ConcurrentDictionary<string, int> _addressToSlot = new();

    // 记录当前处于共享模式的地址（当 HashSet 使用，key=address，value 无意义）
    // 只存 shared 地址，Connect 时写入，Disconnect 时清理
    private readonly ConcurrentDictionary<string, byte> _sharedAddresses = new();

    // 共享模式：从消息中提取槽位号（1-based）
    // 你的协议保证：消息中只会出现槽位数字，如果对方乱发数字，那是对方的问题
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
            try
            {
                cts.Cancel();
            }
            catch
            {
                // 忽略取消时的异常
            }
            finally
            {
                cts.Dispose();
            }
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
        string address,
        CommAction action,
        string payload,
        ExecuteOptions options,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("地址不能为空", nameof(address));

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
                    // Send / Query：只负责发送，返回由 BackgroundReadLoop 通过 PushEvent 回推
                    await HandleSend(slotIndex, address, payload);
                    break;

                case CommAction.Read:
                    // Read：后台循环一直在跑，这里只打个日志
                    Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] Read 动作已由后台循环处理");
                    break;

                case CommAction.Status:
                    // 这里只是示例：简单判断连接状态并打日志
                    // 如果你有统一的状态查询机制，可以在这里通过 PushEvent 或其他通道返回
                    bool isConnected = _clients.TryGetValue(address, out _);
                    Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] Status 查询: Connected={isConnected}");
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
        // 已存在连接则复用
        if (_clients.TryGetValue(address, out _))
        {
            Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] 已经处于连接状态");
            return;
        }

        // 创建并连接 client（这里有可能被并发调用）
        var client = new AsyncBaseClient(address, options.Terminator ?? "\n", options.Terminator ?? "\n");
        await client.ConnectAsync();

        // 修复竞态：TryAdd 失败时必须 Dispose，避免泄漏 [web:16]
        if (!_clients.TryAdd(address, client))
        {
            client.Dispose();
            Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] 连接时发现已有并发连接，丢弃当前实例");
            return;
        }

        // 记录共享模式地址（Connect 阶段只做标记，不绑定 slot）
        if (options.IsShared)
        {
            _sharedAddresses.TryAdd(address, 0);
        }
        // 注意：独占模式的 _addressToSlot 绑定延迟到第一次 Send 时写入，
        // 因为 Connect 时 slotIndex=-1（DeviceManager 发起，不代表具体槽位）

        // 启动背景读取任务
        var cts = new CancellationTokenSource();

        if (_readerCts.TryAdd(address, cts))
        {
            // isShared 根据 _sharedAddresses 读取，而非直接传参，使读取侧与写入侧逻辑保持一致
            _ = Task.Run(() => BackgroundReadLoop(address, client, _sharedAddresses.ContainsKey(address), cts.Token), cts.Token);
        }
        else
        {
            // 极端情况下，如果 CTS 添加失败，也要把连接清掉
            cts.Cancel();
            cts.Dispose();
            await HandleDisconnect(address);
            Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] 无法启动背景读取任务，已回滚连接");
            return;
        }

        _context?.NotifyConnectionStateChanged(address, PluginDeviceConnectionState.Connected);
        Service.AddPluginLog(Id, $"[Slot {slotIndex}] [{address}] 连接成功 (Shared={options.IsShared})");
    }

    private async Task HandleDisconnect(string address)
    {
        if (_readerCts.TryRemove(address, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
                // 忽略取消异常
            }
            finally
            {
                cts.Dispose();
            }
        }

        if (_clients.TryRemove(address, out var client))
        {
            client.Dispose();
            _addressToSlot.TryRemove(address, out _);
            _sharedAddresses.TryRemove(address, out _);
            _context?.NotifyConnectionStateChanged(address, PluginDeviceConnectionState.Disconnected);
            Service.AddPluginLog(Id, $"[{address}] 已断开连接");
        }

        await Task.CompletedTask;
    }

    private async Task HandleSend(int slotIndex, string address, string payload)
    {
        if (_clients.TryGetValue(address, out var client))
        {
            // 独占模式：Send 时才确定绑定关系（Connect 时 slotIndex 可能为 -1）
            if (slotIndex >= 0 && !_sharedAddresses.ContainsKey(address))
            {
                _addressToSlot[address] = slotIndex;
            }

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
                string msg;

                try
                {
                    msg = await client.ReceiveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    // 连接关闭或 CTS 取消时退出循环
                    break;
                }

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
                    if (!_addressToSlot.TryGetValue(address, out targetSlot))
                    {
                        targetSlot = -1;
                    }
                }

                if (targetSlot >= 0)
                {
                    Service.AddPluginLog(Id, $"[{address}] 接收到目标槽位 {targetSlot} 的返回数据: {Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(msg))}");
                    _context?.PushEvent(targetSlot, address, PluginEventType.Result, msg);
                }
                else
                {
                    Service.AddPluginLog(Id, $"[{address}] 无法识别目标槽位的返回数据: {Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(msg))}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消退出
        }
        catch (Exception ex)
        {
            Service.AddPluginLog(Id, $"[{address}] 背景读取循环发生异常: {ex.Message}");
            // 如果连接意外断开，触发清理
            _ = HandleDisconnect(address);
        }
    }
}