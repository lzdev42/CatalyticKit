using TestServer.Core;
using TestServer.Devices;

Console.Title = "Catalytic OQC - Device Simulator (Mock Server)";
Logger.Log("SYSTEM", "Starting Device Simulators...", ConsoleColor.White);

// 1. 启动 Mock 运动控制上层 (模拟机械手，主动驱动测试流程)
//    batchMode: false = 批量模式（同步），true = 独立模式（异步）
var motionUpper = new MotionControlUpper(batchMode: false);
motionUpper.Start(12300);

// 2. 启动 1 个 MotionController (处理带 Slot 的并发请求)
var motionController = new MotionController();
motionController.Start(12301);

// 3. 启动 12 个 LegacyDevice (独立设备)
for (int i = 1; i <= 12; i++)
{
    var legacyDev = new LegacyDevice();
    legacyDev.Start(12302 + i, i);
}

Logger.Log("SYSTEM", "All simulators are up and running! Waiting for Catalytic...", ConsoleColor.Green);

// 挂起主线程
Thread.Sleep(Timeout.Infinite);
