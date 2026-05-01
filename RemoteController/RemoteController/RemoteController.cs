namespace RemoteController;
using CatalyticKit;
using System.Threading;

public class RemoteControllerPlugin : ICoordinator
{
    public string Name => "RemoteController";
    public string Id => "catalytic.remote-controller";

    private TaskCompletionSource? _allFinishedTcs;
    private CancellationTokenSource? _loopCts;
    private int _activeTestsCount = 0;

    public Task ActivateAsync(ICommChannel channel)
    {
        _loopCts = new CancellationTokenSource();
        Host.NotifySlotFinished += OnNotifySlotFinished;
        
        // 启动后台压测循环
        _ = Task.Run(() => RunTestLoopAsync(_loopCts.Token));

        Host.AddPluginLog(Id, "[RemoteControl] 仿真控制器已激活，30秒后将开始第一轮压测。");
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        Host.NotifySlotFinished -= OnNotifySlotFinished;
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        
        Host.AddPluginLog(Id, "[RemoteControl] 仿真控制器已停用。");
        return Task.CompletedTask;
    }

    private void OnNotifySlotFinished(TestFinishedEventArgs args)
    {
        // 递减活跃计数器
        int remaining = Interlocked.Decrement(ref _activeTestsCount);
        
        Host.AddPluginLog(Id, $"[RemoteControl] 槽位 {args.SlotIndex} 已完成 (本轮剩余: {remaining})");

        if (remaining <= 0)
        {
            Host.AddPluginLog(Id, "[RemoteControl] 本轮所有启动的槽位均已完成，触发结束信号。");
            _allFinishedTcs?.TrySetResult();
        }
    }

    private async Task RunTestLoopAsync(CancellationToken ct)
    {
        try
        {
            // 1. 启动后静默 30 秒
            await Task.Delay(30000, ct);

            while (!ct.IsCancellationRequested)
            {
                Host.ResetAll();
                int totalSlots = Host.GetSlotCount();
                Host.AddPluginLog(Id, $"[RemoteControl] === 开始新一轮压测循环 (可用 Slot 数量: {totalSlots}) ===");

                // 2. 初始化计数并启动
                _activeTestsCount = 0;
                for (int i = 0; i < totalSlots; i++)
                {
                    string sn = $"VTS-{DateTime.Now:yyyyMMddHHmmss}-{i:D2}";
                    Host.Slot(i).SetSn(sn);

                    var result = Host.Slot(i).Start();
                    if (result.Ok)
                    {
                        Interlocked.Increment(ref _activeTestsCount);
                    }
                    else
                    {
                        Host.AddPluginLog(Id, $"[RemoteControl] [WARN] Slot {i} 启动被拒绝: {result.Reason}");
                    }
                }

                // 3. 等待本轮结束信号
                if (_activeTestsCount > 0)
                {
                    _allFinishedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Host.AddPluginLog(Id, $"[RemoteControl] 等待本轮 {_activeTestsCount} 个槽位完成...");

                    var waitTask = _allFinishedTcs.Task;
                    if (await Task.WhenAny(waitTask, Task.Delay(-1, ct)) != waitTask)
                    {
                        break; // 取消
                    }
                    Host.AddPluginLog(Id, "[RemoteControl] 本轮压测完成。");
                }
                else
                {
                    Host.AddPluginLog(Id, "[RemoteControl] 本轮没有成功启动的槽位。");
                }

                // 4. 模拟准备下一轮的延时 (10s)
                Host.AddPluginLog(Id, "[RemoteControl] 正在重置状态并等待 10s...");
                
                await Task.Delay(10000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Host.AddPluginLog(Id, $"[RemoteControl] [FATAL] 压测循环发生异常: {ex.Message}");
        }
    }

    public Task AfterStepAsync(int slotIndex, int stepId, string stepName, bool passed) => Task.CompletedTask;
    public Task<bool> BeforeStepAsync(int slotIndex, int stepId, string stepName, CancellationToken ct) => Task.FromResult(true);
}