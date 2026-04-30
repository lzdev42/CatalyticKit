# Catalytic 插件开发指南

*(更新日期: 2026-04-30)*

---

## 目录

- [1. 简介](#1-简介)
- [2. 开发环境准备](#2-开发环境准备)
- [3. 快速开始：第一个插件](#3-快速开始第一个插件)
- [4. 核心概念](#4-核心概念)
- [5. SDK API 完整参考](#5-sdk-api-完整参考)
- [6. 完整示例：通讯器插件](#6-完整示例通讯器插件)
- [7. 错误处理最佳实践](#7-错误处理最佳实践)
- [8. 高级功能](#8-高级功能)
- [9. 调试与排查问题](#9-调试与排查问题)
- [10. 部署插件](#10-部署插件)
- [11. 常见问题 FAQ](#11-常见问题-faq)
- [12. 通讯插件核心设计参考](#12-通讯插件核心设计参考)

---

## 1. 简介

### 什么是 Catalytic 插件？

Catalytic 采用模块化插件架构。**所有与硬件交互或自定义业务逻辑的功能都通过插件实现。** 无论是基础的通讯协议支持，还是特定产品的测试流程扩展，其核心都是插件。

| 类型 | 接口 | 用途 | 典型场景 |
|------|------|------|----------|
| **通讯器** | `ICommunicator` | 底层设备通讯 | 串口、TCP、VISA、Modbus |
| **处理器** | `IProcessor` | 扩展自定义业务逻辑 | 扫码、数据转换、数据库操作、报告生成 |
| **协调器** | `ICoordinator` | 全局步骤执行控制 | 安全门确认、外部条件触发、步骤过滤 |

### 为什么使用插件？

- ✅ **易扩展**: 将 DLL 放入 `plugins` 文件夹，重启 Host 即可加载
- ✅ **隔离性**: 插件崩溃不会影响主程序
- ✅ **复用性**: 一个通讯器可以被多个处理器复用
- ✅ **跨平台**: 基于 .NET 10，支持 Windows / macOS / Linux
- ✅ **自包含**: 支持插件私有依赖加载，无需全局注册 DLL

---

## 2. 开发环境准备

### 2.1 必需软件

| 软件 | 版本 | 说明 |
|------|------|----------|
| .NET SDK | **10.0+** | 必须使用与 Host 一致或兼容的版本 |
| 目标框架 | **net10.0** | 插件项目必须针对 `net10.0` 进行编译 |
| 代码编辑器 | 任意 | VS Code / Visual Studio / Rider |

### 验证安装

打开终端（或 CMD），运行：

```bash
dotnet --version
# 输出: 10.0.xxx
```

### 2.2 引用 CatalyticKit

建议直接通过 NuGet 安装 SDK，并使用 `*` 始终引用最新版本：

```bash
dotnet add package CatalyticKit --version *
```

---

## 3. 快速开始：第一个插件

### 第一步：创建项目

```bash
# 创建类库项目
dotnet new classlib -n MyFirstPlugin -f net10.0

# 进入项目目录
cd MyFirstPlugin
```

### 第二步：添加 SDK 引用

```bash
dotnet add package CatalyticKit --version *
```

### 第三步：创建清单文件

在项目根目录创建 `manifest.json`：

```json
{
    "id": "my-company.my-first-plugin",
    "name": "My First Plugin",
    "version": "1.0.0",
    "entry": "MyFirstPlugin.dll",
    "capabilities": {
        "protocols": ["demo"],
        "tasks": ["my-custom-task"]
    }
}
```

### 第四步：实现插件

编辑 `Class1.cs`（重命名为 `DemoPlugin.cs`）：

```csharp
using CatalyticKit;

namespace MyFirstPlugin;

public class DemoPlugin : ICommunicator
{
    private ICommChannel? _channel;

    public string Id => "my-company.my-first-plugin";
    public string Protocol => "demo";

    // 插件激活时调用
    public async Task ActivateAsync(ICommChannel channel)
    {
        _channel = channel;
        Service.AddPluginLog(Id, "插件已激活");
    }

    public Task DeactivateAsync() => Task.CompletedTask;

    // 执行通讯动作
    public Task Execute(
        int slotIndex,
        string address,
        CommAction action,
        string payload,
        CommOptions options,
        CancellationToken ct)
    {
        // 上报从设备收到的原始数据
        _channel?.ReportData(slotIndex, address, "Hello from plugin!");
        
        return Task.CompletedTask;
    }
}
```

---

## 4. 核心概念

### 4.1 插件 ID

每个插件必须有一个**全局唯一 ID**。建议格式为 `publisher.name`，全部小写并使用连字符。

### 4.2 清单文件 (manifest.json)

每个插件目录**必须**包含一个 `manifest.json`。

### 4.3 生命周期

1. **ActivateAsync(channel)**：加载时调用一次，用于资源初始化。
2. **Execute(...) / ExecuteAsync(...)**：运行时根据任务触发。
3. **DeactivateAsync()**：关闭时调用，用于释放资源。

---

## 5. SDK API 完整参考

### 5.1 IPlugin（基础接口）

```csharp
public interface IPlugin
{
    string Id { get; }
    Task ActivateAsync(ICommChannel channel);
    Task DeactivateAsync();
}
```

### 5.2 ICommunicator (通讯器)

```csharp
public interface ICommunicator : IPlugin
{
    string Protocol { get; }
    
    Task Execute(
        int slotIndex,
        string address,
        CommAction action,
        string payload,
        CommOptions options,
        CancellationToken ct);
}
```

### 5.3 IProcessor (处理器)

```csharp
public interface IProcessor : IPlugin
{
    string Command { get; }
    Task ExecuteAsync(int slotIndex, CancellationToken ct);
}
```

### 5.4 ICoordinator (协调器)

```csharp
public interface ICoordinator : IPlugin
{
    Task<bool> BeforeStepAsync(int slotIndex, int stepId, string stepName, CancellationToken ct);
    Task AfterStepAsync(int slotIndex, int stepId, string stepName, bool passed);
}
```

### 5.5 ICommChannel (通讯通道)

```csharp
public interface ICommChannel
{
    string PluginDirectory { get; }
    ICommunicator? GetCommunicator(string protocolOrId);
    void ReportData(int slotIndex, string address, string data);
    void NotifyState(string address, DeviceState state);
}
```

### 5.6 Service API

| 方法 | 说明 |
|------|------|
| `Service.Slot(0).Start()` | 启动测试 |
| `Service.Slot(0).SubmitValue("3.31")` | 提交测量值（由引擎判决） |
| `Service.Slot(0).Report(true, "3.31")` | 直接提报结果和测量值 |
| `Service.Slot(0).GetCurrentStep()` | 获取当前步骤配置 (Step) |
| `Service.GetFlowDefinition()` | 获取全量流程配置 (TestFlow) |

---

## 6. 完整示例：通讯器插件 (串口)

```csharp
using System.IO.Ports;
using CatalyticKit;

namespace Acme.Serial;

public class SerialCommunicator : ICommunicator
{
    private ICommChannel? _channel;
    public string Id => "acme.serial";
    public string Protocol => "serial";

    public async Task ActivateAsync(ICommChannel channel)
    {
        _channel = channel;
    }

    public async Task Execute(int slotIndex, string address, CommAction action, string payload, CommOptions options, CancellationToken ct)
    {
        // 示例：仅演示结构
        if (action == CommAction.Send)
        {
            // ... 发送逻辑 ...
            _channel?.ReportData(slotIndex, address, "OK");
        }
    }

    public Task DeactivateAsync() => Task.CompletedTask;
}
```

---

## 7. 错误处理最佳实践

直接抛出标准 .NET 异常即可，Host 会捕获并将其上报给引擎和 UI。

---

## 8. 高级功能

### 数据模型

- **TestFlow / Step**: 流程与步骤的静态配置。
- **TestRecord / StepRecord**: 测试执行的完整历史数据。
- **CheckResult**: 包含实测值与判决详情的强类型模型。

---

## 9. 调试与排查问题

建议使用 `Service.AddPluginLog` 记录调试信息，Host 会将其分流到独立的插件日志文件中。

---

## 10. 部署插件

将插件 DLL 和 `manifest.json` 放入 `plugins/插件ID/` 目录即可。