namespace TestServer.Devices;

using TestServer.Core;

public class MotionController
{
    private AsyncSocketServer _server;

    public void Start(int port)
    {
        _server = new AsyncSocketServer(port, "MotionCtrl");
        _server.OnCommandReceived = HandleCommandAsync;
        _server.Start();
    }

    private async Task<string> HandleCommandAsync(string command, string clientId)
    {
        // 基础延迟模拟
        await Task.Delay(20);

        string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return "ERR:MISSING_SLOT";

        string action = parts[0].ToUpper();
        if (!int.TryParse(parts[1], out int slotId)) return "ERR:INVALID_SLOT";

        // 针对不同工位的模拟动作
        switch (action)
        {
            case "VERSION":
                return $"v1.0.0 {slotId}";
            case "HOME":
                await Task.Delay(1000);
                return $"HOME_OK {slotId}";
            case "CLAMP":
                await Task.Delay(500);
                return $"CLAMP_OK {slotId}";
            case "UNCLAMP":
                await Task.Delay(500);
                return $"UNCLAMP_OK {slotId}";
            default:
                return $"ERR:UNKNOWN_CMD {slotId}";
        }
    }
}