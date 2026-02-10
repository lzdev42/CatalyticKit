namespace CatalyticKit;

/// <summary>
/// 事件回调接口，用于 Host 通知插件 Slot 状态变化。
/// </summary>
public interface ISlotEventHandler
{
    void OnTestStarted();
    void OnTestFinished(bool passed, string? message);
    void OnStepFinished(int stepIndex, bool passed);
}
