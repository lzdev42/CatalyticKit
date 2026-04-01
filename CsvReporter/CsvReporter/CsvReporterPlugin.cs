using CatalyticKit;
using System.Text;

namespace CsvReporter;

/// <summary>
/// CSV 报告生成器插件 (标准横向宽表 - 按天汇总追加)
/// 演示 IProcessor 与 SDK v0.4.1 GetFlowDefinition / GetTestHistory 能力
/// </summary>
public class CsvReporterPlugin : IProcessor
{
    private IPluginContext? _context;
    private string _outputDir = "";
    private IReadOnlyList<StepDefinition>? _testItems;

    // 静态文件锁：确保多 Slot 并发完成时，排队安全地追加写入同一个 CSV
    private static readonly SemaphoreSlim _fileWriteLock = new SemaphoreSlim(1, 1);

    public string Id    => "catalytic.csv-reporter-daily";
    public string TaskName => "generate_csv_report";

    // ──────────────────────────────────────────────
    // IPlugin Lifecycle
    // ──────────────────────────────────────────────

    public Task ActivateAsync(IPluginContext context)
    {
        _context = context;
       Service.AddPluginLog(pluginId:Id,"[CsvReporter] 插件已激活。");

        _outputDir = Service.ReportFolder();
        Directory.CreateDirectory(_outputDir);

        try
        {
            var flow = Service.GetFlowDefinition();
            _testItems = flow?.Steps.Where(s => s.IsTestItem).ToList();

            if (_testItems is { Count: > 0 })
                Service.AddPluginLog(pluginId:Id,$"[CsvReporter] 已加载流程定义，共 {_testItems.Count} 个测试项。");

            else
                Service.AddPluginLog(pluginId:Id,"[CsvReporter] 流程定义为空，CSV 表头将在执行时动态生成。");

        }
        catch (ServiceNotInitializedException)
        {
            Service.AddPluginLog(pluginId:Id, "[CsvReporter] Service 尚未初始化，将在 ExecuteAsync 时延迟获取。");
        }

        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        Service.AddPluginLog(pluginId:Id, "[CsvReporter] 插件已停用。");
        return Task.CompletedTask;
    }

    // ──────────────────────────────────────────────
    // IProcessor 
    // ──────────────────────────────────────────────

    public async Task ExecuteAsync(int slotIndex, CancellationToken ct)
    {
        var slot = Service.Slot(slotIndex);
        Service.AddPluginLog(Id,$"snsn {slot.GetSn() ?? "xx"}");
        Service.AddPluginLog(pluginId:Id, $"[CsvReporter] 开始为 Slot {slotIndex} 生成横向 CSV 报告...");

        try
        {
            if (_testItems == null)
            {
                var flow = Service.GetFlowDefinition();
                _testItems = flow?.Steps.Where(s => s.IsTestItem).ToList() ?? [];
            }

            ct.ThrowIfCancellationRequested();

            var history = slot.GetTestHistory();
            if (history == null)
            {
                Service.AddPluginLog(pluginId:Id, $"[CsvReporter] Slot {slotIndex} 无测试历史，跳过写入。");
                slot.ReportPass(); 
                return;
            }

            ct.ThrowIfCancellationRequested();

            // 按天生成文件名
            var csvPath = BuildDailyCsvPath();
            
            // 执行带锁的追加写入
            await AppendToCsvAsync(csvPath, history, ct);

            Service.AddPluginLog(pluginId:Id, $"[CsvReporter] 数据已追加至宽表 CSV: {csvPath}");
            slot.ReportPass();
        }
        catch (OperationCanceledException)
        {
            Service.AddPluginLog(pluginId:Id, $"[CsvReporter] Slot {slotIndex} CSV 写入被取消。");
            throw; 
        }
        catch (Exception ex)
        {
            Service.AddPluginLog(pluginId:Id, $"[CsvReporter] Slot {slotIndex} CSV 写入失败: {ex.Message}");
            slot.ReportFail($"CSV 写入异常: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────
    // CSV 宽表追加逻辑 (线程安全)
    // ──────────────────────────────────────────────

    private async Task AppendToCsvAsync(string path, TestRecord history, CancellationToken ct)
    {
        // 建立 stepId → StepRecord 的快速查找表
        var resultMap = history.Steps
            .Where(s => s.IsTestItem)
            .ToDictionary(s => s.StepId);

        var templateItems = _testItems ?? [];

        if (templateItems.Count == 0)
        {
            templateItems = history.Steps.Where(s => s.IsTestItem)
                                         .Select(s => new StepDefinition { StepId = s.StepId, StepName = s.StepName, IsTestItem = true })
                                         .ToList();
        }

        var headerRow = new List<string> { "序列号(SN)", "测试结果" };
        var upperRow  = new List<string> { "上限值", "--" };
        var lowerRow  = new List<string> { "下限值", "--" };
        
        var sn = EscapeCsv(history.Sn ?? "N/A");
        Service.AddPluginLog(pluginId:Id,$"SN == {sn}");
        var dataRow = new List<string> { sn };

        bool isOverallPass = true; 

        // 1. 拼装数据与表头结构
        foreach (var def in templateItems)
        {
            ct.ThrowIfCancellationRequested();

            string lower = "";
            string upper = "";

            if (def.CheckRule is CheckRuleDefinition.RangeRule range)
            {
                lower = range.Min.ToString("G");
                upper = range.Max.ToString("G");
            }
            else if (def.CheckRule is CheckRuleDefinition.ThresholdRule thr)
            {
                if (thr.Operator.Contains("<")) 
                    upper = thr.Value.ToString("G");
                else if (thr.Operator.Contains(">")) 
                    lower = thr.Value.ToString("G");
                else 
                    lower = $"{thr.Operator}{thr.Value:G}";
            }

            string measured = "";
            if (resultMap.TryGetValue(def.StepId, out var rec))
            {
                measured = EscapeCsv(rec.ResultValue ?? "");
                if (!rec.Passed) isOverallPass = false;

                if (string.IsNullOrEmpty(lower) && string.IsNullOrEmpty(upper) && rec.Check is CheckDetail.RangeCheck rc)
                {
                    lower = rc.Min.ToString("G");
                    upper = rc.Max.ToString("G");
                }
            }
            else
            {
                isOverallPass = false;
            }

            headerRow.Add(EscapeCsv(def.StepName));
            upperRow.Add(EscapeCsv(upper));
            lowerRow.Add(EscapeCsv(lower));
            dataRow.Add(measured);
        }

        dataRow.Insert(1, isOverallPass ? "PASS" : "FAIL");

        // 2. 加锁进行文件 IO 操作
        await _fileWriteLock.WaitAsync(ct);
        try
        {
            bool isNewFile = !File.Exists(path);
            var linesToWrite = new List<string>();

            // 如果文件是今天刚创建的（不存在），先把前 3 行（表头、上限、下限）写进去
            if (isNewFile)
            {
                linesToWrite.Add(string.Join(",", headerRow));
                linesToWrite.Add(string.Join(",", upperRow));
                linesToWrite.Add(string.Join(",", lowerRow));
            }
            
            // 永远追加当前设备的数据行（第 4 行及以后）
            linesToWrite.Add(string.Join(",", dataRow));

            // AppendAllLinesAsync：如果文件不存在会先创建，存在则在末尾追加
            await File.AppendAllLinesAsync(path, linesToWrite, Encoding.UTF8, ct);
        }
        finally
        {
            // 释放锁，允许下一个 Slot 写入
            _fileWriteLock.Release();
        }
    }

    // 按天生成文件名，例如：Report_20260313.csv
    private string BuildDailyCsvPath()
    {
        var dateStr = DateTime.Now.ToString("yyyyMMdd");
        var fileName = $"DailyReport_{dateStr}.csv";
        return Path.Combine(_outputDir, fileName);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}