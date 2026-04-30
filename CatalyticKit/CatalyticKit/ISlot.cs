namespace CatalyticKit;

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
    /// 设置 SN 并立即开始测试 (非阻塞)
    /// </summary>
    void Start(string sn);

    /// <summary>
    /// 停止测试 (非阻塞)
    /// </summary>
    void Stop();

    /// <summary>
    /// 设置产品 SN
    /// </summary>
    /// <returns>返回当前 Slot 实例以便于链式调用</returns>
    ISlot SetSn(string sn);

    /// <summary>
    /// 获取产品 SN
    /// </summary>
    string? GetSn();

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
    Step? GetCurrentStep();

    /// <summary>
    /// 报告当前步骤的业务逻辑为：通过 (Pass)
    /// </summary>
    void ReportPass();

    /// <summary>
    /// 报告当前步骤的业务逻辑为：失败 (Fail)
    /// </summary>
    /// <param name="reason">失败的具体原因</param>
    void ReportFail(string reason);

    /// <summary>
    /// 提交本步骤的测量值，交由引擎对照检查规则判决 Pass/Fail。
    /// 适用于业务逻辑插件自行计算出测量值，但判决规则由引擎配置决定的场景。
    /// </summary>
    /// <param name="value">测量值字符串，例如 "3.31"</param>
    void SubmitValue(string value);

    /// <summary>
    /// 提交本步骤的测量值和判决结果（插件自行判决）。
    /// 适用于业务逻辑插件自行判断 Pass/Fail，同时需要记录实测值到报告的场景。
    /// </summary>
    /// <param name="passed">步骤是否通过</param>
    /// <param name="value">实际测量值字符串，例如 "3.31"</param>
    /// <param name="reason">失败原因，通过时传 null</param>
    void Report(bool passed, string value, string? reason = null);

    
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
