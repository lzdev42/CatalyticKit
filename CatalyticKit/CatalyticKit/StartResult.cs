namespace CatalyticKit;

/// <summary>
/// 启动操作的结果。
/// Ok = true 表示 Host 已接受请求并触发启动；
/// Ok = false 表示启动被拒绝，Reason 说明具体原因。
/// </summary>
public readonly record struct StartResult(bool Ok, string? Reason = null);
