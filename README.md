# CatalyticKit

Catalytic 自动化测试平台的插件开发 SDK。

## 环境要求
- .NET 10.0 或更高版本

## 核心概念

- **ICommunicator**: 定义与被测设备的通讯协议逻辑（如 TCP, SerialPort）。
- **IInterceptor**: 允许在命令发送前或响应返回后进行拦截与处理。
- **IPlugin**: 插件的基础接口，包含生命周期管理（Activate/Deactivate）。
- **IPluginContext**: 提供插件运行时的上下文信息，如配置读取和 Slot 状态。

## 安装

从 NuGet 安装（发布后可用）：

```bash
dotnet add package CatalyticKit
```

或直接引用 DLL：

```xml
<Reference Include="CatalyticKit">
  <HintPath>lib/CatalyticKit.dll</HintPath>
</Reference>
```

## 快速开始

```csharp
using CatalyticKit;

public class MyPlugin : ICommunicator
{
    public string Id => "my-company.my-plugin";
    public string Protocol => "my-protocol";

    public Task ActivateAsync(IPluginContext context) => Task.CompletedTask;
    public Task DeactivateAsync() => Task.CompletedTask;

    public Task<string> ExecuteAsync(int slotIndex, string address, CommAction action, string payload, ExecuteOptions options, CancellationToken ct)
    {
        Service.AddPluginLog(Id, $"[Slot {slotIndex}] Executing {action} on {address}");
        // 你的通讯逻辑
        return Task.FromResult("OK");
    }
}
```

## 文档

- [插件系统设计](PLUGIN_SYSTEM.md) - 架构概览
- [插件开发指南](PLUGIN_DEVELOPER_GUIDE.md) - 完整开发教程

## 目录结构与示例

本项目不仅包含 SDK 核心，还提供了多种类型的插件实现参考：

| 目录/示例 | 类型 | 说明 |
|-----------|------|------|
| [CatalyticKit/](CatalyticKit/) | **核心** | SDK 源码 |
| [SocketClient/](SocketClient/) | 通讯 | TCP/IP 客户端通讯插件 |
| [CatalyticSerialPort/](CatalyticSerialPort/) | 通讯 | 串口（RS232/RS485）通讯插件 |
| [CsvReporter/](CsvReporter/) | 报告 | 将测试结果导出为 CSV 格式 |
| [Interceptor/](Interceptor/) | 拦截 | 命令/响应拦截处理逻辑 |
| [RemoteController/](RemoteController/) | 控制 | 远程控制接口实现 |

## 构建与测试

使用 dotnet CLI 进行构建：

```bash
dotnet build CatalyticKit.slnx
```

## License

MIT
