namespace CatalyticKit;

/// <summary>
/// Engine 在每个步骤开始前调用 BeforeStepAsync，步骤结束后调用 AfterStepAsync。
/// 
/// 约定：
/// - 全局只允许加载一个 ICoordinator 插件，若发现多个以先到先得为准（并记录警告）
/// - BeforeStepAsync 返回 false 或抛出异常，步骤将被标记为 Fail
/// - BeforeStepAsync 超时（CancellationToken 取消）视为 false，步骤 Fail
/// - AfterStepAsync 为纯通知，不影响步骤结果
/// </summary>
public interface ICoordinator : IPlugin
{
    /// <summary>
    /// 步骤即将开始时调用。
    /// 返回 true = 允许步骤执行；返回 false = 拒绝，步骤被标记为 Fail。
    /// </summary>
    /// <param name="slotIndex">触发的槽位索引</param>
    /// <param name="stepId">步骤 ID</param>
    /// <param name="stepName">步骤名称</param>
    /// <param name="ct">取消令牌，超时时触发（超时也视为拒绝）</param>
    Task<bool> BeforeStepAsync(int slotIndex, int stepId, string stepName, CancellationToken ct);

    /// <summary>
    /// 步骤已结束时调用（纯通知，不影响结果）。
    /// </summary>
    /// <param name="slotIndex">触发的槽位索引</param>
    /// <param name="stepId">步骤 ID</param>
    /// <param name="stepName">步骤名称</param>
    /// <param name="passed">步骤是否通过</param>
    Task AfterStepAsync(int slotIndex, int stepId, string stepName, bool passed);
}
