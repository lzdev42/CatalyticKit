namespace RemoteController;
using CatalyticKit;

public class RemoteControllerPlugin: ICoordinator
{
    public string Name => "RemoteController";

    public string Id => "catalytic.remote-controller";


    public Task DeactivateAsync()
    {
        Service.SlotFinished -= OnSlotFinished;
        Service.AddPluginLog(pluginId:Id, "[RemoteControl] 插件已停用。");
        return Task.CompletedTask;
    }

    public Task ActivateAsync(ICommChannel channel)
    {
        Service.SlotFinished += OnSlotFinished;
        Service.AddPluginLog(pluginId:Id, "[RemoteControl] 插件已激活。");
        return Task.CompletedTask;
    }

    private void OnSlotFinished(TestFinishedEventArgs e)
    {
        Service.AddPluginLog(pluginId:Id, $"[RemoteControl] Slot {e.SlotIndex} 测试完成: {(e.Passed ? "PASS" : "FAIL")}");
    }


    public Task AfterStepAsync(int slotIndex, int stepId, string stepName, bool passed)
    {
        Service.AddPluginLog(pluginId:Id, $"[RemoteControl] after slot = {slotIndex},step = {stepId}, stepname = {stepName}。");
        return Task.CompletedTask;
    }

    public Task<bool> BeforeStepAsync(int slotIndex, int stepId, string stepName, CancellationToken ct)
    {
        Service.AddPluginLog(pluginId:Id, $"[RemoteControl] before slot = {slotIndex},step = {stepId}, stepname = {stepName}。");
        return Task.FromResult(true);
    }
}