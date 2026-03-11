using System.Collections.Concurrent;

namespace CatalyticKit;

/// <summary>
/// 提供对 Host 服务的静态访问入口。
/// 插件使用此类的静态方法来控制测试流程或获取全局服务。
/// 
/// 线程安全：此类的所有公开方法均可从任意线程安全调用。
/// </summary>
public static class Service
{
    private static volatile IHostBridge? _bridge;
    private static readonly ConcurrentDictionary<int, SlotProxy> _slots = new();

    /// <summary>
    /// [Host Internal] 设置 Host Bridge 实现。
    /// 必须在 Host 启动时、加载任何插件之前调用。
    /// </summary>
    /// <param name="bridge">Host Bridge 实现</param>
    public static void SetBridge(IHostBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    private static IReadOnlyList<ISlot>? _allSlotsCache;
    private static readonly object _slotsLock = new();

    // --- Global Commands ---

    /// <summary>
    /// 获取系统配置的总 Slot 数量
    /// </summary>
    /// <returns>Slot 数量</returns>
    /// <exception cref="ServiceNotInitializedException">如果 Host 尚未初始化 Service</exception>
    public static int GetSlotCount()
    {
        if (_bridge is not { } bridge)
            throw new ServiceNotInitializedException();
        return bridge.GetSlotCount();
    }

    /// <summary>
    /// 获取当前系统中所有 Slot 的操作接口实例。
    /// 可以方便地用于遍历操作所有通道。
    /// </summary>
    /// <returns>包含所有 ISlot 的只读集合</returns>
    /// <exception cref="ServiceNotInitializedException">如果 Host 尚未初始化 Service</exception>
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

    /// <summary>
    /// 启动所有 Slot 的测试 (非阻塞)
    /// </summary>
    /// <exception cref="ServiceNotInitializedException">如果 Host 尚未初始化 Service</exception>
    public static void StartAll()
    {
        if (_bridge is not { } bridge)
            throw new ServiceNotInitializedException();
        bridge.StartAll();
    }

    /// <summary>
    /// 停止所有 Slot 的测试 (非阻塞)
    /// </summary>
    /// <exception cref="ServiceNotInitializedException">如果 Host 尚未初始化 Service</exception>
    public static void StopAll()
    {
        if (_bridge is not { } bridge)
            throw new ServiceNotInitializedException();
        bridge.StopAll();
    }

    // --- Slot Access ---

    /// <summary>
    /// 获取指定 Slot 的操作接口。
    /// </summary>
    /// <param name="index">Slot 索引 (0-based)</param>
    /// <returns>Slot 操作对象</returns>
    /// <exception cref="ServiceNotInitializedException">如果 Host 尚未初始化 Service</exception>
    public static ISlot Slot(int index)
    {
        if (_bridge is not { } bridge)
            throw new ServiceNotInitializedException();

        return _slots.GetOrAdd(index, i => new SlotProxy(i, bridge));
    }

    /// <summary>
    /// 获取当前已加载的测试流程定义（全量步骤静态配置，不依赖任何测试执行历史）。
    /// 可在测试开始前调用，用于获取所有测试项的名称和检查上下限，以生成 CSV 报告表头等。
    /// </summary>
    /// <returns>完整的流程定义；若 Engine 尚未加载流程则返回 null</returns>
    /// <exception cref="ServiceNotInitializedException">如果 Host 尚未初始化 Service</exception>
    public static FlowDefinition? GetFlowDefinition()
    {
        if (_bridge is not { } bridge)
            throw new ServiceNotInitializedException();
        return bridge.GetFlowDefinition();
    }

    /// <summary>
    /// 获取报告输出目录的绝对路径（即工作目录下的 reports 子目录）。
    /// 插件可将 CSV、PDF 等报告文件写入此目录。
    /// </summary>
    /// <exception cref="ServiceNotInitializedException">如果 Host 尚未初始化 Service</exception>
    public static string ReportFolder()
    {
        if (_bridge is not { } bridge)
            throw new ServiceNotInitializedException();
        return bridge.GetReportFolder();
    }

    /// <summary>
    /// [Internal] 重置 Service 状态 (仅用于测试或 Host 重启)
    /// </summary>
    internal static void Reset()
    {
        _slots.Clear();
        _bridge = null;
    }
}

