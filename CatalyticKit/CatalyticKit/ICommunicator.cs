namespace CatalyticKit;

/// <summary>
/// 通讯器接口 (Communicator)
/// 用于处理设备通信协议
/// </summary>
public interface ICommunicator : IPlugin
{
    /// <summary>
    /// 该通讯器支持的协议名称
    /// 例如 "scpi"、"modbus"
    /// </summary>
    string Protocol { get; }

    /// <summary>
    /// 执行通讯任务
    /// 插件应在此方法内部完成通讯动作，并通过 ICommChannel.ReportData 上报结果
    /// 禁止通过 return 返回结果（返回值已无意义）
    /// </summary>
    /// <param name="slotIndex">槽位索引</param>
    /// <param name="address">设备地址</param>
    /// <param name="action">操作类型</param>
    /// <param name="payload">命令数据</param>
    /// <param name="options">执行选项</param>
    /// <param name="ct">取消令牌</param>
    Task Execute(
        int slotIndex,
        string address,
        CommAction action,
        string payload,
        CommOptions options,
        CancellationToken ct);
}

/// <summary>
/// 通讯执行选项
/// </summary>
public class CommOptions
{
    /// <summary>
    /// 超时时间（毫秒）
    /// </summary>
    public int TimeoutMs { get; set; }

    /// <summary>
    /// 发送的命令的终止符（如 "\n"）
    /// 如果指定，通讯器收到该字符后返回响应
    /// 如果不指定，通讯器立即返回收到的数据
    /// </summary>
    public string? CommandTerminator { get; set; }

    /// <summary>
    /// 响应数据的结束符（如 "\n"）
    /// 如果指定，通讯器收到该字符后返回响应
    /// 如果不指定，通讯器立即返回收到的数据
    /// </summary>
    public string? ResponseTerminator { get; set; }

    /// <summary>
    /// 是否为共享设备类型
    /// </summary>
    public bool IsShared { get; set; }
}
