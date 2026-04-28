namespace CatalyticKit;

/// <summary>
/// 表示当前执行步骤的上下文信息
/// 包含步骤的标识、名称、标签以及配置的参数字符串
/// </summary>
public class StepContext
{
    /// <summary>步骤内部数字 ID</summary>
    public int StepId { get; set; }
    
    /// <summary>步骤展示名称（如："机器回原点"）</summary>
    public string StepName { get; set; } = "";
    
    /// <summary>步骤标识符/标签（如："WaitReady"），供代码侧进行 if/switch 判定</summary>
    public string StepLabel { get; set; } = "";
    
    /// <summary>
    /// 步骤配置的参数字符串（仅扩展模式有效）。
    /// 内容由 UI 配置决定，可能是普通字符串、CSV、JSON 等，插件自行解析。
    /// 标准模式（EngineControlled）此字段为 null。
    /// </summary>
    public string? Params { get; set; }
}
