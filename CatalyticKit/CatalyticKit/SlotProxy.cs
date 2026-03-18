namespace CatalyticKit;

/// <summary>
/// ISlot 的内部实现。
/// 命令转发给 IHostBridge，事件由 Host 通过 ISlotEventHandler 回调触发。
/// 
/// 线程安全：
/// - 事件订阅/取消订阅：lock 锁保护
/// - 事件触发：快照模式（锁内取委托快照，锁外执行，避免死锁）
/// - 事件回调异常：try-catch 保护，防止插件回调异常影响 Host 稳定性
/// </summary>
internal class SlotProxy : ISlot, ISlotEventHandler
{
    private readonly IHostBridge _bridge;
    private readonly object _eventLock = new();

    // 私有委托字段 (由 lock 锁保护)
    private Action? _testStarted;
    private Action<bool, string?>? _testFinished;
    private Action<int, bool>? _stepFinished;

    public int Index { get; }

    public SlotProxy(int index, IHostBridge bridge)
    {
        Index = index;
        _bridge = bridge;
        _bridge.SubscribeSlotEvents(index, this);
    }

    // --- 事件 (线程安全的 add/remove) ---

    public event Action? TestStarted
    {
        add { lock (_eventLock) _testStarted += value; }
        remove { lock (_eventLock) _testStarted -= value; }
    }

    public event Action<bool, string?>? TestFinished
    {
        add { lock (_eventLock) _testFinished += value; }
        remove { lock (_eventLock) _testFinished -= value; }
    }

    public event Action<int, bool>? StepFinished
    {
        add { lock (_eventLock) _stepFinished += value; }
        remove { lock (_eventLock) _stepFinished -= value; }
    }

    // --- 命令转发 ---

    public void Start() => _bridge.SlotStart(Index);
    public void Stop() => _bridge.SlotStop(Index);
    public void SetSN(string sn) => _bridge.SlotSetSN(Index, sn);
    public string? GetSN() => _bridge.SlotGetSN(Index);
    public void SetVariable(string name, string jsonValue)
        => _bridge.SlotSetVariable(Index, name, jsonValue);
    public string? GetVariable(string name)
        => _bridge.SlotGetVariable(Index, name);
    public TestRecord? GetTestHistory()
        => _bridge.SlotGetHistory(Index);
    public StepContext? GetCurrentStep()
        => _bridge.GetCurrentStep(Index);
    public void ReportPass()
        => _bridge.ReportStepResult(Index, true, null);
    public void ReportFail(string reason)
        => _bridge.ReportStepResult(Index, false, reason);

    // --- 事件分发 (由 Host 调用, 快照机制 + try-catch 保护) ---

    public void OnTestStarted()
    {
        Action? handler;
        lock (_eventLock) handler = _testStarted;
        if (handler == null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action)d)(); }
            catch (Exception ex) { _bridge.ReportPluginError($"slot-{Index}", ex); }
        }
    }

    public void OnTestFinished(bool passed, string? errorMessage)
    {
        Action<bool, string?>? handler;
        lock (_eventLock) handler = _testFinished;
        if (handler == null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action<bool, string?>)d)(passed, errorMessage); }
            catch (Exception ex) { _bridge.ReportPluginError($"slot-{Index}", ex); }
        }
    }

    public void OnStepFinished(int stepIndex, bool passed)
    {
        Action<int, bool>? handler;
        lock (_eventLock) handler = _stepFinished;
        if (handler == null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action<int, bool>)d)(stepIndex, passed); }
            catch (Exception ex) { _bridge.ReportPluginError($"slot-{Index}", ex); }
        }
    }
}
