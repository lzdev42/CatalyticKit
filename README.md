# CatalyticKit

> [!IMPORTANT]
> **本仓库不包含主程序源码，也不是主程序本身**
> 
> Catalytic 主程序不开源。本仓库只有 SDK 和插件示例，没有主程序的任何代码。
> 
> 主程序在这里下载：https://github.com/lzdev42/catalytic-home/releases

> [!WARNING]
> **不要修改 SDK 源码**
> 
> `CatalyticKit/` 目录下的 SDK 源码仅供参考，请不要修改它。
> 
> 主程序内置了自己的 SDK 副本，你本地改了也不会影响运行时行为。放出来的目的只有一个：当你遇到问题时，可以翻一翻源码搞清楚内部逻辑，仅此而已。
> 
> 你需要做的只是**实现 SDK 中定义的接口**，写你自己的插件逻辑。

### 第三方库与 .NET 版本不兼容？用独立进程 + IPC

如果你依赖的第三方库（如某些硬件驱动、算法 SDK）不支持 .NET 10，不要试图降低整个插件的目标框架。

正确做法是：

1. 创建一个**独立的子进程**，使用这个第三方库兼容的 .NET 或其他运行时版本
2. 在子进程与插件之间通过 **IPC**（如命名管道、本地 Socket、gRPC 等）通信
3. 插件本身仍然面向 .NET 10，保持与主程序的兼容性

### 打不开 `.slnx` 文件？

`.slnx` 是较新的 Solution 格式，旧版 IDE 可能无法识别。解决办法任选其一：

- 升级到支持 `.slnx` 的 IDE 版本：
  - **Visual Studio**：最新版
  - **Rider**：最新版
  - **VS Code**：安装最新版 C# Dev Kit 扩展
- 或者直接忽略 `.slnx`，进对应目录**用 `.csproj` 单独打开**即可正常构建


Catalytic 自动化测试平台的插件开发 SDK。

## 环境要求
- .NET 10.0 或更高版本

## 核心概念

- **ICommunicator**: 定义与被测设备的通讯协议逻辑（如 TCP, SerialPort）。
- **IProcessor**: 定义自定义业务逻辑（如报告生成、数据处理）。
- **ICoordinator**: 允许在步骤执行前后进行全局拦截与流程控制。
- **IPlugin**: 插件的基础接口，包含生命周期管理（Activate/Deactivate）。
- **ICommChannel**: 提供插件回传数据和通知状态的通道。

## 安装

从 NuGet 安装（发布后可用）：

```bash
dotnet add package CatalyticKit
```

## 快速开始

```csharp
using CatalyticKit;

public class MyPlugin : ICommunicator
{
    public string Id => "my-company.my-plugin";
    public string Protocol => "my-protocol";

    public Task ActivateAsync(ICommChannel channel) => Task.CompletedTask;
    public Task DeactivateAsync() => Task.CompletedTask;

    public Task Execute(int slotIndex, string address, CommAction action, string payload, CommOptions options, CancellationToken ct)
    {
        Host.AddPluginLog(Id, $"[Slot {slotIndex}] Executing {action} on {address}");
        // 你的通讯逻辑
        return Task.CompletedTask;
    }
}
```

## 文档

- [插件开发指南](PLUGIN_DEVELOPER_GUIDE.md) — 接口参考、项目搭建、部署流程
- [插件运行机制](PLUGIN_SYSTEM.md) — 通信模型、slotIndex 归属契约、请求队列机制

## 目录结构与示例

本项目不仅包含 SDK 核心，还提供了多种类型的插件实现参考：

| 目录/示例 | 类型 | 说明 |
|-----------|------|------|
| [CatalyticKit/](CatalyticKit/) | **核心** | SDK 源码 |
| [SocketClient/](SocketClient/) | 通讯器 | TCP 客户端，支持多槽位并发与请求队列路由 |
| [CatalyticSerialPort/](CatalyticSerialPort/) | 通讯器 | 串口通讯，支持终止符模式 |
| [CsvReporter/](CsvReporter/) | 处理器 | 将测试结果导出为 CSV 格式 |
| [RemoteController/](RemoteController/) | 协调器 | 远程压测控制器，演示事件订阅与槽位启动 |

## 构建

使用 dotnet CLI 进行构建：

```bash
dotnet build CatalyticKit.slnx
```

## License

MIT
