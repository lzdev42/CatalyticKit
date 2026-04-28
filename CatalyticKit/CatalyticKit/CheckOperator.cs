namespace CatalyticKit;

/// <summary>
/// 测试步骤检查规则中使用的比较运算符
/// </summary>
public enum CheckOperator
{
    /// <summary>未知或未定义</summary>
    None,
    
    /// <summary>大于 (&gt;)</summary>
    GreaterThan,
    
    /// <summary>小于 (&lt;)</summary>
    LessThan,
    
    /// <summary>大于等于 (&gt;=)</summary>
    GreaterThanOrEqual,
    
    /// <summary>小于等于 (&lt;=)</summary>
    LessThanOrEqual,
    
    /// <summary>等于 (==)</summary>
    Equal,
    
    /// <summary>不等于 (!=)</summary>
    NotEqual
}

/// <summary>
/// 提供比较运算符与字符串之间的转换辅助方法
/// </summary>
public static class CheckOperatorExtensions
{
    public static string ToSymbol(this CheckOperator op) => op switch
    {
        CheckOperator.GreaterThan => ">",
        CheckOperator.LessThan => "<",
        CheckOperator.GreaterThanOrEqual => ">=",
        CheckOperator.LessThanOrEqual => "<=",
        CheckOperator.Equal => "==",
        CheckOperator.NotEqual => "!=",
        _ => ""
    };

    public static CheckOperator FromSymbol(string symbol) => symbol switch
    {
        ">" => CheckOperator.GreaterThan,
        "<" => CheckOperator.LessThan,
        ">=" => CheckOperator.GreaterThanOrEqual,
        "<=" => CheckOperator.LessThanOrEqual,
        "==" => CheckOperator.Equal,
        "!=" => CheckOperator.NotEqual,
        _ => CheckOperator.None
    };
}
