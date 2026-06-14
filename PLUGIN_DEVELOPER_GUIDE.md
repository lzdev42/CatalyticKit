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
- [13. ⚠️ 通讯插件最重要的契约：数据归属哪个 Slot（必读）](#13-通讯插件最重要的契约数据归属哪个-slot必读)

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
        Host.AddPluginLog(Id, "插件已激活");
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

### 5.6 Host API

| 方法 | 说明 |
|------|------|
| `Host.Slot(0).Start()` | 启动测试，返回 `StartResult` (Ok/Reason) |
| `Host.Slot(0).SubmitValue("3.31")` | 提交测量值（由引擎判决） |
| `Host.Slot(0).Report(true, "3.31")` | 直接提报结果和测量值 |
| `Host.Slot(0).GetCurrentStep()` | 获取当前步骤配置 (Step) |
| `Host.GetFlowDefinition()` | 获取全量流程配置 (TestFlow) |
| `Host.NotifySlotFinished` | 全局静态事件，用于监听任一槽位完成 |
| `Host.NotifySlotStarted` | 全局静态事件，用于监听任一槽位启动 |

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

## 7. 错误处理与启动校验

### 7.1 启动校验 (StartResult)
调用 `Host.Slot(i).Start()` 现在返回一个 `StartResult` 结构。这允许插件或宿主在真正开始前拦截错误：
- **越界校验**: Slot 索引是否合法。
- **状态校验**: 该 Slot 是否已在运行。
- **设备校验**: 绑定的硬件设备是否全部在线。

```csharp
var result = Host.Slot(0).Start();
if (!result.Ok) {
    Host.AddPluginLog(Id, $"启动失败: {result.Reason}");
    return;
}
```

### 7.2 运行时异常
直接抛出标准 .NET 异常即可，Host 会捕获并将其上报给引擎和 UI。

---

## 8. 高级功能

### 数据模型

- **TestFlow / Step**: 流程与步骤的静态配置。
- **TestRecord / StepRecord**: 测试执行的完整历史数据。
- **CheckResult**: 包含实测值与判决详情的强类型模型。
- **StartResult**: 用于同步反馈启动校验结果的模型。

---

## 9. 调试与排查问题

建议使用 `Host.AddPluginLog` 记录调试信息，Host 会将其分流到独立的插件日志文件中。

---

## 10. 部署插件

将插件 DLL 和 `manifest.json` 放入 `plugins/插件ID/` 目录即可。

---

## 13. ⚠️ 通讯插件最重要的契约：数据归属哪个 Slot（必读）

> *(本章新增于 2026-05-31。如果你只读这份文档的一章，请读这一章。)*
>
> **郑重声明：通讯插件 99% 的线上事故，根源都在本章描述的问题上。**
> **不读本章直接写通讯插件，几乎必然踩坑——而且是那种"测试时好好的，上多工位才偶发串台"的最难查的坑。**

### 13.1 一句话契约

> **通讯插件唯一的、不可推卸的硬性责任：**
> **你通过 `ReportData(slotIndex, address, data)` 上报数据时，`slotIndex` 必须是这坨数据真正归属的工位。**
> **你说是 Slot 几的，Host 就原样交给 Slot 几去判决。Host 绝对信任你，绝对不做二次校验。**
> **所以你报错了，数据就串台了，测试结果就错了，而且 Host 不会报错、不会拦截。**

### 13.2 为什么这是你的责任，而不是 Host 的责任

职责边界划得很清楚：

| 角色 | 负责什么 | 不负责什么 |
|------|---------|-----------|
| **Host / 引擎** | 知道"要发什么命令"、"该用哪个解析规则"、"该判 Pass/Fail" | **不知道你的设备怎么返回数据** |
| **通讯插件（你）** | 知道"我的设备怎么收发、怎么把响应拆解对应到工位" | 不需要懂测试逻辑、不需要懂判决规则 |

**核心原因：设备到底怎么返回数据，全世界只有你（插件开发者）知道。**

Host 不可能知道你的设备：
- 是请求一条、响应一条（FIFO 配对）
- 还是一次下发、一次性返回所有工位的数据
- 还是响应里自带工位号（如 `HOME_OK 1`）
- 还是按某个固定通道顺序返回
- 还是乱序、还是主动推送

正因为只有你知道，所以"把设备的原始响应翻译成『这坨数据属于 Slot X』"这件事，**只能由你做，Host 帮不了你，也不该帮你。**

### 13.3 Host 给你的 = 你要还给 Host 的

发命令时，Host 通过 `Execute(slotIndex, address, ...)` 告诉你：
> "这次任务是 Slot `slotIndex` 的，发到 `address`。"

但**这个 slotIndex 只在『请求-响应一一对应』时才能直接拿来回传。** 一旦你的设备不是这种简单模式（见 13.5），你就不能无脑用 `Execute` 收到的 slotIndex 去 `ReportData`，必须自己重新判断。

`ReportData` 的签名再强调一遍：
```csharp
void ReportData(int slotIndex, string address, string data);
```
- `slotIndex`：**这坨 data 真正属于的工位**（你的责任，必须算对）
- `address`：设备地址（辅助信息）
- `data`：原始响应数据，Host 会用配置好的规则去解析判决

**如果一次响应包含多个工位的数据，就调多次 `ReportData`，一个工位一次：**
```csharp
// 设备一次返回了 "3.3,3.4,3.5"，分别是 slot 0/1/2
ReportData(0, address, "3.3");
ReportData(1, address, "3.4");
ReportData(2, address, "3.5");
```

### 13.4 一个真实的反面教材（串口插件曾经的坑）

串口在工业场景里常常是**一个控制器带多个工位**（如 RS-485 总线、多通道继电器板）：
一根 COM3 线，背后是一个控制器，Slot 0 / Slot 1 / Slot 2 共用它。

**错误写法（曾经的串口插件）：**
```csharp
// 在 Connect 时把 slotIndex 闭包捕获进 DataReceived 事件
private async Task HandleConnect(int slotIndex, string portName, ...)
{
    // ...
    wrapper.SetDataReceivedHandler((sender, e) => {
        string line = ReadLine(sender);
        _channel?.ReportData(slotIndex, portName, line);  // ❌ 用的是"连接时"的 slotIndex
    });
}
```

**为什么错：**
1. Slot 0 先连 COM3，闭包捕获了 `slotIndex = 0`
2. Slot 1 再连 COM3，发现端口已开，直接 `return`，闭包里的 0 没被更新
3. 之后无论 Slot 1 还是 Slot 2 的数据回来，`DataReceived` 都上报 `ReportData(0, ...)`
4. **所有工位的数据全部串到了 Slot 0** —— 而且 Host 不会报错，测试照常跑，结果全错

**根因：`DataReceived` 是异步触发的，它没有"当前是哪个工位在请求"的上下文，**
**而开发者错误地以为"连接时的工位 = 数据回来时的工位"。在单工位独占时这碰巧成立，多工位共用时立刻崩。**

### 13.5 设备返回模式 → 正确处理策略对照表

| 设备返回模式 | 你该怎么算 slotIndex |
|------------|---------------------|
| **请求一条，响应一条，严格配对（FIFO）** | 维护一个"请求队列"，发请求时把 `slotIndex` 入队，收响应时出队取回 |
| **响应里自带工位标识**（如 `HOME_OK 1`） | 从响应内容里解析出工位号（注意设备可能是 1-based，要转 0-based） |
| **一次返回所有工位的数据**（如 CSV `3.3,3.4,3.5`） | 按位置拆分，逐个 `ReportData`，一个工位一次 |
| **按固定通道顺序返回** | 用你预先知道的"通道→工位"映射表换算 |
| **设备主动推送，不对应任何请求** | 用你的协议约定判断归属；实在无法判断的，记日志并丢弃，不要瞎猜 |

> 没有"唯一正确做法"。**怎么翻译是你的自由，但翻译对是你的责任。**

### 13.6 推荐的通用骨架：请求队列（FIFO）

对于"请求-响应配对"和"共享设备"，最稳的通用做法是 **per-address 请求队列**：

```csharp
// Key = 设备地址；Value = 该地址上「已发出、等响应」的请求队列
// 队列元素记录发请求时的 slotIndex（以及任何你需要的上下文）
private readonly ConcurrentDictionary<string, ConcurrentQueue<int>> _pending = new();

public async Task Execute(int slotIndex, string address, CommAction action,
                          string payload, CommOptions options, CancellationToken ct)
{
    if (action == CommAction.Send || action == CommAction.Query)
    {
        // 发请求时入队，记住这次是哪个工位
        _pending.GetOrAdd(address, _ => new ConcurrentQueue<int>()).Enqueue(slotIndex);
        await SendToDevice(address, payload);
    }
    // ... 其余 action
}

// 后台读取循环 / DataReceived 回调里：
private void OnDeviceResponse(string address, string raw)
{
    if (_pending.TryGetValue(address, out var q) && q.TryDequeue(out var slotIndex))
    {
        // FIFO：这条响应对应最早那次未完成的请求
        _channel?.ReportData(slotIndex, address, raw);
    }
    else
    {
        // 队列空 = 设备主动推送或多余响应，按你的协议决定丢弃或特殊处理
        Host.AddPluginLog(Id, $"[警告] {address} 收到无主响应，已丢弃: {raw}");
    }
}
```

> 共享设备如果响应里自带工位号，就在出队后**用响应里的工位号覆盖队列里的 slotIndex**，
> 队列只用来保证"有响应可配"，最终归属以设备返回的工位号为准。

### 13.7 `IsShared` 标志的正确理解

`CommOptions.IsShared` 只是 Host 给你的**提示**：当前这个设备类型被配置成了"多工位共享"。

- 它**不强制**你用任何特定策略
- 它只是提醒你："这个设备会被多个工位并发使用，你处理 slot 归属时要格外小心"
- 怎么处理共享，依然是你的自由（响应解析 / FIFO 队列 / 通道映射都行）

### 13.8 自检清单（写完通讯插件后逐条对照）

- [ ] 我上报的 `slotIndex` 是"数据真正归属的工位"，不是"连接时的工位"
- [ ] 我的设备如果一个控制器带多工位，我没有用闭包捕获连接时的 slotIndex
- [ ] 如果是请求-响应配对，我用了队列（或等价机制）保证顺序匹配
- [ ] 如果一次响应含多工位数据，我拆开调了多次 `ReportData`
- [ ] 设备主动推送 / 无主响应时，我没有瞎猜工位，而是记日志或按协议处理
- [ ] 我清楚：Host 完全信任我报的 slotIndex，报错了不会有任何人提醒我

---

> **最后再强调一次：**
> **"这坨数据是哪个工位的" —— 这个判断只有你能做，也只有你该做。**
> **Host 把判决、解析、跳转都包了，只把"归属判断"这一件事留给你，因为只有你懂你的设备。**
> **请认真对待 `ReportData` 的第一个参数。**
