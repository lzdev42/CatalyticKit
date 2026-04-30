using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CatalyticKit;
using SocketClient.Core;

namespace SocketClient.Plugin;

/// <summary>
/// Socket TCP 通讯器实现，支持多槽位并发通信。
/// 
/// 【核心设计：请求-响应匹配机制】
/// 
/// 本通讯器需要处理两种设备共享模式：
/// 
/// 1. 独享模式（isShared=false）：
///    - 一个设备地址绑定到特定槽位（slotIndex）
///    - 响应直接路由到绑定的槽位
///    - 即使多个槽位使用同一设备，也是"后发送的请求覆盖前一个槽位"的简单映射
/// 
/// 2. 共享模式（isShared=true）：
///    - 同一设备地址被多个槽位并发使用
///    - 响应消息中包含槽位标识（如 "HOME_OK 1" 中的 "1" 表示 slot 1）
///    - 设备侧使用 1-based 编号，系统使用 0-based，需要转换（slotNum - 1）
/// 
/// 【关键技术问题：为什么必须使用请求队列？】
/// 
/// 错误做法（会导致响应窜槽）：
///    在连接时（HandleConnect）记录 isShared 状态，后续响应时使用连接时的状态。
///    
/// 问题场景：
///    - 连接时可能没有槽位信息（slotIndex=-1），isShared 可能是 false
///    - 同一设备可能被不同槽位以不同模式使用（先独享后共享，或反之）
///    - 如果连接时固化 isShared 状态，后续响应会使用错误的路由逻辑
///    
///    例如：连接时 isShared=false，但后续 slot 5 发送独享请求，slot 0 发送共享请求，
///         响应时如果还用连接时的状态，会导致共享请求的响应被路由到错误的槽位。
/// 
/// 正确做法（本实现）：
///    每次发送请求时，将 (slotIndex, isShared) 入队；
///    每次收到响应时，从队列取出对应的请求信息，按请求时的状态决定路由策略。
///    
/// 这样保证了：
///    - 请求和响应严格按 FIFO 顺序匹配
///    - 每个响应使用对应请求时的 isShared 状态，而非连接时的状态
///    - 即使同一设备被不同槽位以不同模式使用，也能正确路由
/// 
/// 【队列数据结构说明】
///    _requestQueues: Key=设备地址, Value=请求队列
///    队列元素: (slotIndex, isShared) - 记录发送请求时的槽位和共享模式
///    
/// 【响应路由流程】
///    1. 收到响应消息
///    2. 从 _requestQueues[address] 队列出队一个请求记录
///    3. 如果 request.isShared=true：从消息内容解析槽位号（正则匹配数字，减1）
///    4. 如果 request.isShared=false：使用请求记录中的 slotIndex
///    5. 调用 ReportData(targetSlot, address, msg) 推送到对应槽位
/// </summary>
public class SocketCommunicator : ICommunicator
{
    private ICommChannel? _channel;
    
    /// <summary>
    /// 用于从共享设备的响应消息中解析槽位号。
    /// 设备响应格式示例："HOME_OK 1"，其中 "1" 表示 slot 1（设备侧 1-based）。
    /// </summary>
    private readonly Regex _slotRegex = new(@"\d+", RegexOptions.Compiled);
    
    /// <summary>
    /// 活跃的 Socket 客户端连接。
    /// Key: 设备地址（如 "127.0.0.1:12301"）
    /// Value: (客户端实例, 取消令牌)
    /// </summary>
    private readonly ConcurrentDictionary<string, (AsyncBaseClient client, CancellationTokenSource cts)> _clients = new();
    
    /// <summary>
    /// 请求队列，用于匹配响应到正确的槽位。
    /// 
    /// Key: 设备地址
    /// Value: 请求队列，每个元素包含发送请求时的 (slotIndex, isShared)
    /// 
    /// 为什么需要队列而不是简单的 address->slot 映射？
    ///   - 共享设备需要 FIFO 匹配请求和响应的顺序
    ///   - 同一设备可能被多个槽位并发请求
    ///   - 需要记录每次请求的 isShared 状态（见上方技术问题说明）
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(int slotIndex, bool isShared)>> _requestQueues = new();

    public string Id => "catalytic.socket-client";
    public string Protocol => "tcp";

    public Task ActivateAsync(ICommChannel channel)
    {
        Service.AddPluginLog(Id, "插件激活开始");
        _channel = channel;
        Service.AddPluginLog(Id, "插件激活完成");
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        Service.AddPluginLog(Id, $"插件停用开始，当前客户端数: {_clients.Count}");
        foreach (var (client, cts) in _clients.Values)
        {
           // Service.AddPluginLog(Id, $"正在断开客户端: {client.Address}");
            cts.Cancel();
            client.Dispose();
        }
        _clients.Clear();
        Service.AddPluginLog(Id, "插件停用完成");
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
        Service.AddPluginLog(Id, $"ExecuteTask 调用: slotIndex={slotIndex}, address={address}, action={action}, payload=[{payload}], isShared={options.IsShared}");

        try
        {
            switch (action)
            {
                case CommAction.Connect:
                    await HandleConnect(slotIndex, address, options);
                    break;
            case CommAction.Send:
            case CommAction.Query:
                if (_clients.TryGetValue(address, out var entry))
                {
                    // 【关键】将当前请求的 (slotIndex, isShared) 入队，用于后续响应路由。
                    // 如果不记录 isShared，后续响应可能使用错误的路由策略，导致响应窜槽。
                    // 详见类注释中的"技术问题"说明。
                    if (slotIndex >= 0)
                    {
                        _requestQueues.GetOrAdd(address, _ => new ConcurrentQueue<(int, bool)>()).Enqueue((slotIndex, options.IsShared));
                    }
                        entry.client.TxTerminator = options.CommandTerminator ?? "\n";
                        entry.client.RxTerminator = options.ResponseTerminator ?? "\n";
                        Service.AddPluginLog(Id, $"发送数据: address={address}, payload=[{payload}], TxTerminator=[{entry.client.TxTerminator}], RxTerminator=[{entry.client.RxTerminator}]");
                        await entry.client.SendAsync(payload);
                        Service.AddPluginLog(Id, $"发送完成: address={address}");
                    }
                    else
                    {
                        Service.AddPluginLog(Id, $"发送失败，客户端不存在: {address}");
                        throw new InvalidOperationException($"设备 [{address}] 未连接");
                    }
                    break;
                case CommAction.Disconnect:
                    Service.AddPluginLog(Id, $"断开连接请求: {address}");
                    if (_clients.TryRemove(address, out var old))
                    {
                        Service.AddPluginLog(Id, $"客户端已移除: {address}");
                        old.cts.Cancel();
                        old.client.Dispose();
                        _channel?.NotifyState(address, DeviceState.Disconnected);
                        Service.AddPluginLog(Id, $"通知断开状态: {address}");
                    }
                    else
                    {
                        Service.AddPluginLog(Id, $"断开失败，客户端不存在: {address}");
                    }
                    break;
                case CommAction.Status:
                    var state = _clients.ContainsKey(address) ? DeviceState.Connected : DeviceState.Disconnected;
                    Service.AddPluginLog(Id, $"状态查询: address={address}, state={state}");
                    _channel?.NotifyState(address, state);
                    break;
            }
            Service.AddPluginLog(Id, $"ExecuteTask 完成: action={action}, address={address}");
        }
        catch (Exception ex)
        {
            Service.AddPluginLog(Id, $"ExecuteTask 异常: action={action}, address={address}, 异常={ex.Message}");
            throw;
        }
    }

    private async Task HandleConnect(int slotIndex, string address, CommOptions options)
    {
        Service.AddPluginLog(Id, $"HandleConnect 开始: slotIndex={slotIndex}, address={address}, isShared={options.IsShared}");

        if (_clients.ContainsKey(address))
        {
            Service.AddPluginLog(Id, $"客户端已存在，跳过连接: {address}");
            _channel?.NotifyState(address, DeviceState.Connected);
            return;
        }

        var client = new AsyncBaseClient(address);

        Service.AddPluginLog(Id, $"创建新客户端: address={address}, TxTerminator=[{client.TxTerminator}], RxTerminator=[{client.RxTerminator}]");

        Service.AddPluginLog(Id, $"正在连接: {address}");
        await client.ConnectAsync();
        Service.AddPluginLog(Id, $"连接成功: {address}");

        var cts = new CancellationTokenSource();
        if (_clients.TryAdd(address, (client, cts)))
        {
            Service.AddPluginLog(Id, $"客户端已添加到字典: {address}");
            _channel?.NotifyState(address, DeviceState.Connected);
            Service.AddPluginLog(Id, $"后台读取任务已启动: address={address}, entrySlot={slotIndex}, isShared={options.IsShared}");
            _ = Task.Run(() => BackgroundReadLoop(slotIndex, address, client, options.IsShared, cts.Token));
        }
        else
        {
            Service.AddPluginLog(Id, $"客户端添加失败（并发冲突）: {address}");
            cts.Dispose();
            client.Dispose();
        }
    }

    private async Task BackgroundReadLoop(int entrySlot, string address, AsyncBaseClient client, bool isShared, CancellationToken token)
    {
        Service.AddPluginLog(Id, $"BackgroundReadLoop 启动: address={address}, entrySlot={entrySlot}, isShared={isShared}");

        while (!token.IsCancellationRequested)
        {
            string msg;
            try
            {
                Service.AddPluginLog(Id, $"等待接收数据: {address}");
                msg = await client.ReceiveNextAsync();
                Service.AddPluginLog(Id, $"收到消息: address={address}, raw=[{msg}]");
            }
            catch (Exception ex)
            {
                Service.AddPluginLog(Id, $"BackgroundReadLoop 异常退出: address={address}, 异常={ex.Message}");
                _clients.TryRemove(address, out _);
                _channel?.NotifyState(address, DeviceState.Disconnected);
                break;
            }

            if (string.IsNullOrEmpty(msg))
            {
                Service.AddPluginLog(Id, $"收到空消息，跳过: {address}");
                continue;
            }

            int targetSlot;
            // 【关键】从请求队列取出匹配信息，按发送请求时的 isShared 状态决定路由策略。
            // 如果队列为空（可能设备主动发送消息），回退到 entrySlot。
            if (_requestQueues.TryGetValue(address, out var queue) && queue.TryDequeue(out var request))
            {
                if (request.isShared)
                {
                    // 共享模式：从响应消息中解析槽位号（设备 1-based -> 系统 0-based）
                    var match = _slotRegex.Match(msg);
                    if (!match.Success || !int.TryParse(match.Value, out int slotNum))
                    {
                        Service.AddPluginLog(Id, $"共享模式解析失败: address={address}, msg=[{msg}]");
                        continue;
                    }
                    targetSlot = slotNum - 1;
                    Service.AddPluginLog(Id, $"共享模式路由: address={address}, msg=[{msg}], requestSlot={request.slotIndex}, parsedSlot={slotNum}, targetSlot={targetSlot}");
                }
                else
                {
                    // 独享模式：直接使用发送请求时记录的 slotIndex
                    targetSlot = request.slotIndex;
                    Service.AddPluginLog(Id, $"独享模式路由: address={address}, msg=[{msg}], targetSlot={targetSlot}");
                }
            }
            else
            {
                // 无匹配请求（设备主动推送或超时响应），使用连接时记录的 entrySlot
                Service.AddPluginLog(Id, $"无匹配请求: address={address}, msg=[{msg}], 使用entrySlot={entrySlot}");
                targetSlot = entrySlot;
            }

            // 【重要】推送到 Service 时使用 <slotIndex, address> 格式。
            // Service 通过 slotIndex 判定数据归属哪个槽位。
            // 共享设备时仅靠 address 无法区分槽位，必须使用 slotIndex。
            Service.AddPluginLog(Id, $"推送事件: address={address}, targetSlot={targetSlot}, eventType=Data, msg=[{msg}]");
            _channel?.ReportData(targetSlot, address, msg);
        }

        Service.AddPluginLog(Id, $"BackgroundReadLoop 退出: address={address}, IsCancellationRequested={token.IsCancellationRequested}");
    }
}