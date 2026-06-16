# Catalytic 插件开发指南

---

## 1. 概述

Catalytic 采用插件架构。所有与硬件通讯、自定义业务逻辑、流程拦截的功能都通过插件实现。

### 三种插件类型

| 类型 | 接口 | 职责 | 典型场景 |
|------|------|------|----------|
| 通讯器 | `ICommunicator` | 与设备通讯 | TCP Socket、串口、VISA、Modbus |
| 处理器 | `IProcessor` | 执行自定义业务逻辑 | 报告生成、数据转换、扫码 |
| 协调器 | `ICoordinator` | 拦截步骤执行 | 安全门确认、条件跳过、步骤过滤 |

通讯器是数量最多、逻辑最复杂的插件类型。[插件运行机制](PLUGIN_SYSTEM.md) 详细解释了通讯器的数据流转和 slotIndex 归属契约——写通讯器插件前务必阅读。

---

## 2. 环境与项目搭建

### 前置条件

- .NET SDK 10.0+
- 任意代码编辑器

### 创建项目

```bash
dotnet new classlib -n MyPlugin -f net10.0
cd MyPlugin
dotnet add package CatalyticKit --version *
```

### 项目配置

确保 `.csproj` 中目标框架为 `net10.0`，并将 `manifest.json` 设为复制到输出目录：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CatalyticKit" Version="*" />
  </ItemGroup>

  <ItemGroup>
    <None Update="manifest.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

---

## 3. manifest.json

每个插件目录必须包含一个 `manifest.json`，Host 启动时据此发现和加载插件。

```json
{
    "id": "catalytic.socket-client",
    "name": "Generic Socket Client",
    "version": "1.0.0",
    "entry": "SocketClient.dll",
    "capabilities": {
        "protocols": ["tcp", "udp"],
        "tasks": []
    }
}
```

### 字段说明

| 字段 | 必填 | 说明 |
|------|------|------|
| `id` | 是 | 全局唯一标识，格式 `publisher.name`，全小写 + 连字符 |
| `name` | 是 | 显示名称 |
| `version` | 是 | 语义化版本号 |
| `entry` | 是 | 入口 DLL 文件名 |
| `capabilities.protocols` | 否 | 通讯器支持的协议列表 |
| `capabilities.tasks` | 否 | 处理器支持的任务列表 |

`id` 必须与代码中的 `Id` 属性一致，否则 Host 无法匹配。

---

## 4. 接口参考

以下签名直接取自 SDK 源码，是唯一准确的 API 定义。

### 4.1 IPlugin（基础接口）

所有插件必须实现此接口。

```csharp
public interface IPlugin
{
    string Id { get; }
    Task ActivateAsync(ICommChannel channel);
    Task DeactivateAsync();
}
```

- `Id`：全局唯一标识，必须与 `manifest.json` 中的 `id` 一致。
- `ActivateAsync`：Host 加载插件时调用一次，在此初始化资源、保存 `channel` 引用。
- `DeactivateAsync`：Host 关闭时调用，在此释放资源。

### 4.2 ICommunicator（通讯器）

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

- `Protocol`：支持的协议名，用于 Host 匹配（如 `"tcp"`、`"serial"`）。
- `Execute`：Host 调用此方法执行通讯任务。**结果通过 `ICommChannel.ReportData()` 上报，不通过返回值。** 返回值为 `Task`，不是 `Task<T>`。

**参数说明：**

| 参数 | 类型 | 说明 |
|------|------|------|
| `slotIndex` | `int` | 发起本次请求的槽位索引（0-based） |
| `address` | `string` | 设备地址，格式由协议决定 |
| `action` | `CommAction` | 操作类型枚举 |
| `payload` | `string` | 发送给设备的命令内容 |
| `options` | `CommOptions` | 执行选项（超时、终止符、共享标志） |
| `ct` | `CancellationToken` | 取消令牌 |

### 4.3 CommAction（操作类型枚举）

```csharp
public enum CommAction
{
    Connect,     // 建立连接
    Disconnect,  // 断开连接
    Send,        // 发送数据（不等响应）
    Read,        // 读取当前可用数据
    Query,       // 发送 + 读取
    Status       // 查询连接状态
}
```

### 4.4 CommOptions（执行选项）

```csharp
public class CommOptions
{
    public int TimeoutMs { get; set; }            // 超时时间（毫秒）
    public string? CommandTerminator { get; set; }  // 发送命令的终止符
    public string? ResponseTerminator { get; set; }  // 响应数据的结束符
    public bool IsShared { get; set; }            // 是否为共享设备
}
```

- `CommandTerminator`：追加到发送命令末尾的终止符（如 `"\n"`）。
- `ResponseTerminator`：判断一条响应是否结束的终止符（如 `"\n"`）。
- `IsShared`：提示当前设备被多槽位共享，但不强制任何处理策略——slotIndex 归属判断始终是插件的责任。

### 4.5 IProcessor（处理器）

```csharp
public interface IProcessor : IPlugin
{
    string Command { get; }
    Task ExecuteAsync(int slotIndex, CancellationToken ct);
}
```

- `Command`：任务名称，用于 Host 在流程配置中匹配此处理器。
- `ExecuteAsync`：执行业务逻辑，通过 `Host.Slot(slotIndex)` 的方法上报结果。

### 4.6 ICoordinator（协调器）

```csharp
public interface ICoordinator : IPlugin
{
    Task<bool> BeforeStepAsync(int slotIndex, int stepId, string stepName, CancellationToken ct);
    Task AfterStepAsync(int slotIndex, int stepId, string stepName, bool passed);
}
```

- `BeforeStepAsync`：步骤执行前调用。返回 `true` 允许执行，返回 `false` 则步骤标记为 Fail。
- `AfterStepAsync`：步骤执行后调用，纯通知，不影响步骤结果。
- 全局只允许加载一个 `ICoordinator`。

### 4.7 ICommChannel（通讯通道）

在 `ActivateAsync` 中由 Host 注入，保存引用供后续使用。

```csharp
public interface ICommChannel
{
    string PluginDirectory { get; }
    ICommunicator? GetCommunicator(string protocolOrId);
    void ReportData(int slotIndex, string address, string data);
    void NotifyState(string address, DeviceState state);
}
```

| 方法 | 说明 |
|------|------|
| `PluginDirectory` | 插件目录路径，用于访问附带资源文件 |
| `GetCommunicator` | 获取其他通讯器实例（插件互调） |
| `ReportData` | **向 Host 上报设备响应数据**，Host 将数据交给引擎判决 |
| `NotifyState` | 通知 Host 设备连接状态变化 |

**`ReportData` 是通讯器输出数据的唯一途径。** `slotIndex` 必须是数据真正归属的槽位——这是通讯器插件的核心契约，详见[插件运行机制](PLUGIN_SYSTEM.md)。

### 4.8 CommunicatorExtensions（便捷扩展方法）

SDK 提供扩展方法简化常见操作，每个方法都包含 `slotIndex` 参数：

```csharp
// 发送数据
await communicator.SendAsync(slotIndex, address, data, ct);

// 读取数据
await communicator.ReadAsync(slotIndex, address, timeoutMs: 1000, ct);

// 建立连接
await communicator.ConnectAsync(slotIndex, address, timeoutMs: 5000, ct);

// 断开连接
await communicator.DisconnectAsync(slotIndex, address, ct);

// 查询状态
await communicator.GetStatusAsync(slotIndex, address, ct);
```

### 4.9 DeviceState（设备状态枚举）

```csharp
public enum DeviceState
{
    Connected,
    Disconnected
}
```

---

## 5. Host API

插件通过 `Host` 静态类访问主程序服务。所有方法线程安全。

### 5.1 全局方法

| 方法 | 说明 |
|------|------|
| `Host.AddPluginLog(id, msg)` | 记录插件专属日志，分流到独立日志文件 |
| `Host.StartAll()` | 启动所有槽位测试 |
| `Host.StopAll()` | 停止所有槽位测试 |
| `Host.ResetAll()` | 重置所有槽位状态 |
| `Host.GetSlotCount()` | 获取总槽位数 |
| `Host.GetAllSlots()` | 获取所有槽位的 `ISlot` 实例 |
| `Host.GetFlowDefinition()` | 获取测试流程定义（`TestFlow?`） |
| `Host.ReportFolder()` | 获取报告输出目录的绝对路径 |
| `Host.TestInfo` | 全局测试会话信息（`Operator`、`Build`） |

### 5.2 全局事件

```csharp
// 槽位测试完成时触发
Host.NotifySlotFinished += (TestFinishedEventArgs args) => {
    // args.SlotIndex, args.Passed, args.ErrorMessage
};

// 槽位测试开始时触发
Host.NotifySlotStarted += (int slotIndex) => {
    // ...
};
```

### 5.3 ISlot（槽位操作）

通过 `Host.Slot(index)` 获取：

```csharp
var slot = Host.Slot(0);
```

| 方法 | 说明 |
|------|------|
| `Start()` | 启动测试，返回 `StartResult`（含 `Ok` 和 `Reason`） |
| `Start(sn)` | 设置 SN 并启动测试 |
| `Stop()` | 停止测试 |
| `Reset()` | 重置测试状态 |
| `SetSn(sn)` | 设置产品 SN，返回 `ISlot` 支持链式调用 |
| `GetSn()` | 获取产品 SN |
| `GetVariable(name)` | 获取流程变量（返回 JSON 字符串） |
| `GetTestHistory()` | 获取完整测试记录（`TestRecord?`） |
| `GetCurrentStep()` | 获取当前步骤配置（`Step?`） |
| `SubmitValue(value)` | 提交测量值，由引擎判决 |
| `Report(passed, value, reason?)` | 直接提报结果和测量值 |
| `ReportPass()` | 报告当前步骤通过 |
| `ReportFail(reason)` | 报告当前步骤失败 |

### 5.4 StartResult（启动结果）

```csharp
public readonly record struct StartResult(bool Ok, string? Reason = null);
```

```csharp
var result = Host.Slot(0).Start();
if (!result.Ok) {
    Host.AddPluginLog(Id, $"启动失败: {result.Reason}");
    return;
}
```

---

## 6. 数据模型

### 6.1 TestFlow / Step（静态流程配置）

```csharp
public record TestFlow
{
    public IReadOnlyList<Step> Steps { get; init; }
}

public record Step
{
    public int StepId { get; init; }
    public string StepName { get; init; }
    public string StepLabel { get; init; }
    public bool IsTestItem { get; init; }
    public CheckRule? CheckRule { get; init; }
    public string? Params { get; init; }       // 扩展模式参数（Base64 已解码）
}
```

- `IsTestItem` 为 `false` 的是辅助步骤（初始化、延时），不计入报告统计。
- `Params` 仅扩展模式有值，由插件自行解析。

### 6.2 TestRecord / StepRecord（测试执行记录）

```csharp
public record TestRecord
{
    public string? Sn { get; init; }
    public IReadOnlyList<StepRecord> Steps { get; init; }
}

public record StepRecord
{
    public int StepId { get; init; }
    public string StepName { get; init; }
    public bool Passed { get; init; }
    public bool IsTestItem { get; init; }
    public uint ElapsedMs { get; init; }
    public string? ResultValue { get; init; }
    public string? ResultSummary { get; init; }
    public string? ErrorMessage { get; init; }
    public CheckResult? Check { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; }
}
```

### 6.3 CheckRule / CheckResult（检查规则与结果）

使用 C# pattern matching 按类型访问：

```csharp
// 检查规则（静态配置）
if (step.CheckRule is CheckRule.RangeRule r)
    Console.WriteLine($"Min={r.Min}, Max={r.Max}");

if (step.CheckRule is CheckRule.ThresholdRule t)
    Console.WriteLine($"{t.Operator} {t.Value}");

// 检查结果（运行时）
if (record.Check is CheckResult.RangeCheck rc)
    Console.WriteLine($"Min={rc.Min}, Max={rc.Max}, Actual={rc.Actual}");

if (record.Check is CheckResult.Threshold th)
    Console.WriteLine($"{th.Operator} {th.ThresholdValue}, Actual={th.Actual}");
```

完整子类型：`RangeRule`/`ThresholdRule`/`ContainsRule`/`CompareRule`/`UnknownRule`，以及对应的 `RangeCheck`/`Threshold`/`Contains`/`Compare`/`Unknown`。

---

## 7. 部署

将编译产物放入 Host 的 `plugins/` 目录：

```
plugins/
└── catalytic.socket-client/
    ├── manifest.json
    └── SocketClient.dll
```

- 目录名 = 插件 ID
- 每个插件独占一个目录
- 如有私有依赖 DLL，一并放入
- 添加或更新插件后需重启 Host

构建命令：

```bash
dotnet build -c Release
# 或发布自包含版本（包含所有依赖）
dotnet publish -c Release --self-contained false
```

---

## 8. 参考实现

本仓库包含四个经过验证的插件示例：

| 插件 | 类型 | 说明 |
|------|------|------|
| [SocketClient](SocketClient/SocketClient/) | 通讯器 | TCP 客户端，支持多槽位并发，含请求队列路由 |
| [CatalyticSerialPort](CatalyticSerialPort/CatalyticSerialPort/) | 通讯器 | 串口通讯，支持终止符模式 |
| [CsvReporter](CsvReporter/CsvReporter/) | 处理器 | 生成 CSV 报告，演示 Host API 使用 |
| [RemoteController](RemoteController/RemoteController/) | 协调器 | 远程压测控制器，演示事件订阅与槽位启动 |
