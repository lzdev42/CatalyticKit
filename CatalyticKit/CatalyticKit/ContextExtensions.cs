namespace CatalyticKit;

public static class ContextExtensions
{
    /// <summary>
    /// 推送任务结果 (便捷方法)
    /// 等价于 PushEvent(slotIndex, address, PluginEventType.Result, data)
    /// 调用后 Host 将立即将数据提交给引擎进行判决
    /// </summary>
    public static void PushResult(this IPluginContext context, int slotIndex, string address, string data)
    {
        context.PushEvent(slotIndex, address, PluginEventType.Result, data);
    }
}
