# Catalytic 插件开发指南

*(更新日期: 2026-04-22)*

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
| **处理器** | `IProcessor` | 扩展自定义业务逻辑 | 扫码、数据转换、数据库操作 |
| **拦截器** | `IInterceptor` | 全局步骤执行控制 | 安全门确认、外部条件触发、步骤过滤 |

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

或在 `.csproj` 中添加：

```xml
<ItemGroup>
    <PackageReference Include="CatalyticKit" Version="*" />
</ItemGroup>
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

建议通过 NuGet 安装 SDK，以确保依赖管理的一致性：

```bash
dotnet add package CatalyticKit --version *
```

或者手动在 `.csproj` 中添加：

```xml
<ItemGroup>
    <PackageReference Include="CatalyticKit" Version="*" />
</ItemGroup>
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

> ⚠️ **重要**: `id` 必须全局唯一，建议格式为 `公司名.插件名`

### 第四步：实现插件

编辑 `Class1.cs`（重命名为 `DemoPlugin.cs`）：

```csharp
using CatalyticKit;

namespace MyFirstPlugin;

public class DemoPlugin : ICommunicator
{
    private IPluginContext? _context;

    // 插件唯一标识（必须与 manifest.json 中的 id 一致）
    public string Id => "my-company.my-first-plugin";
    
    // 支持的协议名称
    public string Protocol => "demo";

    // 插件激活时调用
    public async Task ActivateAsync(IPluginContext context)
    {
        _context = context;
        Service.AddPluginLog(Id, "插件已激活");
    }

    // 插件停用时调用
    public Task DeactivateAsync()
    {
        return Task.CompletedTask;
    }

    // 执行通讯动作
    public Task ExecuteTask(
        int slotIndex,
        string address,
        CommAction action,
        string payload,
        ExecuteOptions options,
        CancellationToken ct)
    {
        // 实现通讯逻辑
        // 示例：直接推送成功结果
        _context?.PushEvent(slotIndex, address, PluginEventType.Result, "Hello from plugin!");
        
        return Task.CompletedTask;
    }
}
```

### 第五步：编译

```bash
dotnet build -c Release
```

输出文件位于 `bin/Release/net10.0/`

### 第六步：部署测试

将以下文件复制到 Catalytic 的 `plugins/my-company.my-first-plugin/` 目录：

```
plugins/
└── my-company.my-first-plugin/
    ├── manifest.json
    └── MyFirstPlugin.dll
```

重启 Catalytic，在日志中应该能看到 "🎉 插件已激活！"

---

## 4. 核心概念

### 4.1 插件 ID

每个插件必须有一个**全局唯一 ID**。

| 格式 | 正确示例 | 错误示例 |
|------|----------|----------|
| `publisher.name` | `acme.scpi-driver` | `scpi`（太短）|
| 小写 + 连字符 | `my-company.modbus` | `MyCompany.Modbus`（大写）|

> 💡 ID 是 UI 中选择插件的唯一凭证，请谨慎命名。

### 4.2 清单文件 (manifest.json)

每个插件目录**必须**包含一个 `manifest.json`：

```json
{
    "id": "acme.serial",           // 必填：全局唯一 ID
    "name": "Acme Serial Driver",  // 必填：显示名称
    "version": "1.0.0",            // 必填：版本号
    "entry": "Acme.Serial.dll",    // 必填：入口 DLL 文件名
    "capabilities": {
        "protocols": ["serial"],   // 通讯器支持的协议列表
        "tasks": []                // 处理器支持的任务列表
    }
}
```

### 4.3 生命周期

```
┌─────────────────────────────────────────────────────────────┐
│                     插件生命周期                              │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  [Host 启动]                                                 │
│       │                                                      │
│       ▼                                                      │
│  1. LoadAllAsync()  ──> 读取 manifest.json                   │
│       │                                                      │
│       ▼                                                      │
│  2. LoadAssembly()  ──> 加载 DLL                             │
│       │                                                      │
│       ▼                                                      │
│  3. CreateInstance() ──> 反射创建插件实例                     │
│       │                                                      │
│       ▼                                                      │
│  4. ActivateAsync() ──> 【资源初始化】                          │
│       │                                                      │
│       ▼                                                      │
│  5. ExecuteTask()   ──> 【处理请求】 (循环调用)               │
│       │                                                      │
│       ▼                                                      │
│  6. DeactivateAsync() ──> 【资源释放】                          │
│       │                                                      │
│       ▼                                                      │
│  [Host 关闭]                                                 │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

### 4.4 Service API (控制与事件)

Catalytic 提供了一个静态 `Service` 类，允许插件**主动控制**测试流程并**监听**状态变化。这对于集成外部硬件（如 PLC、控制卡）非常有用。

#### 全局命令

| 方法 | 说明 |
|------|------|
| `Service.GetSlotCount()` | 获取系统配置的总 Slot 数量 |
| `Service.GetAllSlots()` | 获取系统中所有 Slot 对象数组，方便遍历 |
| `Service.StartAll()` | 启动所有 Slot 的测试（非阻塞）|
| `Service.StopAll()` | 停止所有 Slot 的测试（非阻塞）|
| `Service.GetFlowDefinition()` | 获取全量步骤的静态定义（含上下限） |
| `Service.ReportFolder()` | 获取工作目录下的 `reports` 文件夹绝对路径 |

#### 单 Slot 命令

| 方法 | 说明 |
|------|------|
| `Service.Slot(0).Start()` | 启动指定 Slot 测试（非阻塞）|
| `Service.Slot(0).Stop()` | 停止指定 Slot 测试（非阻塞）|
| `Service.Slot(0).SetSN("ABC")` | 设置产品 SN |
| `Service.Slot(0).GetVariable(name)` | 获取 Engine 提取的流程变量（只读）|
| `Service.Slot(0).GetTestHistory()` | 获取本次测试完整记录（建议在 `TestFinished` 回调中调用）|

#### 事件监听

| 事件 | 签名 | 说明 |
|------|------|------|
| `TestStarted` | `Action` | 测试开始时触发 |
| `TestFinished` | `Action<bool, string?>` | 测试结束时触发 (passed, message) |
| `StepFinished` | `Action<int, bool>` | 单步结束时触发 (stepIndex, passed) |

#### 线程安全

> ✅ **Service API 是完全线程安全的。** 可从任意线程调用 `Service` 的任何方法或订阅事件，SDK 内部已做防护。即使在事件回调中调用 `Service.Slot(x).Start()` 也不会死锁。

#### 插件实例的线程安全
> [!IMPORTANT]
> **核心机制**: Catalytic Host 采用单例模式加载插件。这意味着一个测试项目中，**同一个插件 ID 的实例在内存中只有一份**。
> **并发调用**: 多个 Slot 可能会同时进入同一个插件实例的 `ExecuteTask`（通讯器）或 `ExecuteAsync`（处理器）。因此，插件开发者**必须**处理好并发竞争问题：
>
> 1. **物理资源互斥**: 如串口、网口等独占资源，必须使用 `SemaphoreSlim` 或 `lock` 确保同一时间只有一个 Slot 在操作。推荐使用 `SemaphoreSlim` 因为它支持异步等待 `WaitAsync`。
> 2. **数据隔离**: 避免使用类级别的全局变量存储特定槽位的状态，除非该变量是用来做跨槽位共享的。
> 3. **缓冲区清理**: 由于设备响应可能超时，下次通讯前应主动清理物理缓冲区，防止读到上一个 Slot 遗留的残余数据。

> ✅ **事件回调异常保护。** 若事件处理代码抛出异常，SDK 会自动捕获并上报给 Host，不会导致 Host 崩溃。

#### 代码示例：PLC 集成

```csharp
public class PlcPlugin : ICommunicator
{
    public string Id => "acme.plc";
    public string Protocol => "plc";

    public async Task ActivateAsync(IPluginContext context)
    {
        // 1. 监听单个 Slot 测试结束事件 ( ISlot.TestFinished )
        Service.Slot(0).TestFinished += (passed, message) => 
        {
            if (passed) 
                PlcDriver.MoveToPass();
            else 
                PlcDriver.MoveToFail();
            
            Service.AddPluginLog(Id, $"Slot 0 测试已结束，结果: {passed}, 消息: {message}");
        };

        // 或监听全局测试完成事件 ( Service.SlotFinished )
        Service.SlotFinished += (args) => 
        {
            Service.AddPluginLog(Id, $"检测到 Slot {args.SlotIndex} 完成，通过={args.Passed}");
        };

        // 2. 监听 PLC 信号 -> 触发所有 Slot 测试
        PlcDriver.OnMaterialArrived += (sn) => 
        {
            Service.Slot(0).SetSn(sn);
            Service.StartAll();  // 启动所有 Slot
        };

        // 3. 紧急停止
        PlcDriver.OnEmergencyStop += () => 
        {
            Service.StopAll();  // 停止所有 Slot
        };
    }

    public Task DeactivateAsync() => Task.CompletedTask;

    public Task ExecuteTask(int slotIndex, string address, CommAction action, string payload, ExecuteOptions options, CancellationToken ct)
        => Task.CompletedTask;
}
```

> ⚠️ **注意**: 所有控制指令（如 `Start`、`StartAll`）都是**非阻塞 (Fire-and-Forget)** 的。调用 `Start()` 只是向 Host 发送请求，Host 会在稍后调度执行。不要假设调用返回后测试已经开始。

---

## 5. SDK API 完整参考

### 5.1 IPlugin（基础接口）

所有插件都必须实现此接口。

```csharp
public interface IPlugin
{
    string Id { get; }
    Task ActivateAsync(IPluginContext context);
    Task DeactivateAsync();
}
```

### 5.2 ICommunicator (通讯器)

用于实现与底层硬件（PLC、仪表、扫码枪等）的直接通讯。

```csharp
public interface ICommunicator : IPlugin
{
    string Protocol { get; }
    
    Task ExecuteTask(
        int slotIndex,
        string address,
        CommAction action,
        string payload,
        ExecuteOptions options,
        CancellationToken ct);
}
```

#### ExecuteTask 参数说明

| 参数 | 类型 | 语义 |
|---|---|---|
| `slotIndex` | `int` | 调用此任务的槽位索引。插件必须将其传入 `PushEvent` 以确保结果路由正确 |
| `address` | `string` | 目标设备地址（如 `192.168.1.1:5025`、`COM3`） |
| `action` | `CommAction` | 操作意图 (Connect/Send/Query 等) |
| `payload` | `string` | 指令内容 |
| `options` | `ExecuteOptions` | 超时、结束符、资源模式等选项 |
| `ct` | `CancellationToken` | 取消令牌 |

### 5.3 ExecuteOptions (执行选项)

```csharp
public class ExecuteOptions
{
    public int TimeoutMs { get; set; }
    public string? CommandTerminator { get; set; }
    public string? ResponseTerminator { get; set; }
    public bool IsShared { get; set; }
}
```

> [!NOTE]
> **独占资源模式 vs 共享资源模式**
>
> | | 独占资源模式 | 共享资源模式 |
> |---|---|---|
> | **硬件映射** | N Slots : N 硬件实例 | N Slots : 1 硬件端点 |
> | **地址关系** | 每个槽位对应独立地址 | 所有槽位路由至同一地址 |
> | **并发冲突** | 天然隔离，无冲突 | 插件必须自行序列化 |
> | `IsShared` | `false` | `true` |

### 5.4 IProcessor (处理器)

实现此接口以处理复杂业务逻辑（如数据库操作、文件解析、业务流控制）。

```csharp
public interface IProcessor : IPlugin
{
    string TaskName { get; }
    Task ExecuteAsync(int slotIndex, CancellationToken ct);
}
```

#### 获取运行参数

插件在 `ExecuteAsync` 中通过 `Service` 接口拉取参数：

```csharp
public async Task ExecuteAsync(int slotIndex, CancellationToken ct)
{
    var step = Service.Slot(slotIndex).GetCurrentStep();
    string? rawParams = step?.Params; // UI 配置的参数字符串
}
```

### 5.5 IInterceptor (拦截器)

拦截器用于在步骤执行前后进行全局预检或处理。

> [!WARNING]
> **全局唯一性**：整个系统中只允许加载一个拦截器插件。如果加载了多个，系统会以先发现的为准，并在日志中发出警告。

```csharp
public interface IInterceptor : IPlugin
{
    Task<bool> BeforeStepAsync(int slotIndex, int stepId, string stepName, CancellationToken ct);
    Task AfterStepAsync(int slotIndex, int stepId, string stepName, bool passed);
}
```

#### 关键特性
1. **同步阻塞**：`BeforeStepAsync` 会阻塞 Engine 的执行，直到收到插件的决定。
2. **超时机制**：Host 为拦截器提供了 5s 的默认超时保护。如果插件在该时间内未返回，系统将自动视为 `false` (拒绝)。
3. **无缝执行**：如果没有加载拦截器，Engine 会以零延迟直接执行步骤，不会产生性能损耗。

### 5.6 IPluginContext (插件上下文)

在 `ActivateAsync` 中传入，插件应保存该引用以便后续使用。

```csharp
public interface IPluginContext
{
    string PluginDirectory { get; }
    ICommunicator? GetCommunicator(string protocolOrId);
    void PushEvent(int slotIndex, string address, PluginEventType eventType, string data);
    void NotifyConnectionStateChanged(string address, PluginDeviceConnectionState state);
}
```

> 💡 **说明**：`CancellationToken` 是 .NET 的取消机制。当用户点击“停止测试”时，此 token 会触发取消。在插件代码中应定期检查 `ct.ThrowIfCancellationRequested()` 或将其传递给异步方法。

### 5.7 CommAction (标准动作枚举)

```csharp
public enum CommAction
{
    Connect, Disconnect, Send, Read, Query, Status
}
```

### 5.8 便捷扩展方法

#### IPluginContext 扩展
| 方法 | 说明 |
|---|---|
| `PushResult(slot, addr, data)` | 等价于 `PushEvent` 且类型为 `Result` |

#### ICommunicator 扩展
| 方法 | 说明 |
|---|---|
| `SendAsync(...)` | 快速发送字符串 |
| `ReadAsync(...)` | 快速读取 |
| `ConnectAsync(...)` | 建立连接 |
| `DisconnectAsync(...)` | 断开连接 |
| `GetStatusAsync(...)` | 查询连接状态 |

### 5.9 日志与工具

#### LogLevel
```csharp
public enum LogLevel { Debug, Info, Warning, Error }
```

#### 常用扩展 (Utility Extensions)
SDK 提供 `StringExtension` 和 `ByteExtension`（如 `ToBool()`, `ToInt()`, `ToBytes()`, `ToHexStringWithSpaces()`），建议优先使用。

### 5.10 数据模型参考

#### TestRecord (测试记录)
通过 `Service.Slot(x).GetTestHistory()` 获取强类型的完整测试记录。

| 字段 | 类型 | 说明 |
|---|---|---|
| `Sn` | `string?` | 产品 SN |
| `Steps` | `IReadOnlyList<StepRecord>` | 各步骤执行结果 |

#### StepRecord (步骤结果)
| 字段 | 类型 | 说明 |
|---|---|---|
| `StepId` | `int` | 步骤唯一 ID |
| `StepName` | `string` | 步骤显示名称 |
| `Passed` | `bool` | 该步骤是否通过 |
| `IsTestItem` | `bool` | 是否为有效测试项（忽略辅助步骤） |
| `ElapsedMs` | `uint` | 执行耗时 |
| `ResultValue` | `string?` | 最终测量的结果值（纯净字符串） |
| `ResultSummary` | `string?` | 格式化的结果摘要（含上下限） |
| `Variables` | `IReadOnlyDictionary<string, string>` | 本步骤生成的变量快照 |
| `Check` | `CheckDetail?` | 强类型检查详情 |

#### CheckDetail 子类型 (实测数据)
| 类型 | 关键字段 | 说明 |
|---|---|---|
| `RangeCheck` | `Min`, `Max`, `Actual` | 范围检查 |
| `Threshold` | `Operator`, `Value`, `Actual` | 阈值检查 |
| `Contains` | `Substring`, `Actual` | 子串包含 |
| `Compare` | `Operator`, `ActualA`, `ActualB` | 双变量比较 |

#### FlowDefinition (流程定义)
通过 `Service.GetFlowDefinition()` 获取静态配置。

| 类型 | 字段 | 说明 |
|---|---|---|
| `FlowDefinition` | `Steps` | `IReadOnlyList<StepDefinition>` |
| `StepDefinition` | `StepName`, `StepKey` | 步骤标识 |
| | `CheckRule` | 强类型静态检查规则 |

#### CheckRuleDefinition 子类型 (静态规则)
| 类型 | 关键字段 | 说明 |
|---|---|---|
| `RangeRule` | `Min`, `Max` | 静态上下限配置 |
| `ThresholdRule` | `Operator`, `Value` | 静态阈值配置 |
| `ContainsRule` | `Substring` | 静态目标子串 |
| `CompareRule` | `Operator` | 变量比较规则 |


---

## 6. 完整示例：通讯器插件

本示例演示一个基于 `.NET SerialPort` 的通讯器插件。

```csharp
using System.Collections.Concurrent;
using System.IO.Ports;
using CatalyticKit;

namespace Acme.Serial;

public class SerialCommunicator : ICommunicator
{
    private readonly ConcurrentDictionary<string, SerialPort> _ports = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly object _mainLock = new();
    private IPluginContext? _context;

    public string Id => "acme.serial";
    public string Protocol => "serial";

    public async Task ActivateAsync(IPluginContext context)
    {
        _context = context;
        Service.AddPluginLog(Id, "串口插件已激活");
    }

    public Task DeactivateAsync()
    {
        lock (_mainLock)
        {
            foreach (var port in _ports.Values)
            {
                try { if (port.IsOpen) port.Close(); port.Dispose(); }
                catch { }
            }
            _ports.Clear();
            foreach (var s in _locks.Values) s.Dispose();
            _locks.Clear();
        }
        return Task.CompletedTask;
    }

    public async Task ExecuteTask(
        int slotIndex,
        string address,
        CommAction action,
        string payload,
        ExecuteOptions options,
        CancellationToken ct)
    {
        var semaphore = _locks.GetOrAdd(address, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            var port = GetOrCreatePort(address);
            
            switch (action)
            {
                case CommAction.Connect:
                    if (!port.IsOpen) port.Open();
                    break;
                case CommAction.Query:
                    var resp = await QueryAsync(slotIndex, port, payload, options.TimeoutMs, options.ResponseTerminator, ct);
                    _context?.PushResult(slotIndex, address, resp);
                    break;
                case CommAction.Send:
                    await SendAsync(slotIndex, port, payload, ct);
                    break;
                case CommAction.Read:
                    var r = await ReadAsync(slotIndex, port, options.TimeoutMs, options.ResponseTerminator, ct);
                    _context?.PushResult(slotIndex, address, r);
                    break;
                case CommAction.Disconnect:
                    await DisconnectAsync(slotIndex, port);
                    break;
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private Task DisconnectAsync(int slotIndex, SerialPort port)
    {
        if (port.IsOpen)
        {
            port.Close();
            Service.AddPluginLog(Id, $"[{port.PortName}] 已断开");
        }
        return Task.CompletedTask;
    }

    private Task SendAsync(int slotIndex, SerialPort port, string data, CancellationToken ct)
    {
        EnsureOpen(port);
        port.Write(data);
        return Task.CompletedTask;
    }

    private async Task<string> ReadAsync(int slotIndex, SerialPort port, int timeoutMs, string? terminator, CancellationToken ct)
    {
        EnsureOpen(port);
        port.ReadTimeout = timeoutMs > 0 ? timeoutMs : -1;

        // 如果指定了结束符，读取直到结束符
        if (!string.IsNullOrEmpty(terminator))
        {
            port.NewLine = terminator;
            return await Task.Run(() => port.ReadLine(), ct);
        }

        // 否则读取现有缓冲区数据
        return await Task.Run(() => port.ReadExisting(), ct);
    }

    private async Task<string> QueryAsync(int slotIndex, SerialPort port, string data, int timeoutMs, string? terminator, CancellationToken ct)
    {
        await SendAsync(slotIndex, port, data, ct);
        return await ReadAsync(slotIndex, port, timeoutMs, terminator, ct);
    }

    private void EnsureOpen(SerialPort port)
    {
        if (!port.IsOpen)
            throw new InvalidOperationException($"串口 {port.PortName} 未打开");
    }

    private SerialPort GetOrCreatePort(string portName)
    {
        lock (_mainLock)
        {
            if (_ports.TryGetValue(portName, out var port))
                return port;

            var newPort = new SerialPort(portName) { BaudRate = 9600 };
            _ports[portName] = newPort;
            return newPort;
        }
    }
}
```

### manifest.json

```json
{
  "id": "acme.serial",
  "name": "Acme Serial Communicator",
  "version": "1.0.0",
  "author": "Acme Corp",
  "entry": "Acme.Serial.dll",
  "capabilities": {
    "protocols": ["serial", "rs232"],
    "tasks": []
  }
}
```

- **id**: 插件唯一标识（建议使用反向域名格式）。必须与代码中 `Id` 属性返回值完全一致。
- **entry**: 插件入口 DLL 的文件名。
- **capabilities**: 插件的能力声明。
    - **protocols**: 仅通讯器插件需要，声明支持的协议标识。
    - **tasks**: 仅处理器插件需要，声明支持的任务名称。

---

## 7. 错误处理最佳实践

### 7.1 异常类型选择

| 异常类型 | 使用场景 |
|----------|----------|
| `ArgumentException` | 参数无效（如 JSON 解析失败）|
| `InvalidOperationException` | 操作顺序错误（如未连接就发送）|
| `TimeoutException` | 操作超时 |
| `IOException` | 通讯/文件错误 |
| `NotSupportedException` | 不支持的操作 |
| `OperationCanceledException` | 用户取消（由 CancellationToken 触发）|

### 7.2 正确使用 CancellationToken

```csharp
public async Task ExecuteAsync(int slotIndex, CancellationToken ct)
{
    // ✅ 正确：在长时间操作前检查
    ct.ThrowIfCancellationRequested();
    
    // ✅ 正确：传递给所有异步方法
    await Task.Delay(1000, ct);
    await File.ReadAllBytesAsync(path, ct);
    
    // ✅ 正确：在循环中检查
    for (int i = 0; i < 1000; i++)
    {
        ct.ThrowIfCancellationRequested();
        // ... 处理 ...
    }
}
```

### 7.3 不需要手动捕获的异常

Host 会自动捕获插件抛出的异常并：
- 将异常消息通过 `SubmitError` 返回给 Engine
- 在日志中记录详细信息

```csharp
// ✅ 正确：直接抛出，让 Host 处理
if (parameters == null)
{
    throw new ArgumentException("参数不能为空");
}

// ❌ 错误：不需要手动 try-catch 后返回错误码
try
{
    // ...
}
catch (Exception ex)
{
    return new byte[] { 0xFF }; // 不要这样做！
}
```

### 7.4 资源清理

```csharp
public async Task ExecuteTask(
    int slotIndex, 
    string address, 
    CommAction action, 
    string payload, 
    ExecuteOptions options, 
    CancellationToken ct)
{
    Stream? stream = null;
    try
    {
        stream = File.OpenRead(path);
        // ... 使用 stream ...
    }
    finally
    {
        // ✅ 确保资源被释放
        stream?.Dispose();
    }
}

// 或使用 using 语句
public async Task ExecuteAsync(int slotIndex, CancellationToken ct)
{
    using var stream = File.OpenRead(path);
    // ... 自动释放 ...
}
```

---

## 8. 高级功能

### 8.1 插件互调

处理器可以调用其他通讯器：

```csharp
public async Task ExecuteAsync(int slotIndex, CancellationToken ct)
{
    // 获取串口通讯器
    var serial = _context?.GetCommunicator("acme.serial");
    if (serial == null)
    {
        throw new InvalidOperationException("串口插件未加载");
    }
    
    // 使用扩展方法调用 (注意：扩展方法也返回 Task，不直接返回结果)
    await serial.ConnectAsync(slotIndex, "COM3", 5000, ct);
    await serial.SendAsync(slotIndex, "COM3", "MEAS:VOLT?\n", ct);
    await serial.ReadAsync(slotIndex, "COM3", 1000, ct);
    
    // 逻辑：处理器完成工作后直接退出，结果通过通讯器插件的 PushEvent 上报给引擎
}
```

### 8.2 访问插件目录资源

```csharp
public Task ActivateAsync(IPluginContext context)
{
    _context = context;
    
    // 读取插件附带的配置文件
    var configPath = Path.Combine(context.PluginDirectory, "config.json");
    if (File.Exists(configPath))
    {
        var config = File.ReadAllText(configPath);
        Service.AddPluginLog(Id, $"已加载配置: {config}");
    }
    
    return Task.CompletedTask;
}
```

### 8.3 推送异步事件

用于设备主动推送数据（如 CAN 帧监控、设备报警）或状态变更（如断线通知）。

**标准事件：设备断线**

当插件检测到设备连接意外断开时，**强烈建议**主动推送 `DeviceDisconnected` 事件，以便 Host 立即更新状态，而不是等待下一次心跳或操作失败。

```csharp
using CatalyticKit; 

public void OnConnectionLost(int slotIndex, string address)
{
    // 状态数据
    var data = "Disconnected";
    
    // 使用 Status 类型推送事件
    _context?.PushEvent(slotIndex, address, PluginEventType.Status, data);
    
    Service.AddPluginLog(Id, $"[{address}] 检测到断线，已通知 Host");
}
```

**自定义事件**

插件也可以定义自己的事件类型，供上层业务处理。
```csharp
public void OnDataReceived(int slotIndex, string address, string data)
{
    // 推送自定义事件 (包含 slotIndex 和 address)
    _context?.PushEvent(slotIndex, address, PluginEventType.Status, $"custom_data:{data}");
}
```

### 8.4 低代码模式数据推送 (Low-Code Data Push)

当使用 Catalytic Engine 的低代码模式（Engine Controlled）判断 Pass/Fail 时，Host 会使用特殊的 `FetchData` 指令来获取设备数据。为了确保 Engine 能正确解析数据（Regex/Numeric Check）：

> [!IMPORTANT]
> **约束**: 若要支持低代码判断，推送到 `DeviceData` 通道的数据 **必须是 UTF-8 编码的字符串或 JSON**。如果是私有二进制格式，低代码引擎将无法解析。

```csharp
// ✅ 正确：推送到 Host 内部缓冲区，供 Low-code Engine 进行判决
_context?.PushResult(slotIndex, address, "VOLT 5.003");
```

---

## 9. 调试与排查问题

### 9.1 使用日志

```csharp
Service.AddPluginLog(Id, "调试信息：变量值 = " + value);
Service.AddPluginLog(Id, "操作完成");
Service.AddPluginLog(Id, "警告：超时后重试");
Service.AddPluginLog(Id, "错误：连接失败");
```

日志会显示在 Catalytic UI 的系统日志面板中。

### 9.2 使用 Visual Studio 附加调试

1. 启动 Catalytic Host
2. 打开 Visual Studio，选择 **Debug > Attach to Process**
3. 找到 `Catalytic.Host` 进程
4. 在插件代码中设置断点
5. 触发插件执行，断点会命中

### 9.3 常见问题检查清单

| 问题 | 检查项 |
|------|--------|
| 插件未加载 | manifest.json 是否存在？格式是否正确？|
| 插件未找到 | manifest.json 中的 `id` 是否与代码中的 `Id` 一致？|
| DLL 加载失败 | 是否缺少依赖 DLL？.NET 版本是否匹配？|
| 超时 | `timeoutMs` 设置是否合理？设备是否响应？|

---

## 10. 依赖项管理与部署

### 10.1 依赖项管理规范

插件支持引用第三方托管库（Managed DLL）和原生库（Native DLL）。为了确保插件在 Host 中正确加载，必须遵循以下规范：
#### 1. .NET 版本要求
*   **目标框架**: 插件项目必须设置 `<TargetFramework>net10.0</TargetFramework>`。
*   **第三方托管库**: 引用的第三方 DLL 必须兼容 .NET 10（建议使用 `net10.0`、`net9.0` 或 `netstandard2.1`）。

#### 2. 原生库加载 (P/Invoke)
若插件需要调用 C/C++ 编写的原生库，请遵循以下原则：
*   **禁止硬编码路径**: 在 `[DllImport]` 中**仅允许指定库名**（例如 `VendorSDK`），严禁包含任何文件夹路径。
*   **自动寻址机制**: Host 会自动在插件所在的私有目录中搜索对应的库文件。
*   **跨平台支持**: Host 能够自动识别并补全不同平台的后缀（`.dll`、`.so`、`.dylib`）及 `lib` 前缀。
*   **架构匹配**: 原生库的指令集架构（如 x64）必须与 Host 进程的架构保持一致。

#### 3. 项目配置
若插件引用了 NuGet 包，请务必在 `.csproj` 中开启以下设置，以确保编译时所有依赖项都复制到插件目录下：
```xml
<PropertyGroup>
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>
```

### 10.2 历史遗留环境兼容性方案

若插件依赖的第三方组件与 .NET 10 存在底层不兼容（例如：仅支持 x86 架构的旧版驱动、过时的 .NET Framework 算法库等），建议采用以下**进程间通信 (IPC)** 方案进行解耦：

1.  **独立进程封装**: 将不兼容的组件封装为一个独立的外部执行程序（Executable）。
2.  **插件通信桥接**: 插件作为 Host 的调用入口，通过 Socket、命名管道（Named Pipe）或本地 HTTP 等方式与该独立进程进行交互。
3.  **职责分离**: 插件负责满足 Catalytic 接口规范，独立进程负责处理特定的底层兼容性逻辑。

*注：具体的 IPC 实现方式由开发者根据业务性能需求自行决定。*

### 10.3 部署目录结构

部署时，只需将插件目录整体拷贝至 Host 的 `plugins` 文件夹下。Host 保证每个插件的依赖环境是相互隔离的。

```
<Catalytic 工作目录>/
└── plugins/
    └── <插件 ID>/
        ├── manifest.json      (必须)
        ├── YourPlugin.dll     (必须)
        ├── ThirdParty.dll     (第三方托管依赖)
        ├── VendorSDK.dll      (原生依赖库)
        └── config.json        (可选配置文件)
```

### 10.4 部署注意

添加或更新插件后，需要**重启 Catalytic Host** 才能加载新插件。

---

## 11. 常见问题 FAQ

### Q1: manifest.json 格式报错

**症状**：日志显示"解析清单失败"

**解决**：
- 检查 JSON 格式（https://jsonlint.com/ 在线验证）
- 确保使用双引号，不是单引号
- 检查是否有多余的逗号

### Q2: 找不到 IPlugin 实现

**症状**：日志显示 "xxx 中没有找到 IPlugin 实现"

**解决**：
- 确保类实现了 `ICommunicator` 或 `IProcessor`
- 确保类是 `public` 的
- 确保类不是 `abstract`

### Q3: 协议冲突

**症状**：日志显示 "协议冲突: xxx 已被其他插件注册"

**解决**：
- 确保 `Protocol` 属性值是唯一的
- 如果确实需要多个串口驱动，使用不同的 protocol 名称（如 "serial-v1", "serial-v2"）

### Q4: 插件执行超时

**症状**：测试显示超时错误

**解决**：
- 检查设备是否正确响应
- 增加 UI 中配置的超时时间
- 在插件中添加日志，定位卡住的位置

### Q5: 取消操作不生效

**症状**：点击停止按钮后，插件仍在执行

**解决**：
- 确保在循环和长时间操作中检查 `ct.ThrowIfCancellationRequested()`
- 确保将 `CancellationToken` 传递给所有异步方法

### Q6: 缺少依赖 DLL

**症状**：日志显示 "加载程序集失败" 或 "找不到类型"

**解决**：
- 将所有依赖 DLL 复制到插件目录
- 使用 `dotnet publish` 发布自包含版本

```bash
dotnet publish -c Release --self-contained false
```

---

## 附录：SDK 文件清单

| 文件 | 说明 |
|------|------|
| `CatalyticKit.dll` | SDK 动态库 |
| `IPlugin.cs` | 插件接口定义 (`IPlugin` / `ICommunicator` / `IProcessor` / `IPluginContext`) |
| `FlowDefinition.cs` | 流程定义模型 (`FlowDefinition` / `StepDefinition` / `CheckRuleDefinition`) |
| `ISlot.cs` | Slot 操作接口 (命令 + 事件 + `GetTestHistory`) |
| `TestRecord.cs` | 测试历史记录模型 (`TestRecord` / `StepRecord` / `CheckDetail`) |
| `IHostBridge.cs` | Host 桥接接口 (Host 必须实现) |
| `ISlotEventHandler.cs` | Slot 事件回调接口 |
| `Service.cs` | 静态 Service 入口 (全局命令 + Slot 访问) |
| `SlotProxy.cs` | ISlot 内部实现 (线程安全事件 + 命令转发) |
| `ServiceNotInitializedException.cs` | Service 未初始化异常 |
| `TestFinishedEventArgs.cs` | 测试完成事件参数 |
| `IInterceptor.cs` | 拦截器接口定义 |
| `CommAction.cs` | 标准通讯动作枚举 |
| `CommunicatorExtensions.cs` | ICommunicator 便捷扩展方法 |
| `ContextExtensions.cs` | IPluginContext 便捷扩展方法 |
| `Extensions/ByteExtension.cs` | 字节数组工具 |
| `Extensions/StringExtension.cs` | 字符串工具 |

---

---

## 12. 通讯插件核心设计参考

### 12.1 典型场景与核心挑战

在多槽位（Multi-Slot）测试系统中，常常会遇到一种非对称架构：**所有槽位的整体动作由一个主控制器（如主控 PLC 或自研控制卡）统筹调度，而每个槽位又配备独立的检测仪器**。

在这种场景下，主控 PLC 在配置中被设置为**共享资源 (`IsShared = true`)**。当多个槽位并发执行测试步骤，同时向同一个主控 PLC 发送请求时，底层的物理通讯链路（如同一个 TCP 连接）会混杂来自各个槽位的指令。如果插件不对请求与响应进行精准匹配，必然导致**响应窜槽**（如 Slot 0 收到了本该给 Slot 1 的响应），进而引发测试逻辑的彻底崩塌。

### 12.2 前置要求：协议层面的槽位标识

> [!CAUTION]
> **硬件协议约束**
> 
> 要实现可靠的异步并发路由，**底层硬件的通讯协议在返回数据时，必须包含可识别的槽位标识符（Slot ID/Index）**。
> 例如：向 PLC 发送 `MOVE_CYLINDER 2`（控制 2 号槽位气缸），PLC 响应必须为 `CYLINDER_DONE 2`。
> 
> 如果硬件协议不支持携带槽位标识，软件层面上将**无法实现真正的异步并发共享**。此时，开发者只能在插件中对整个通讯链路施加全局互斥锁（Mutex/Lock），强制将并发降级为排队串行执行，这将严重损失系统的测试吞吐量（UPH）。

### 12.3 最佳实践：请求队列与异步路由机制

为了完美解决上述问题，建议通讯器插件采用 **"FIFO 请求队列 + 后台异步分发"** 架构。以下是核心逻辑解析：

#### 1. 状态记录入队（发送端）
当 `ExecuteTask` 被调用以发送指令时，切勿假定当前的 `IsShared` 状态会永远保持不变。必须在发送数据的瞬间，将当前请求的元数据存入对应地址的队列中：

```csharp
// 维护每个物理地址的请求队列
private readonly ConcurrentDictionary<string, ConcurrentQueue<(int slotIndex, bool isShared)>> _requestQueues = new();

// 发送时记录身份
_requestQueues.GetOrAdd(address, ...).Enqueue((slotIndex, options.IsShared));
await client.SendAsync(payload);
```

#### 2. 后台循环读取（接收端）
建立连接后，即刻启动一个完全独立的后台任务，持续监听底层流，将接收与发送解耦：

```csharp
_ = Task.Run(() => BackgroundReadLoop(address, client, cts.Token));
```

#### 3. 动态路由判决（分发端）
在收到底层响应报文后，从队列中弹出一个请求记录，并根据该请求当初的 `IsShared` 属性执行路由判决：

```csharp
if (_requestQueues[address].TryDequeue(out var request))
{
    int targetSlot;
    
    if (request.isShared)
    {
        // 【共享模式】：设备可能乱序响应。不能信任 request.slotIndex
        // 必须从硬件返回的报文中解析出设备侧的槽位号
        var match = Regex.Match(msg, @"\d+"); 
        if (!match.Success) continue; // 丢弃无法识别槽位的废弃帧
        
        int hardwareSlotNum = int.Parse(match.Value);
        targetSlot = hardwareSlotNum - 1; // 设备(1-based) 转换为 系统(0-based)
    }
    else
    {
        // 【独占模式】：严格一问一答。直接信任发送时记录的槽位
        targetSlot = request.slotIndex;
    }

    // 精准推送到目标槽位
    _context?.PushEvent(targetSlot, address, PluginEventType.Result, msg);
}
```

#### 机制优势总结
1. **防窜槽**：通过报文解析，确保共享响应 100% 路由至正确的测试流。
2. **状态解耦**：使用 FIFO 队列保存瞬间的并发状态，即使设备在“独占”与“共享”模式间被动态切换，路由逻辑也绝不会错乱。
3. **吞吐最大化**：主线程发送完指令即刻返回，不阻塞等待，依靠后台循环完成匹配，最大化并发效能。

---

> **最后更新**: 2026-04-22  