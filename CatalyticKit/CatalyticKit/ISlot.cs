using System.Collections.Generic;

namespace CatalyticKit;

/// <summary>
/// 表示当前执行步骤的上下文信息
/// 包含步骤的标识、名称、标签以及配置的参数字符串
/// </summary>
public class StepContext
{
    /// <summary>步骤内部数字 ID</summary>
    public int StepId { get; set; }
    
    /// <summary>步骤展示名称（如："机器回原点"）</summary>
    public string StepName { get; set; } = "";
    
    /// <summary>步骤标识符/标签（如："WaitReady"），供代码侧进行 if/switch 判定</summary>
    public string StepLabel { get; set; } = "";
    
    /// <summary>
    /// 步骤配置的参数字符串（仅扩展模式有效）。
    /// 内容由 UI 配置决定，可能是普通字符串、CSV、JSON 等，插件自行解析。
    /// 标准模式（EngineControlled）此字段为 null。
    /// </summary>
    public string? Params { get; set; }
}

/// <summary>
/// 插件使用的 Slot 操作接口。
/// 提供对特定 Slot 的控制和状态监听能力。
/// </summary>
public interface ISlot
{
    /// <summary>
    /// Slot 索引 (0-based)
    /// </summary>
    int Index { get; }
    
    // --- Commands (插件 -> Host) ---

    /// <summary>
    /// 开始测试 (非阻塞)
    /// </summary>
    void Start();

    /// <summary>
    /// 停止测试 (非阻塞)
    /// </summary>
    void Stop();

    /// <summary>
    /// 设置产品 SN
    /// </summary>
    void SetSN(string sn);

    /// <summary>
    /// 设置流程变量 (Context Variable)
    /// </summary>
    /// <param name="name">变量名</param>
    /// <param name="jsonValue">变量值 (JSON 字符串)</param>
    void SetVariable(string name, string jsonValue);

    /// <summary>
    /// 获取流程变量
    /// </summary>
    /// <param name="name">变量名</param>
    /// <returns>变量值 (JSON 字符串)，不存在返回 null</returns>
    string? GetVariable(string name);

    /// <summary>
    /// 获取本次（或上次）测试的完整步骤记录。
    /// <para><b>建议在 <see cref="TestFinished"/> 事件的回调中调用</b>，此时引擎已保存完整的运行结果。</para>
    /// </summary>
    /// <returns>
    /// 当前 Slot 的完整测试记录，含 SN 和每个步骤的测试结果。
    /// 若数据不可用或发生错误则返回 null。
    /// </returns>
    TestRecord? GetTestHistory();

    /// <summary>
    /// 获取当前正在执行的步骤的完整上下文（已将 JSON 跨语言参数解析为强类型/字典）
    /// </summary>
    StepContext? GetCurrentStep();

    /// <summary>
    /// 报告当前步骤的业务逻辑为：通过 (Pass)
    /// </summary>
    void ReportPass();

    /// <summary>
    /// 报告当前步骤的业务逻辑为：失败 (Fail)
    /// </summary>
    /// <param name="reason">失败的具体原因</param>
    void ReportFail(string reason);
    
    // --- Events (Host -> 插件) ---

    /// <summary>
    /// 当测试开始时触发
    /// </summary>
    event Action? TestStarted;

    /// <summary>
    /// 当测试结束时触发 (passed, message)
    /// </summary>
    event Action<bool, string?>? TestFinished;

    /// <summary>
    /// 当单个步骤结束时触发 (stepIndex, passed)
    /// </summary>
    event Action<int, bool>? StepFinished;
}
