namespace CatalyticKit;

/// <summary>
/// 日志级别枚举
/// 用于插件向 Host 输出日志
/// </summary>
public enum LogLevel
{
    /// <summary>调试信息</summary>
    Debug,
    
    /// <summary>一般信息</summary>
    Info,
    
    /// <summary>警告信息</summary>
    Warning,
    
    /// <summary>错误信息</summary>
    Error
}
