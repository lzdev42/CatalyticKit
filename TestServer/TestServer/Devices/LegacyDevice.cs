namespace TestServer.Devices;

using System;
using System.Threading.Tasks;
using TestServer.Core;

public class LegacyDevice
{
    private AsyncSocketServer _server;
    private int _slotId;

    // 把 Random 提出来，防止并发请求时产生一样的随机数
    private readonly Random _rand = new Random();

    public void Start(int port, int slotId)
    {
        _slotId = slotId;
        _server = new AsyncSocketServer(port, $"Legacy-S{_slotId:D2}");
        _server.OnCommandReceived = HandleCommandAsync;
        _server.Start();
    }

    private async Task<string> HandleCommandAsync(string command, string clientId)
    {
        // 基础延迟模拟 (10~50ms)
        await Task.Delay(_rand.Next(10, 50));

        string cmd = command.ToUpper();

        switch (cmd)
        {
            case "*IDN?":
                // 身份信息加上 Slot ID 尾巴
                return $"Acme Corp,LegacyModel-X,SN-998877-S{_slotId:D2},v3.14";

            case "SYST:ERR?":
                return "0,\"No error\"";

            case "MEAS:VOLT?":
                // 整数部分 11 或 12，十分位随机 0~9，百分位和千分位直接定死为 slotId
                // 比如 slot 1 出来的结果就是: VOLTAGE = 12.501 VDC
                int vInt = _rand.Next(11, 13);
                int vTenth = _rand.Next(0, 10);
                return $"VOLTAGE = {vInt}.{vTenth}{_slotId:D2} VDC";

            case "MEAS:CURR?":
                // 十分位随机 3~6，百分位和千分位定死为 slotId
                // 比如 slot 2 出来的结果就是: Current: 0.402A (OK)
                int cTenth = _rand.Next(3, 7);
                return $"Current: 0.{cTenth}{_slotId:D2}A (OK)";

            case "SYST:STATUS?":
                // 状态里直接加个 SLOT 字段
                return $"TEMP:45.5;HUM:60%;ALARM:0;SLOT:{_slotId}";

            case "CONF:GET?":
                // 配置里加个 SLOT 标识
                return $"[MODE=AUTO][RANGE=10][FILTER=ON][SLOT={_slotId}]";

            case "MEM:DUMP?":
                // 内存 Dump 第一行的最后一个字节变成 slotId 的十六进制
                return $"0010: AA BB CC {_slotId:X2}\n0020: 11 22 33 44\nEND";

            case "WAVE:RAW?":
                // 波形数据第一个点加上 slotId 作为基数
                return $"{100 + _slotId},105,110,120,135,140,115,108";

            case "TEST:SLOW":
                await Task.Delay(1000);
                return $"PART1_PART2_END_S{_slotId}";

            case "TEST:DELAY":
                await Task.Delay(3000);
                return $"DELAYED_RESPONSE_S{_slotId}";

            case "TEST:GLITCH":
                return $"V@L#UE: 12.34_$_S{_slotId}";

            default:
                return "ERR:SYNTAX";
        }
    }
}