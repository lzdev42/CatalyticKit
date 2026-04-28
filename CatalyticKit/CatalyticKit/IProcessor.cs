namespace CatalyticKit;

/// <summary>
/// 处理器接口 (Processor)
/// 开发者自定义的功能扩展接口，其具体执行逻辑完全由实现者决定。
/// </summary>
public interface IProcessor : IPlugin
{
    /// <summary>
    /// 该处理器支持的任务名称。
    /// 此名称用于在低代码脚本或配置中引用此插件执行特定操作。
    /// </summary>
    string TaskName { get; }

    /// <summary>
    /// 执行处理逻辑
    /// </summary>
    /// <param name="slotIndex">调用此插件的槽位索引</param>
    /// <param name="ct">取消令牌</param>
    Task ExecuteAsync(int slotIndex, CancellationToken ct);
}
