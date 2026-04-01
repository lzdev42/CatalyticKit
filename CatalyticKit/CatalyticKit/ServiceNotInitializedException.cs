namespace CatalyticKit;

/// <summary>
/// 当尝试使用 Service 但 Host 尚未初始化 Bridge 时抛出此异常。
/// </summary>
public class ServiceNotInitializedException : InvalidOperationException
{
    public ServiceNotInitializedException() 
        : base("Service API has not been initialized. The Host application must call Service.SetBridge() during startup.") 
    { }
}
