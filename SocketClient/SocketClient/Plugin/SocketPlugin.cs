using CatalyticKit;

namespace SocketClient.Plugin;

/// <summary>
/// 插件入口类
/// </summary>
public class SocketPlugin : IPlugin
{
    private readonly SocketCommunicator _communicator = new();

    public string Id => "catalytic.socket-client";

    public async Task ActivateAsync(IPluginContext context)
    {
        // 1. 激活通讯器
        await _communicator.ActivateAsync(context);
        
        // 2. 注册通讯器
        // 这是一个 ICommunicator 实现，所以 Host 会自动扫描并注册它。
        // 但如果需要手动显式注册（例如 PluginManager 提供了注册接口），可以在这里做。
        // 根据 Plugin Developer Guide，只要实现了 ICommunicator 且被 Host 加载，就会自动注册。
        // 所以这里主要做一些初始化日志。
        
        context.Log(-1, LogLevel.Info, $"插件 {Id} 已激活。");
    }

    public async Task DeactivateAsync()
    {
        await _communicator.DeactivateAsync();
    }
}
