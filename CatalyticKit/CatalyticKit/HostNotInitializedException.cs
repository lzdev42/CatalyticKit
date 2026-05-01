namespace CatalyticKit;

/// <summary>
/// 当尝试使用 Host 但 Host 尚未初始化 Bridge 时抛出此异常。
/// </summary>
public class HostNotInitializedException : InvalidOperationException
{
    public HostNotInitializedException() 
        : base("Host API has not been initialized. The Host application must call Host.SetBridge() during startup.") 
    { }
}
