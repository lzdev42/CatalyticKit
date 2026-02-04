namespace CatalyticKit;

/// <summary>
/// 标准插件事件常量
/// </summary>
public static class PluginEvents
{
    /// <summary>
    /// 设备断开连接
    /// Payload: UTF8 编码的设备 Address
    /// </summary>
    public const string DeviceDisconnected = "DeviceDisconnected";

    /// <summary>
    /// 设备数据推送
    /// Payload: 任意二进制数据 (建议结合 FetchData 使用)
    /// </summary>
    public const string DeviceData = "DeviceData";
}
