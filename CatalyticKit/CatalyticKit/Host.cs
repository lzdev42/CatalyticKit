using System.Collections.Concurrent;

namespace CatalyticKit;

/// <summary>
/// 提供对 Host 服务的静态访问入口。
/// 插件使用此类的静态方法来控制测试流程或获取全局服务。
/// 
/// 线程安全：此类的所有公开方法均可从任意线程安全调用。
/// </summary>
public static class Host
{
    private static volatile IHostBridge? _bridge;
    private static readonly ConcurrentDictionary<int, SlotProxy> _slots = new();

    // === 全局事件总线 ===
    private static readonly object _eventLock = new();
    private static Action<TestFinishedEventArgs>? _slotFinished;
    private static Action<int>? _slotStarted;

    /// <summary>
    /// [Internal] 设置底层 Bridge 实现
    /// </summary>
    public static void SetBridge(IHostBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    private static IReadOnlyList<ISlot>? _allSlotsCache;
    private static readonly object _slotsLock = new();

    // --- Global Commands ---

    /// <summary>
    /// 记录当前插件的专属独立日志（如协议通讯报文）。
    /// </summary>
    public static void AddPluginLog(string pluginId, string message)
    {
        if (_bridge is not { } bridge)
            throw new HostNotInitializedException();
        bridge.AddPluginLog(pluginId, message);
    }

    public static int GetSlotCount()
    {
        if (_bridge is not { } bridge)
            throw new HostNotInitializedException();
        return bridge.GetSlotCount();
    }

    /// <summary>
    /// 获取当前系统中所有 Slot 的操作接口实例。
    /// 可以方便地用于遍历操作所有通道。
    /// </summary>
    /// <returns>包含所有 ISlot 的只读集合</returns>
    /// <exception cref="HostNotInitializedException">如果 Host 尚未初始化 Service</exception>
    public static IReadOnlyList<ISlot> GetAllSlots()
    {
        if (_allSlotsCache != null)
            return _allSlotsCache;

        lock (_slotsLock)
        {
            if (_allSlotsCache != null)
                return _allSlotsCache;

            int count = GetSlotCount();
            var slots = new ISlot[count];
            for (int i = 0; i < count; i++)
            {
                slots[i] = Slot(i);
            }
            _allSlotsCache = slots;
            return _allSlotsCache;
        }
    }

    public static void StartAll()
    {
        if (_bridge is not { } bridge)
            throw new HostNotInitializedException();
        bridge.StartAll();
    }

    public static void StopAll()
    {
        if (_bridge is not { } bridge)
            throw new HostNotInitializedException();
        bridge.StopAll();
    }

    public static void ResetAll()
    {
        if (_bridge is not { } bridge)
            throw new HostNotInitializedException();
        bridge.ResetAll();
    }
    
    /// <summary>
    /// [Internal] 由 SlotProxy 调用，通知某个 Slot 已重置。
    /// </summary>
    internal static void NotifySlotReset(int slotIndex)
    {
        // 预留接口，目前无全局重置事件，仅用于满足逻辑闭环
    }

    // --- Global Events ---

    /// <summary>
    /// 当任一 Slot 的测试完成时触发。
    /// </summary>
    public static event Action<TestFinishedEventArgs>? NotifySlotFinished
    {
        add { lock (_eventLock) _slotFinished += value; }
        remove { lock (_eventLock) _slotFinished -= value; }
    }

    /// <summary>
    /// 当任一 Slot 的测试开始时触发。
    /// </summary>
    public static event Action<int>? NotifySlotStarted
    {
        add { lock (_eventLock) _slotStarted += value; }
        remove { lock (_eventLock) _slotStarted -= value; }
    }

    // --- Slot Access ---

    /// <param name="index">槽位索引 (0-based)</param>
    /// <returns>Slot 操作对象</returns>
    public static ISlot Slot(int index)
    {
        if (_bridge is not { } bridge)
            throw new HostNotInitializedException();

        return _slots.GetOrAdd(index, i => new SlotProxy(i, bridge));
    }

    /// <summary>
    /// 获取当前已加载的测试流程定义（全量步骤静态配置，不依赖任何测试执行历史）。
    /// 可在测试开始前调用，用于获取所有测试项的名称和检查上下限，以生成 CSV 报告表头等。
    /// </summary>
    /// <returns>完整的流程定义；若 Engine 尚未加载流程则返回 null</returns>
    /// <exception cref="HostNotInitializedException">如果 Host 尚未初始化 Service</exception>
    public static TestFlow? GetFlowDefinition()
    {
        if (_bridge is not { } bridge)
            throw new HostNotInitializedException();
        return bridge.GetFlowDefinition();
    }

    /// <summary>
    /// 获取报告输出目录的绝对路径（即工作目录下的 reports 子目录）。
    /// 插件可将 CSV、PDF 等报告文件写入此目录。
    /// </summary>
    /// <exception cref="HostNotInitializedException">如果 Host 尚未初始化</exception>
    public static string ReportFolder()
    {
        if (_bridge is not { } bridge)
            throw new HostNotInitializedException();
        return bridge.GetReportFolder();
    }

    /// <summary>
    /// [Internal] 重置 Host 状态 (仅用于测试或 Host 重启)
    /// </summary>
    internal static void Reset()
    {
        _slots.Clear();
        _bridge = null;
    }

    /// <summary>
    /// [Host Internal] 供 HostBridge 调用，触发槽位完成事件。
    /// </summary>
    public static void RaiseSlotFinished(int slotIndex, bool passed, string? errorMessage)
    {
        var handler = _slotFinished;
        if (handler == null) return;

        var args = new TestFinishedEventArgs
        {
            SlotIndex = slotIndex,
            Passed = passed,
            ErrorMessage = errorMessage
        };

        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action<TestFinishedEventArgs>)d)(args); }
            catch (Exception ex) { _bridge?.ReportPluginError("host-event", ex); }
        }
    }

    /// <summary>
    /// [Host Internal] 供 HostBridge 调用，触发槽位开始事件。
    /// </summary>
    public static void RaiseSlotStarted(int slotIndex)
    {
        var handler = _slotStarted;
        if (handler == null) return;

        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action<int>)d)(slotIndex); }
            catch (Exception ex) { _bridge?.ReportPluginError("host-event", ex); }
        }
    }
}

