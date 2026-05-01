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
internal class SlotProxy : ISlot
{
    private readonly IHostBridge _bridge;

    public int Index { get; }

    public SlotProxy(int index, IHostBridge bridge)
    {
        Index = index;
        _bridge = bridge;
    }

    // --- 命令转发 ---

    public StartResult Start() => _bridge.SlotStart(Index);

    public StartResult Start(string sn)
    {
        SetSn(sn);
        return Start();
    }

    public void Stop() => _bridge.SlotStop(Index);

    public void Reset()
    {
        _bridge.SlotReset(Index);
        Host.NotifySlotReset(Index);
    }

    public ISlot SetSn(string sn)
    {
        ArgumentNullException.ThrowIfNull(sn);
        _bridge.SetSlotSn(Index, sn);
        return this;
    }
    public string? GetSn() => _bridge.GetSlotSn(Index);
    public string? GetVariable(string name)
        => _bridge.SlotGetVariable(Index, name);
    public TestRecord? GetTestHistory()
        => _bridge.SlotGetHistory(Index);
    public Step? GetCurrentStep()
        => _bridge.GetCurrentStep(Index);
    public void ReportPass()
        => _bridge.ReportStepResult(Index, true, null);
    public void ReportFail(string reason)
        => _bridge.ReportStepResult(Index, false, reason);

    public void SubmitValue(string value)
        => _bridge.SubmitStepValue(Index, value);

    public void Report(bool passed, string value, string? reason = null)
        => _bridge.ReportStepResultWithValue(Index, passed, value, reason);
}
