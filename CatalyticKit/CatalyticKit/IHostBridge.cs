namespace CatalyticKit;

/// <summary>
/// Host 必须实现此接口并通过 Service.SetBridge() 注入。
/// 所有方法必须是非阻塞的 (Fire-and-Forget)。
/// 
/// 线程安全约定：
/// 此接口的所有方法可能被任意线程并发调用，实现方必须保证线程安全。
/// </summary>
public interface IHostBridge
{
    // --- Global Commands (Plugin -> Host) ---

    /// <summary>
    /// 获取当前 Host 配置的总 Slot 数量
    /// </summary>
    int GetSlotCount();

    /// <summary>
    /// 启动所有 Slot 的测试 (非阻塞)
    /// </summary>
    void StartAll();

    /// <summary>
    /// 停止所有 Slot 的测试 (非阻塞)
    /// </summary>
    void StopAll();

    // --- Slot Commands (Plugin -> Host) ---

    /// <summary>
    /// 启动指定 Slot 的测试 (非阻塞)
    /// </summary>
    void SlotStart(int slotIndex);

    /// <summary>
    /// 停止指定 Slot 的测试 (非阻塞)
    /// </summary>
    void SlotStop(int slotIndex);

    /// <summary>
    /// 设置指定 Slot 的产品 SN
    /// </summary>
    void SlotSetSN(int slotIndex, string sn);

    /// <summary>
    /// 设置指定 Slot 的流程变量
    /// </summary>
    void SlotSetVariable(int slotIndex, string name, string jsonValue);

    /// <summary>
    /// 获取指定 Slot 的流程变量
    /// </summary>
    string? SlotGetVariable(int slotIndex, string name);

    /// <summary>
    /// 获取指定 Slot 的完整测试历史记录。
    /// 建议在 <see cref="ISlotEventHandler.OnTestFinished"/> 回调触发后调用，此时数据最完整。
    /// </summary>
    /// <param name="slotIndex">Slot 索引（0-based）</param>
    /// <returns>完整的测试记录；若数据不可用或发生错误则返回 null</returns>
    TestRecord? SlotGetHistory(int slotIndex);

    // --- Event Subscription ---

    /// <summary>
    /// 订阅指定 Slot 的事件 (Host 通过 handler 回调通知插件)
    /// </summary>
    void SubscribeSlotEvents(int slotIndex, ISlotEventHandler handler);

    /// <summary>
    /// 取消订阅指定 Slot 的事件
    /// </summary>
    void UnsubscribeSlotEvents(int slotIndex, ISlotEventHandler handler);

    // --- Error Reporting ---

    /// <summary>
    /// 上报插件错误。
    /// 当 SDK 内部捕获到插件的未处理异常时自动调用此方法。
    /// Host 应记录日志并通知 UI。
    /// </summary>
    /// <param name="pluginId">触发错误的插件标识 (或 "slot-event" 表示事件回调错误)</param>
    /// <param name="exception">异常对象</param>
    void ReportPluginError(string pluginId, Exception exception);
}
