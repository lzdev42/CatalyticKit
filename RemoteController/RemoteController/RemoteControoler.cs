namespace RemoteController;
using CatalyticKit;

public class RemoteControllerPlugin: IInterceptor
{
    public string Name => "RemoteController";

    public string Id    => "catalytic.remote-controller";
    public string TaskName => "RemoteControl";

    public Task DeactivateAsync()
    {
        Service.AddPluginLog(pluginId:Id, "[RemoteControl] 插件已停用。");
        return Task.CompletedTask;
    }

    public Task ActivateAsync(IPluginContext context)
    {
        Service.AddPluginLog(pluginId:Id, "[RemoteControl] 插件已停用。");
        return Task.CompletedTask;
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

    private bool AllFinished()
    {
        var isFinished = true;
        foreach (var slot in Service.GetAllSlots())
        {

        }

        return isFinished;
    }
}