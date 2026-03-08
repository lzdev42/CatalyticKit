namespace CatalyticKit;

/// <summary>
/// 一次完整测试的记录，对应一个 Slot 完成一次 Run 后的全量结果数据。
/// 通过 <see cref="ISlot.GetTestHistory"/> 获取，建议在 <see cref="ISlot.TestFinished"/> 回调中调用。
/// </summary>
public record TestRecord
{
    /// <summary>
    /// 产品序列号（SN）。
    /// 若本次测试前未通过 <see cref="ISlot.SetSN"/> 设置 SN，则为 null。
    /// </summary>
    public string? Sn { get; init; }

    /// <summary>
    /// 所有步骤的执行结果，按执行顺序排列。
    /// 包含已执行、跳过、超时的所有步骤；未执行的步骤不在此列表中。
    /// </summary>
    public IReadOnlyList<StepRecord> Steps { get; init; } = [];
}

/// <summary>
/// 单个测试步骤的执行结果。
/// </summary>
public record StepRecord
{
    /// <summary>
    /// 步骤唯一 ID，对应低代码脚本中配置的 step_id。
    /// 可用于与步骤定义（名称、配置）进行关联查询。
    /// </summary>
    public int StepId { get; init; }

    /// <summary>
    /// 步骤名称，对应低代码脚本中配置的 step_name。
    /// 例如："电压检测"、"初始化"。
    /// </summary>
    public string StepName { get; init; } = "";

    /// <summary>
    /// 该步骤本次执行是否通过（综合检查规则的最终结果）。
    /// 若步骤无检查规则，则执行成功即视为通过。
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// 是否为测试项。
    /// false 表示辅助步骤（如初始化、延时、计算中间量），通常不应出现在测试报告的统计行中。
    /// 插件可使用此字段过滤，只对 IsTestItem == true 的步骤生成 CSV 行。
    /// </summary>
    public bool IsTestItem { get; init; }

    /// <summary>
    /// 步骤执行耗时（毫秒）。
    /// 从引擎开始执行该步骤到接收到设备响应并完成检查的总时长。
    /// </summary>
    public uint ElapsedMs { get; init; }

    /// <summary>
    /// 结果摘要（人类可读字符串），例如 "3.3100 (>=3.0 &amp;&amp; &lt;=3.5) → PASS"。
    /// 可直接用于日志展示，不适合机器解析。
    /// </summary>
    public string? ResultSummary { get; init; }

    /// <summary>
    /// 错误信息。仅在步骤执行出错或超时时存在，正常执行时为 null。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 从设备响应中解析出的最终值（JSON 字符串）。
    /// 该值的结构由开发者在低代码脚本中配置的解析规则（正则/数字提取）决定，
    /// SDK 不对其结构做假设，插件若需使用请自行解析。
    /// 例如：数值步骤为 <c>"3.31"</c>，字符串步骤为 <c>"\"OK\""</c>。
    /// </summary>
    public string? FinalValue { get; init; }

    /// <summary>
    /// 检查结果详情。为 null 表示该步骤未配置检查规则（无检查步骤视为执行成功）。
    /// 使用 C# pattern matching 按类型访问：
    /// <code>
    /// if (step.Check is CheckDetail.RangeCheck r)
    ///     Console.WriteLine($"Min={r.Min}, Max={r.Max}, Actual={r.Actual}");
    /// </code>
    /// </summary>
    public CheckDetail? Check { get; init; }

    /// <summary>
    /// 该步骤从设备响应中提取并存入变量池的变量快照。
    /// Key 为变量名（由开发者在低代码脚本中命名，例如 "voltage"、"current"）。
    /// Value 为 JSON 字符串，值类型由解析规则决定，可能是数字字符串或普通字符串。
    /// </summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>
/// 检查结果详情的 discriminated union。
/// 按检查模板（template）类型分为不同子类，每种子类型含完全强类型的字段。
/// 使用 C# pattern matching 访问：<c>if (check is CheckDetail.RangeCheck r) { ... }</c>
/// </summary>
public abstract record CheckDetail
{
    /// <summary>该步骤检查是否通过</summary>
    public abstract bool Passed { get; init; }

    /// <summary>
    /// 范围检查（range_check）：验证变量值是否在 [Min, Max] 区间内。
    /// 对应低代码脚本中配置的 min / max 参数。
    /// </summary>
    public record RangeCheck : CheckDetail
    {
        /// <summary>检查是否通过</summary>
        public override bool Passed { get; init; }

        /// <summary>配置的下限值（含边界）</summary>
        public double Min { get; init; }

        /// <summary>配置的上限值（含边界）</summary>
        public double Max { get; init; }

        /// <summary>步骤实际测量值</summary>
        public double Actual { get; init; }
    }

    /// <summary>
    /// 阈值检查（threshold）：验证变量值与单一阈值的关系，如 value &gt;= 3.0。
    /// 对应低代码脚本中配置的 operator / value 参数。
    /// </summary>
    public record Threshold : CheckDetail
    {
        /// <summary>检查是否通过</summary>
        public override bool Passed { get; init; }

        /// <summary>比较运算符，如 "&gt;"、"&lt;"、"&gt;="、"&lt;="、"=="、"!="</summary>
        public string Operator { get; init; } = "";

        /// <summary>配置的阈值</summary>
        public double ThresholdValue { get; init; }

        /// <summary>步骤实际测量值</summary>
        public double Actual { get; init; }
    }

    /// <summary>
    /// 字符串包含检查（contains）：验证字符串变量是否包含指定子串。
    /// 对应低代码脚本中配置的 substring 参数。
    /// </summary>
    public record Contains : CheckDetail
    {
        /// <summary>检查是否通过</summary>
        public override bool Passed { get; init; }

        /// <summary>配置的期望子串</summary>
        public string Substring { get; init; } = "";

        /// <summary>实际字符串值</summary>
        public string Actual { get; init; } = "";
    }

    /// <summary>
    /// 双变量比较（compare）：验证两个变量之间的数值关系，如 voltage_a &gt;= voltage_b。
    /// 对应低代码脚本中配置的 var_a / operator / var_b 参数。
    /// </summary>
    public record Compare : CheckDetail
    {
        /// <summary>检查是否通过</summary>
        public override bool Passed { get; init; }

        /// <summary>比较运算符，如 "&gt;"、"&lt;"、"&gt;="、"&lt;="、"=="、"!="</summary>
        public string Operator { get; init; } = "";

        /// <summary>变量 A 的实际值</summary>
        public double ActualA { get; init; }

        /// <summary>变量 B 的实际值</summary>
        public double ActualB { get; init; }
    }

    /// <summary>
    /// 未知/扩展检查模板。
    /// 当 Engine 返回的 template 字段不在上述已知类型中时使用，以保证前向兼容。
    /// 插件可访问 <see cref="RawJson"/> 自行解析。
    /// </summary>
    public record Unknown : CheckDetail
    {
        /// <summary>检查是否通过</summary>
        public override bool Passed { get; init; }

        /// <summary>
        /// 原始 check_result JSON 字符串，包含 template / params / actual 等字段。
        /// </summary>
        public string RawJson { get; init; } = "";
    }
}
