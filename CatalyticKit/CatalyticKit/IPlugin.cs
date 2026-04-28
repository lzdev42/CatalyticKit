namespace CatalyticKit;

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
    /// 在此进行初始化工作（建立连接、注册回调、启动后台线程等）
    /// </summary>
    /// <param name="context">插件上下文</param>
    Task ActivateAsync(IPluginContext context);

    /// <summary>
    /// 插件停用时调用
    /// 在此进行清理工作（关闭连接、停止后台线程等）
    /// </summary>
    Task DeactivateAsync();
}
