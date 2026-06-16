namespace TestServer.Core;

/// <summary>
/// 不匹配日志记录器 — 只记录校验不通过的情况到 mismatch.log
/// </summary>
public class MismatchLogger
{
    private readonly string _logPath;
    private readonly object _lock = new();

    public MismatchLogger(string logPath = "mismatch.log")
    {
        _logPath = logPath;
    }

    /// <summary>
    /// 记录一轮校验不匹配的结果
    /// </summary>
    public void LogMismatch(
        int roundId,
        HashSet<int> startSlots,
        HashSet<int> passSlots,
        HashSet<int> failSlots,
        HashSet<int> missingSlots,
        HashSet<int> extraSlots,
        HashSet<int> duplicateSlots)
    {
        lock (_lock)
        {
            try
            {
                using var writer = new StreamWriter(_logPath, append: true);
                writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Round-{roundId:D3}] MISMATCH");
                writer.WriteLine($"  Start:     [{string.Join(", ", startSlots.OrderBy(s => s))}]");
                writer.WriteLine($"  Pass:      [{string.Join(", ", passSlots.OrderBy(s => s))}]");
                writer.WriteLine($"  Fail:      [{string.Join(", ", failSlots.OrderBy(s => s))}]");
                writer.WriteLine($"  Missing:   [{string.Join(", ", missingSlots.OrderBy(s => s))}]");
                writer.WriteLine($"  Extra:     [{string.Join(", ", extraSlots.OrderBy(s => s))}]");
                writer.WriteLine($"  Duplicate: [{string.Join(", ", duplicateSlots.OrderBy(s => s))}]");
                writer.WriteLine();
            }
            catch (Exception ex)
            {
                Logger.Log("MismatchLog", $"Failed to write log: {ex.Message}", ConsoleColor.Red);
            }
        }
    }
}
