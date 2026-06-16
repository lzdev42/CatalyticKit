namespace TestServer.Core;

/// <summary>
/// 轮次追踪器 — 记录每轮发出的 slot 集合，追踪 Catalytic 回复的 pass/fail，校验是否一致
/// </summary>
public class RoundTracker
{
    private readonly MismatchLogger _logger;
    private readonly object _lock = new();

    // 当前轮次
    private int _currentRoundId;
    private HashSet<int> _currentStartSlots = new();
    private HashSet<int> _currentPassSlots = new();
    private HashSet<int> _currentFailSlots = new();

    // 轮次完成信号
    private TaskCompletionSource<bool> _roundCompleteTcs = new();

    public RoundTracker(MismatchLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 开始新一轮，记录本轮发送的 slot 集合
    /// </summary>
    public void StartRound(int roundId, HashSet<int> slots)
    {
        lock (_lock)
        {
            _currentRoundId = roundId;
            _currentStartSlots = new HashSet<int>(slots);
            _currentPassSlots = new HashSet<int>();
            _currentFailSlots = new HashSet<int>();
            _roundCompleteTcs = new TaskCompletionSource<bool>();
        }
        Logger.Log("RoundTracker", $"Round-{roundId:D3} started with slots: [{string.Join(", ", slots.OrderBy(s => s))}]", ConsoleColor.Cyan);
    }

    /// <summary>
    /// 记录 Catalytic 回复的 pass slot 集合
    /// </summary>
    public void RecordPass(HashSet<int> passSlots)
    {
        lock (_lock)
        {
            foreach (var s in passSlots)
                _currentPassSlots.Add(s);

            CheckRoundComplete();
        }
    }

    /// <summary>
    /// 记录 Catalytic 回复的 fail slot 集合
    /// </summary>
    public void RecordFail(HashSet<int> failSlots)
    {
        lock (_lock)
        {
            foreach (var s in failSlots)
                _currentFailSlots.Add(s);

            CheckRoundComplete();
        }
    }

    /// <summary>
    /// 检查当前轮次是否完成（pass ∪ fail == start）
    /// </summary>
    private void CheckRoundComplete()
    {
        // 必须在 lock 内调用
        var allReported = new HashSet<int>(_currentPassSlots);
        allReported.UnionWith(_currentFailSlots);

        if (allReported.SetEquals(_currentStartSlots))
        {
            // 轮次完成，校验
            ValidateAndLog();
            _roundCompleteTcs.TrySetResult(true);
        }
    }

    /// <summary>
    /// 等待轮次完成，带超时
    /// </summary>
    /// <returns>true = 正常完成，false = 超时</returns>
    public async Task<bool> WaitForRoundCompleteAsync(TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() => _roundCompleteTcs.TrySetResult(false));
            return await _roundCompleteTcs.Task;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 强制完成当前轮次（超时后调用），执行校验并记录不匹配
    /// </summary>
    public void ForceComplete()
    {
        lock (_lock)
        {
            ValidateAndLog();
            _roundCompleteTcs.TrySetResult(false);
        }
    }

    /// <summary>
    /// 校验并记录不匹配（在 lock 内调用）
    /// </summary>
    private void ValidateAndLog()
    {
        var allReported = new HashSet<int>(_currentPassSlots);
        allReported.UnionWith(_currentFailSlots);

        // 缺少的 slot
        var missing = new HashSet<int>(_currentStartSlots);
        missing.ExceptWith(allReported);

        // 多余的 slot
        var extra = new HashSet<int>(allReported);
        extra.ExceptWith(_currentStartSlots);

        // 重复判定（同时出现在 pass 和 fail 中）
        var duplicate = new HashSet<int>(_currentPassSlots);
        duplicate.IntersectWith(_currentFailSlots);

        bool isMatch = missing.Count == 0 && extra.Count == 0 && duplicate.Count == 0;

        if (isMatch)
        {
            Logger.Log("RoundTracker",
                $"Round-{_currentRoundId:D3} OK — start=[{string.Join(",", _currentStartSlots.OrderBy(s => s))}] " +
                $"pass=[{string.Join(",", _currentPassSlots.OrderBy(s => s))}] fail=[{string.Join(",", _currentFailSlots.OrderBy(s => s))}]",
                ConsoleColor.Green);
        }
        else
        {
            Logger.Log("RoundTracker",
                $"Round-{_currentRoundId:D3} MISMATCH — missing=[{string.Join(",", missing.OrderBy(s => s))}] " +
                $"extra=[{string.Join(",", extra.OrderBy(s => s))}] dup=[{string.Join(",", duplicate.OrderBy(s => s))}]",
                ConsoleColor.Red);

            _logger.LogMismatch(
                _currentRoundId,
                _currentStartSlots,
                _currentPassSlots,
                _currentFailSlots,
                missing,
                extra,
                duplicate);
        }
    }
}
