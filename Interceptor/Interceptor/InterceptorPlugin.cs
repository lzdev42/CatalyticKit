using CatalyticKit;

namespace Interceptor;

/// <summary>
/// 拦截器插件模板
/// 用于在测试步骤执行前后进行自定义逻辑处理
/// </summary>
public class InterceptorPlugin : IInterceptor
{
    private IPluginContext? _context;

    public string Id => "catalytic.interceptor";

    /// <summary>
    /// 插件激活时调用
    /// </summary>
    public Task ActivateAsync(IPluginContext context)
    {
        _context = context;
        _context.Log(-1, LogLevel.Info, "拦截器插件已激活");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 插件停用时调用
    /// </summary>
    public Task DeactivateAsync()
    {
        _context?.Log(-1, LogLevel.Info, "拦截器插件已停用");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 步骤执行前的拦截逻辑
    /// </summary>
    /// <param name="slotIndex">Slot 索引</param>
    /// <param name="stepId">步骤 ID</param>
    /// <param name="stepName">步骤名称</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>返回 true 允许执行，返回 false 则跳过该步并将结果标记为 Failed</returns>
    public Task<bool> BeforeStepAsync(int slotIndex, int stepId, string stepName, CancellationToken ct)
    {
        _context?.Log(slotIndex, LogLevel.Debug, $"[Slot {slotIndex}] 步骤执行前: {stepId} - {stepName}");

        // TODO: 在此添加自定义拦截逻辑
        // 例如:
        // - 检查安全门是否关闭
        // - 等待外部PLC信号
        // - 根据步骤名称过滤特定步骤
        // - 检查设备状态

        return Task.FromResult(true); // 默认允许所有步骤执行
    }

    /// <summary>
    /// 步骤执行后的通知（纯通知，不影响结果）
    /// </summary>
    /// <param name="slotIndex">Slot 索引</param>
    /// <param name="stepId">步骤 ID</param>
    /// <param name="stepName">步骤名称</param>
    /// <param name="passed">步骤是否通过</param>
    public Task AfterStepAsync(int slotIndex, int stepId, string stepName, bool passed)
    {
        _context?.Log(slotIndex, LogLevel.Debug, $"[Slot {slotIndex}] 步骤执行后: {stepId} - {stepName} => {(passed ? "PASS" : "FAIL")}");

        // TODO: 在此添加步骤完成后的处理逻辑
        // 例如:
        // - 记录审计日志
        // - 上报测试进度到MES系统
        // - 触发外部设备动作

        return Task.CompletedTask;
    }
}