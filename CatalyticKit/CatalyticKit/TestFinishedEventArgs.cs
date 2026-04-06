namespace CatalyticKit;

/// <summary>
/// 单个 Slot 测试完成时的事件参数
/// </summary>
public class TestFinishedEventArgs
{
    /// <summary>
    /// 完成的 Slot 索引 (0-based)
    /// </summary>
    public int SlotIndex { get; init; }

    /// <summary>
    /// 该 Slot 是否通过
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// 错误信息（通过时为 null）
    /// </summary>
    public string? ErrorMessage { get; init; }
}
