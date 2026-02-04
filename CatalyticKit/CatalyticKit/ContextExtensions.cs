namespace CatalyticKit;

public static class ContextExtensions
{
    /// <summary>
    /// 推送设备数据 (便捷方法)
    /// 自动将 Address 编码进 EventType: "DeviceData:{address}"
    /// </summary>
    public static void PushDeviceData(this IPluginContext context, string address, byte[] data)
    {
        context.PushEvent($"{PluginEvents.DeviceData}:{address}", data);
    }
}
