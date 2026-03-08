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
