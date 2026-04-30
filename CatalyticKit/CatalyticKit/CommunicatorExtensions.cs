namespace CatalyticKit;

public static class CommunicatorExtensions
{
    public static async Task Execute(this ICommunicator communicator, int slotIndex,
        string address,
        CommAction action,
        string payload,
        int timeoutMs,
        CancellationToken ct)
    {
        await communicator.Execute(slotIndex, address, action, payload,
            new CommOptions { TimeoutMs = timeoutMs }, ct);
    }

    public static async Task SendAsync(this ICommunicator communicator, int slotIndex, string address, string data, CancellationToken ct = default)
        => await communicator.Execute(slotIndex, address, CommAction.Send, data,
            new CommOptions { TimeoutMs = 5000 }, ct);

    public static async Task ReadAsync(this ICommunicator communicator, int slotIndex, string address, int timeoutMs, CancellationToken ct = default)
        => await communicator.Execute(slotIndex, address, CommAction.Read, "",
            new CommOptions { TimeoutMs = timeoutMs }, ct);

    public static async Task ConnectAsync(this ICommunicator communicator, int slotIndex, string address, int timeoutMs = 5000, CancellationToken ct = default)
        => await communicator.Execute(slotIndex, address, CommAction.Connect, "",
            new CommOptions { TimeoutMs = timeoutMs }, ct);

    public static async Task DisconnectAsync(this ICommunicator communicator, int slotIndex, string address, CancellationToken ct = default)
        => await communicator.Execute(slotIndex, address, CommAction.Disconnect, "",
            new CommOptions { TimeoutMs = 1000 }, ct);

    public static async Task GetStatusAsync(this ICommunicator communicator, int slotIndex, string address, CancellationToken ct = default)
        => await communicator.Execute(slotIndex, address, CommAction.Status, "",
            new CommOptions { TimeoutMs = 1000 }, ct);
}