namespace CatalyticKit;

public interface IProcessor : IPlugin
{
    string Command { get; }

    /// <summary>
    /// 执行处理逻辑
    /// </summary>
    /// <param name="slotIndex">调用此插件的槽位索引</param>
    /// <param name="ct">取消令牌</param>
    Task ExecuteAsync(int slotIndex, CancellationToken ct);
}
