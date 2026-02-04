namespace CatalyticKit;

/// <summary>
/// 标准通讯动作
/// </summary>
public enum CommAction
{
    /// <summary>建立连接</summary>
    Connect,
    
    /// <summary>断开连接</summary>
    Disconnect,
    
    /// <summary>发送数据（不等响应）</summary>
    Send,
    
    /// <summary>读取当前可用数据（可能为空或不完整）</summary>
    Read,
    
    /// <summary>发送 + 读取一次（便捷方法，适用于简单协议）</summary>
    Query,
    
    /// <summary>查询连接状态</summary>
    Status
}
