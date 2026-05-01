namespace CatalyticKit;

/// <summary>
/// 当前已加载的测试流程配置（静态配置，不依赖测试执行结果）。
/// 通过 Host.GetFlowDefinition() 获取。
/// </summary>
public record TestFlow
{
    /// <summary>
    /// 所有步骤的配置，按配置顺序排列
    /// </summary>
    public IReadOnlyList<Step> Steps { get; init; } = [];
}

/// <summary>
/// 单个测试步骤的配置。
/// 表示步骤"应该是什么"，而不是"实际执行了什么"。
/// </summary>
public record Step
{
    /// <summary>步骤唯一 ID，对应低代码脚本中配置的 step_id</summary>
    public int StepId { get; init; }

    /// <summary>步骤名称，例如："电压检测"、"初始化"</summary>
    public string StepName { get; init; } = "";

    /// <summary>步骤标签，供代码逻辑 if/switch 判断，例如："voltage_check"</summary>
    public string StepLabel { get; init; } = "";

    /// <summary>
    /// 该步骤是否为测试项。false 表示辅助步骤（初始化、延时等），不计入测试报告统计
    /// </summary>
    public bool IsTestItem { get; init; }

    /// <summary>
    /// 该步骤配置的检查规则。null 表示未配置检查规则
    /// </summary>
    public CheckRule? CheckRule { get; init; }

    /// <summary>
    /// 步骤配置的参数字符串（仅扩展模式有值，插件自行解析）
    /// </summary>
    public string? Params { get; init; }
}

/// <summary>
/// 步骤检查规则（discriminated union）。
/// 使用 C# pattern matching 访问：if (rule is CheckRule.RangeRule r) { ... }
/// </summary>
public abstract record CheckRule
{
    /// <summary>范围检查规则：测量值必须在 [Min, Max] 区间内</summary>
    public record RangeRule : CheckRule
    {
        /// <summary>配置的下限值（含边界）</summary>
        public double Min { get; init; }
        /// <summary>配置的上限值（含边界）</summary>
        public double Max { get; init; }
    }

    /// <summary>阈值检查规则：测量值满足单一阈值条件（如 value >= 3.0）</summary>
    public record ThresholdRule : CheckRule
    {
        /// <summary>比较运算符</summary>
        public CheckOperator Operator { get; init; } = CheckOperator.None;
        /// <summary>配置的阈值</summary>
        public double Value { get; init; }
    }

    /// <summary>字符串包含检查规则：验证字符串是否包含指定子串</summary>
    public record ContainsRule : CheckRule
    {
        /// <summary>期望包含的子串</summary>
        public string Substring { get; init; } = "";
    }

    /// <summary>双变量比较规则：验证两个变量之间的数值关系</summary>
    public record CompareRule : CheckRule
    {
        /// <summary>比较运算符</summary>
        public CheckOperator Operator { get; init; } = CheckOperator.None;
    }

    /// <summary>未知/扩展检查规则，保留原始 JSON 以保证前向兼容</summary>
    public record UnknownRule : CheckRule
    {
        /// <summary>原始检查规则 JSON</summary>
        public string RawJson { get; init; } = "";
    }
}
