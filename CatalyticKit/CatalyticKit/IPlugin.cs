namespace CatalyticKit;

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
    /// 通过 Catalytic 的日志系统输出日志
    /// </summary>
    /// <param name="level">日志级别</param>
    /// <param name="message">日志内容</param>
    void Log(LogLevel level, string message);

    /// <summary>
    /// 获取指定协议或 ID 的通讯器
    /// 用于业务插件调用底层通讯插件
    /// </summary>
    /// <param name="protocolOrId">协议名（如 "serial"）或插件 ID</param>
    /// <returns>通讯器实例，未找到返回 null</returns>
    ICommunicator? GetCommunicator(string protocolOrId);

    /// <summary>
    /// 推送事件到 Catalytic
    /// 用于设备主动推送数据（如 CAN 帧监控、设备报警）
    /// </summary>
    /// <param name="eventType">事件类型</param>
    /// <param name="data">事件数据</param>
    void PushEvent(string eventType, byte[] data);

    /// <summary>
    /// 获取设备缓冲的数据 (用于 Polling 模式)
    /// </summary>
    /// <param name="deviceId">设备 ID (或 Address)</param>
    /// <returns>被缓冲的数据，如果没有数据返回空数组</returns>
    byte[] GetDeviceData(string deviceId);
}

/// <summary>
/// 插件基础接口
/// 所有插件必须实现此接口
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// 插件唯一标识
    /// 格式建议: "公司.插件名"，例如 "acme.scpi-driver"
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// 插件激活时调用
    /// 在此进行初始化工作
    /// </summary>
    /// <param name="context">插件上下文</param>
    Task ActivateAsync(IPluginContext context);
    
    /// <summary>
    /// 插件停用时调用
    /// 在此进行清理工作
    /// </summary>
    Task DeactivateAsync();
}

/// <summary>
/// 通讯器接口 (Communicator)
/// 用于处理设备通信协议 (原 ProtocolDriver)
/// </summary>
public interface ICommunicator : IPlugin
{
    /// <summary>
    /// 该通讯器支持的协议名称
    /// 例如 "scpi"、"modbus"
    /// </summary>
    string Protocol { get; }
    
    /// <summary>
    /// 执行通讯动作
    /// </summary>
    /// <param name="address">设备地址，如 "COM3" 或 "192.168.1.100:5025"</param>
    /// <param name="action">操作类型: "send"、"query"、"wait"</param>
    /// <param name="payload">命令数据</param>
    /// <param name="timeoutMs">超时时间（毫秒）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>设备响应数据</returns>
    Task<byte[]> ExecuteAsync(
        string address, 
        string action, 
        byte[] payload, 
        int timeoutMs, 
        CancellationToken ct);
    
    /// <summary>
    /// 执行通讯动作（带高级选项）
    /// </summary>
    /// <param name="address">设备地址</param>
    /// <param name="action">操作类型</param>
    /// <param name="payload">命令数据</param>
    /// <param name="options">执行选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>设备响应数据</returns>
    Task<byte[]> ExecuteAsync(
        string address,
        string action,
        byte[] payload,
        ExecuteOptions options,
        CancellationToken ct);
}

/// <summary>
/// 通讯执行选项
/// </summary>
public class ExecuteOptions
{
    /// <summary>
    /// 超时时间（毫秒）
    /// </summary>
    public int TimeoutMs { get; set; }
    
    /// <summary>
    /// 响应结束符（如 "\n"）
    /// 如果指定，通讯器收到该字符后返回响应
    /// 如果不指定，通讯器立即返回收到的数据
    /// </summary>
    public string? Terminator { get; set; }
}

/// <summary>
/// 处理器接口 (Processor)
/// 用于处理复杂的业务逻辑任务 (原 HostTaskHandler)
/// </summary>
public interface IProcessor : IPlugin
{
    /// <summary>
    /// 该处理器支持的任务能力名称
    /// 例如 "burn_firmware"、"calibrate"
    /// </summary>
    string TaskName { get; }
    
    /// <summary>
    /// 执行处理逻辑
    /// </summary>
    /// <param name="slotIndex">调用此插件的槽位索引</param>
    /// <param name="ct">取消令牌</param>
    Task ExecuteAsync(int slotIndex, CancellationToken ct);
}
