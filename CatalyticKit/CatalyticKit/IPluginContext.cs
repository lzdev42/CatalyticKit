namespace CatalyticKit;

/// <summary>
/// 插件阅知的设备连接状态
/// </summary>
public enum PluginDeviceConnectionState
{
    Connected,
    Disconnected,
}

/// <summary>
/// 插件事件类型枚举
/// </summary>
public enum PluginEventType
{
    /// <summary>
    /// 任务执行结果 (对应引擎 SubmitResult)
    /// </summary>
    Result,

    /// <summary>
    /// 连接状态变化 (仅通知，不触发引擎判决)
    /// </summary>
    Status,
}

/// <summary>
/// 插件上下文接口
/// 在插件激活时传入，提供插件与 Catalytic 交互的能力
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// 插件目录路径
    /// 用于访问插件附带的资源文件
    /// </summary>
    string PluginDirectory { get; }

    /// <summary>
    /// 获取指定协议或 ID 的通讯器
    /// 用于业务插件调用底层通讯插件
    /// </summary>
    /// <param name="protocolOrId">协议名（如 "serial"）或插件 ID</param>
    /// <returns>通讯器实例，未找到返回 null</returns>
    ICommunicator? GetCommunicator(string protocolOrId);

    /// <summary>
    /// 向 Host 推送事件或任务结果
    /// 当 eventType = Result 时，Host 将此数据作为当前步骤的结果提交给引擎进行判决
    /// </summary>
    /// <param name="slotIndex">关联的槽位索引</param>
    /// <param name="address">关联的设备地址</param>
    /// <param name="eventType">事件类型</param>
    /// <param name="data">数据内容</param>
    void PushEvent(int slotIndex, string address, PluginEventType eventType, string data);

    /// <summary>
    /// 向 Host 通知设备连接状态变化
    /// Host 将根据 address 更新内部连接表并推送事件到 UI
    /// </summary>
    /// <param name="address">设备地址 (IP:port / COM3 / VISA 地址等)</param>
    /// <param name="state">连接状态</param>
    void NotifyConnectionStateChanged(string address, PluginDeviceConnectionState state);
}
