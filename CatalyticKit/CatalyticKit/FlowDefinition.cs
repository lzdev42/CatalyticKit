namespace CatalyticKit;

/// <summary>
/// 当前已加载的测试流程定义（静态配置，不依赖任何测试执行结果）。
/// 通过 <see cref="Service.GetFlowDefinition"/> 获取。
/// </summary>
public record FlowDefinition
{
    /// <summary>
    /// 所有步骤的定义，按配置顺序排列。
    /// 包含所有步骤，无论是否为测试项，无论是否已执行。
    /// </summary>
    public IReadOnlyList<StepDefinition> Steps { get; init; } = [];
}

/// <summary>
/// 单个测试步骤的配置定义。
/// 表示一个步骤"应该是什么"，而不是"实际执行了什么"。
/// </summary>
public record StepDefinition
{
    /// <summary>
    /// 步骤唯一 ID，对应低代码脚本中配置的 step_id。
    /// </summary>
    public int StepId { get; init; }

    /// <summary>
    /// 步骤名称，对应低代码脚本中配置的 step_name。
    /// 例如："电压检测"、"初始化"。
    /// </summary>
    public string StepName { get; init; } = "";

    /// <summary>
    /// 步骤标签（Label），供代码逻辑进行 if/switch 判断。
    /// 例如："voltage_check"、"init"。
    /// </summary>
    public string StepLabel { get; init; } = "";

    /// <summary>
    /// 该步骤是否为测试项。
    /// false 表示辅助步骤（如初始化、延时），通常不计入测试报告统计。
    /// 插件可使用此字段过滤，只对 IsTestItem == true 的步骤生成 CSV 行。
    /// </summary>
    public bool IsTestItem { get; init; }

    /// <summary>
    /// 该步骤配置的检查规则定义（静态上下限等）。
    /// 为 null 表示该步骤未配置检查规则。
    /// </summary>
    public CheckRuleDefinition? CheckRule { get; init; }
}

/// <summary>
/// 步骤检查规则的静态配置定义（discriminated union）。
/// 描述"配置了什么规则"，而非"检查结果如何"。
/// 使用 C# pattern matching 访问：<c>if (rule is CheckRuleDefinition.RangeRule r) { ... }</c>
/// </summary>
public abstract record CheckRuleDefinition
{
    /// <summary>
    /// 范围检查规则：验证测量值是否在 [Min, Max] 区间内。
    /// </summary>
    public record RangeRule : CheckRuleDefinition
    {
        /// <summary>配置的下限值（含边界）</summary>
        public double Min { get; init; }

        /// <summary>配置的上限值（含边界）</summary>
        public double Max { get; init; }
    }

    /// <summary>
    /// 阈值检查规则：验证测量值是否满足单一阈值条件（如 value >= 3.0）。
    /// </summary>
    public record ThresholdRule : CheckRuleDefinition
    {
        /// <summary>比较运算符，如 ">"、"<"、">="、"<="、"=="、"!="</summary>
        public string Operator { get; init; } = "";

        /// <summary>配置的阈值</summary>
        public double ThresholdValue { get; init; }
    }

    /// <summary>
    /// 字符串包含检查规则：验证字符串是否包含指定子串。
    /// </summary>
    public record ContainsRule : CheckRuleDefinition
    {
        /// <summary>期望包含的子串</summary>
        public string Substring { get; init; } = "";
    }

    /// <summary>
    /// 双变量比较规则：验证两个变量之间的数值关系。
    /// </summary>
    public record CompareRule : CheckRuleDefinition
    {
        /// <summary>比较运算符，如 ">"、"<"、">="、"<="、"=="、"!="</summary>
        public string Operator { get; init; } = "";
    }

    /// <summary>
    /// 未知/扩展检查模板，保留原始 JSON 以保证前向兼容。
    /// </summary>
    public record UnknownRule : CheckRuleDefinition
    {
        /// <summary>原始检查规则 JSON</summary>
        public string RawJson { get; init; } = "";
    }
}
