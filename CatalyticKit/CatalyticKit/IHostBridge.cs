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
    // --- 全局命令 (从插件到 Host) ---

    /// <summary>
    /// 由插件主动记录自身专属的业务日志（如通讯明文报文）
    /// Host 将负责将其专门分流到该插件的独立日志文件中
    /// </summary>
    void AddPluginLog(string pluginId, string message);

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

    // --- Slot (槽位) 命令 (从插件到 Host) ---

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
    /// 获取指定 Slot 的产品 SN
    /// </summary>
    string? SlotGetSN(int slotIndex);

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

    /// <summary>
    /// 获取当前已加载的测试流程定义（全量步骤静态配置，不依赖任何测试执行历史）。
    /// 可在测试开始前调用，用于获取所有测试项的名称和检查上下限，以生成 CSV 报告表头等。
    /// </summary>
    /// <returns>完整的流程定义；若 Engine 尚未加载流程则返回 null</returns>
    FlowDefinition? GetFlowDefinition();

    /// <summary>
    /// 获取报告输出目录的绝对路径（即工作目录下的 reports 子目录）。
    /// 插件可将 CSV、PDF 等报告文件写入此目录。
    /// 目录保证在 Host 启动时已创建。
    /// </summary>
    /// <returns>报告目录的绝对路径</returns>
    string GetReportFolder();

    // --- 步骤级命令 (从插件到 Host) ---

    /// <summary>
    /// 获取指定 Slot 当前所在步骤的上下文原数据
    /// </summary>
    StepContext? GetCurrentStep(int slotIndex);

    /// <summary>
    /// 向引擎原生接口主动提报当前步骤的执行结果
    /// </summary>
    /// <param name="slotIndex">Slot 索引</param>
    /// <param name="passed">步骤是否判定为通过</param>
    /// <param name="failReason">失败的具体原因，可通过时传 null</param>
    void ReportStepResult(int slotIndex, bool passed, string? failReason);
    
    /// <summary>
    /// 登记当前 Slot 正在运行的 Host 类型任务 (用于生命周期关联和取消)
    /// </summary>
    void RegisterRunningHostTask(int slotIndex, ulong taskId);

    // --- 事件订阅 ---

    /// <summary>
    /// 订阅指定 Slot 的事件 (Host 通过 handler 回调通知插件)
    /// </summary>
    void SubscribeSlotEvents(int slotIndex, ISlotEventHandler handler);

    /// <summary>
    /// 取消订阅指定 Slot 的事件
    /// </summary>
    void UnsubscribeSlotEvents(int slotIndex, ISlotEventHandler handler);

    // --- 错误提报 ---

    /// <summary>
    /// 上报插件错误。
    /// 当 SDK 内部捕获到插件的未处理异常时自动调用此方法。
    /// Host 应记录日志并通知 UI。
    /// </summary>
    /// <param name="pluginId">触发错误的插件标识 (或 "slot-event" 表示事件回调错误)</param>
    /// <param name="exception">异常对象</param>
    void ReportPluginError(string pluginId, Exception exception);
}
