using CatalyticKit;

namespace CsvReporter;

/// <summary>
/// CSV 报告生成器插件
/// 演示 IProcessor 与 SDK v0.4.1 GetFlowDefinition / GetTestHistory 能力
/// 
/// 工作模式（扩展/Extension/HostControlled）：
///   1. ActivateAsync 时，通过 Service.GetFlowDefinition() 构建 CSV 表头（测试项名称 + 上下限）
///   2. ExecuteAsync (IProcessor) 由 Host 在扩展步骤触发后调用：
///      读取当前 Slot 的测试历史，生成 CSV 数据行后写入文件，并 ReportPass。
/// </summary>
public class CsvReporterPlugin : IProcessor
{
    private IPluginContext? _context;

    // CSV 输出目录，在 ActivateAsync 时从 Service.ReportFolder() 获取
    private string _outputDir = "";

    // 流程定义快照（在 ActivateAsync 时获取一次，作为表头基准）
    private IReadOnlyList<StepDefinition>? _testItems;

    public string Id    => "catalytic.csv-reporter";
    public string TaskName => "generate_csv_report";

    // ──────────────────────────────────────────────
    // IPlugin Lifecycle
    // ──────────────────────────────────────────────

    public Task ActivateAsync(IPluginContext context)
    {
        _context = context;
        _context.Log(LogLevel.Info, "[CsvReporter] Plugin activated.");

        // 使用 Host 配置的报告目录
        _outputDir = Service.ReportFolder();
        Directory.CreateDirectory(_outputDir);

        // 获取流程定义，缓存所有测试项（IsTestItem == true）
        try
        {
            var flow = Service.GetFlowDefinition();
            _testItems = flow?.Steps.Where(s => s.IsTestItem).ToList();

            if (_testItems is { Count: > 0 })
                _context.Log(LogLevel.Info, $"[CsvReporter] 已加载流程定义，共 {_testItems.Count} 个测试项。");
            else
                _context.Log(LogLevel.Warning, "[CsvReporter] 流程定义为空或 Engine 尚未加载流程，CSV 表头将在执行时动态生成。");
        }
        catch (ServiceNotInitializedException)
        {
            // Host 初始化期间可能 Service 未就绪，不阻断激活
            _context.Log(LogLevel.Warning, "[CsvReporter] Service 尚未初始化，将在 ExecuteAsync 时延迟获取流程定义。");
        }

        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        _context?.Log(LogLevel.Info, "[CsvReporter] Plugin deactivated.");
        return Task.CompletedTask;
    }

    // ──────────────────────────────────────────────
    // IProcessor — 由 Host 在 Extension 步骤时调用
    // ──────────────────────────────────────────────

    public async Task ExecuteAsync(int slotIndex, CancellationToken ct)
    {
        var slot = Service.Slot(slotIndex);
        _context?.Log(LogLevel.Info, $"[CsvReporter] 开始为 Slot {slotIndex} 生成 CSV 报告...");

        try
        {
            // 1. 延迟获取流程定义（若 ActivateAsync 时未能获取）
            if (_testItems == null)
            {
                var flow = Service.GetFlowDefinition();
                _testItems = flow?.Steps.Where(s => s.IsTestItem).ToList() ?? [];
            }

            ct.ThrowIfCancellationRequested();

            // 2. 获取本次测试历史
            var history = slot.GetTestHistory();
            if (history == null)
            {
                _context?.Log(LogLevel.Warning, $"[CsvReporter] Slot {slotIndex} 无测试历史，跳过写入。");
                slot.ReportPass(); // 没有数据不算失败
                return;
            }

            ct.ThrowIfCancellationRequested();

            // 3. 生成 CSV
            var csvPath = BuildCsvPath(slotIndex, history.Sn);
            await WriteCsvAsync(csvPath, slotIndex, history, ct);

            _context?.Log(LogLevel.Info, $"[CsvReporter] CSV 已写入: {csvPath}");
            slot.ReportPass();
        }
        catch (OperationCanceledException)
        {
            _context?.Log(LogLevel.Warning, $"[CsvReporter] Slot {slotIndex} CSV 生成被取消。");
            throw; // 取消直接抛出，不 ReportFail
        }
        catch (Exception ex)
        {
            _context?.Log(LogLevel.Error, $"[CsvReporter] Slot {slotIndex} CSV 生成失败: {ex.Message}");
            slot.ReportFail($"CSV 生成异常: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────
    // CSV 生成逻辑
    // ──────────────────────────────────────────────

    private async Task WriteCsvAsync(string path, int slotIndex, TestRecord history, CancellationToken ct)
    {
        // 建立 stepId → StepRecord 的快速查找表
        var resultMap = history.Steps
            .Where(s => s.IsTestItem)
            .ToDictionary(s => s.StepId);

        var lines = new List<string>();

        // ── 表头 ──────────────────────────────────────
        // SN,SlotIndex,Timestamp,StepName,Lower,Upper,Measured,Result
        lines.Add("SN,SlotIndex,Timestamp,StepName,Lower,Upper,Measured,Result");

        // ── 元信息行 ──────────────────────────────────
        var sn        = EscapeCsv(history.Sn ?? "N/A");
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 以流程定义为模板，确保每个测试项都有一行（即使该步骤未执行也输出占位行）
        var templateItems = _testItems ?? [];

        // 若流程定义为空则退化：只输出实际执行过的测试项
        if (templateItems.Count == 0)
            templateItems = history.Steps.Where(s => s.IsTestItem)
                                         .Select(s => new StepDefinition { StepId = s.StepId, StepName = s.StepName, IsTestItem = true })
                                         .ToList();

        foreach (var def in templateItems)
        {
            ct.ThrowIfCancellationRequested();

            string lower   = "";
            string upper   = "";

            // 从流程定义提取上下限
            if (def.CheckRule is CheckRuleDefinition.RangeRule range)
            {
                lower = range.Min.ToString("G");
                upper = range.Max.ToString("G");
            }
            else if (def.CheckRule is CheckRuleDefinition.ThresholdRule thr)
            {
                lower = $"{thr.Operator} {thr.ThresholdValue:G}";
                upper = "";
            }

            // 从执行历史提取实测值和结果
            string measured = "";
            string result   = "NOT_RUN";

            if (resultMap.TryGetValue(def.StepId, out var rec))
            {
                measured = EscapeCsv(rec.ResultValue ?? "");
                result   = rec.Passed ? "PASS" : "FAIL";

                // 若流程定义里没有上下限，尝试从运行结果补充
                if (string.IsNullOrEmpty(lower) && rec.Check is CheckDetail.RangeCheck rc)
                {
                    lower = rc.Min.ToString("G");
                    upper = rc.Max.ToString("G");
                }
            }

            lines.Add($"{sn},{slotIndex},{EscapeCsv(timestamp)},{EscapeCsv(def.StepName)},{lower},{upper},{measured},{result}");
        }

        await File.WriteAllLinesAsync(path, lines, System.Text.Encoding.UTF8, ct);
    }

    private string BuildCsvPath(int slotIndex, string? sn)
    {
        var safeSn   = string.IsNullOrWhiteSpace(sn) ? "no_sn" : sn.Replace("/", "-").Replace("\\", "-");
        var ts       = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"slot{slotIndex}_{safeSn}_{ts}.csv";
        return Path.Combine(_outputDir, fileName);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
