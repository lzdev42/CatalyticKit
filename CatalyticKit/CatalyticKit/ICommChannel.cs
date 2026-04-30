namespace CatalyticKit;

/// <summary>
/// 设备连接状态
/// </summary>
public enum DeviceState
{
    Connected,
    Disconnected,
}

/// <summary>
/// 通讯插件向 Host 回传数据的通道接口。
/// 仅由 ICommunicator 实现类使用，在 ActivateAsync 时由 Host 注入。
/// </summary>
public interface ICommChannel
{
    /// <summary>
    /// 插件目录路径，用于访问插件附带的资源文件
    /// </summary>
    string PluginDirectory { get; }

    /// <summary>
    /// 获取指定协议或 ID 的通讯器
    /// </summary>
    ICommunicator? GetCommunicator(string protocolOrId);

    /// <summary>
    /// 向 Host 上报从设备收到的原始数据。
    /// Host 将把此数据提交给引擎，引擎对照检查规则判决 Pass/Fail。
    /// </summary>
    void ReportData(int slotIndex, string address, string data);

    /// <summary>
    /// 通知 Host 设备连接状态变化，Host 将根据 address 更新内部连接表并推送事件到 UI
    /// </summary>
    void NotifyState(string address, DeviceState state);
}
