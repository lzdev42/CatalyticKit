# Catalytic 插件运行机制

本文解释插件系统的内部运转逻辑。理解这些机制是写出正确插件——尤其是通讯器插件——的前提。

---

## 1. 插件生命周期

```
Host 启动
  │
  ├─ 扫描 plugins/ 目录
  ├─ 读取每个子目录的 manifest.json
  ├─ 加载 entry 指定的 DLL
  ├─ 反射查找 IPlugin 实现，实例化
  ├─ 调用 ActivateAsync(channel)    ← 在此保存 channel、初始化资源
  │
  ├─ 运行中：Host 调用 Execute / ExecuteAsync
  │
  └─ Host 关闭时调用 DeactivateAsync() ← 在此释放资源
```

关键点：

- `ActivateAsync` 接收的 `ICommChannel` 是插件与 Host 通信的唯一通道，必须保存。
- 插件实例在 Host 运行期间始终存活，不会被创建/销毁多次。
- 插件抛出的未处理异常由 Host 捕获并上报，不会导致 Host 崩溃。

---

## 2. 通信模型：数据如何流转

通讯器插件的核心职责是：接收 Host 的指令，与设备交互，将设备响应上报给 Host。

```
Engine（判定 Pass/Fail）
  ↑ ReportData(slotIndex, address, data)
  │
Host（调度层）
  ↓ Execute(slotIndex, address, action, ...)
  │
通讯器插件（你）
  ↕ 与设备通讯
设备
```

**关键约束：通讯器的 `Execute` 返回 `Task`，不返回数据。** 所有设备响应必须通过 `_channel.ReportData()` 上报。

这个设计的意义在于：设备响应可能是异步到达的（如串口 DataReceived 事件、TCP 后台读取线程），不一定在 `Execute` 调用期间就能拿到。`ReportData` 使得你可以在任意时刻、任意线程上报数据。

---

## 3. 插件发现与匹配

Host 通过两种方式为设备分配通讯器插件：

1. **指定 plugin_id**：设备配置中显式指定了 `plugin_id`，直接使用该插件。
2. **按协议匹配**：未指定 `plugin_id` 时，根据设备的 `protocol` 字段匹配插件的 `Protocol` 属性。

配置示例：

```json
{
  "dmm": {
    "protocol": "unused",
    "plugin_id": "catalytic.socket-client"
  },
  "power_supply": {
    "protocol": "serial"
  }
}
```

---

## 4. slotIndex 归属契约

这是通讯器插件最重要、也是最容易出错的设计点。

### 核心规则

**`ReportData(slotIndex, address, data)` 中的 `slotIndex` 必须是数据真正归属的槽位。Host 信任你传入的值，不做二次校验。报错了，数据就串到错误的槽位，测试结果就错了，且不会有任何报错或拦截。**

### 为什么是插件的责任

Host 知道"要发什么命令"、"该用哪个解析规则"、"该判 Pass/Fail"。但 Host 不知道：

- 你的设备是请求一条响应一条（FIFO 配对），还是一次返回所有工位数据
- 响应里是否自带工位号（如 `HOME_OK 1`）
- 数据是按固定通道顺序返回，还是乱序或主动推送

这些只有你（插件开发者）知道，所以归属判断只能由你做。

### Execute 传入的 slotIndex 能直接用吗

**只在请求-响应严格一一对应时可以。**

`Execute(slotIndex, ...)` 中的 `slotIndex` 表示"这次任务是哪个槽位发起的"。如果你的设备是"发一条、回一条"的简单模式，你可以直接用这个值调用 `ReportData`。

但如果你的设备不是这种模式（共享设备、异步响应、批量返回），你就不能无脑回传，必须自己判断数据归属。

---

## 5. 独享模式 vs 共享模式

### 独享模式（IsShared = false）

一个设备地址绑定到一个槽位。响应直接路由到绑定的槽位。

```
Slot 0 → address="192.168.1.100:5025" → 设备 → 响应 → ReportData(0, ...)
Slot 1 → address="192.168.1.101:5025" → 设备 → 响应 → ReportData(1, ...)
```

即使多个槽位使用同一地址，也是简单的后发覆盖映射。

### 共享模式（IsShared = true）

同一设备地址被多个槽位并发使用。设备的响应中包含槽位标识（如 `HOME_OK 1`），需要从响应内容中解析归属。

```
Slot 0 ─┐
Slot 1 ─┼→ address="192.168.1.100:5025" → 设备 → "HOME_OK 1" → 解析为 Slot 0
Slot 2 ─┘                                  → "HOME_OK 2" → 解析为 Slot 1
```

**注意：设备侧通常使用 1-based 编号，系统使用 0-based，需要减 1 转换。**

`CommOptions.IsShared` 只是 Host 给你的提示，不强制任何处理策略。怎么处理共享，仍然是你的自由。

---

## 6. 请求队列机制（FIFO）

对于请求-响应配对的场景，推荐使用 **per-address 请求队列** 来匹配响应归属。

### 为什么需要队列

不能简单地用 `address → slotIndex` 的映射，因为：

- 同一设备可能被多个槽位并发请求，必须按 FIFO 顺序匹配
- 每次请求的 `isShared` 状态可能不同，必须记录请求时的状态
- 连接时记录的状态不能代表后续所有请求

### 实现方式（取自 SocketClient 插件）

```csharp
// Key = 设备地址，Value = 请求队列
// 队列元素记录 (slotIndex, isShared)
private readonly ConcurrentDictionary<string, ConcurrentQueue<(int slotIndex, bool isShared)>> _requestQueues = new();

// Execute 中：发送请求时入队
public async Task Execute(int slotIndex, string address, CommAction action,
                          string payload, CommOptions options, CancellationToken ct)
{
    switch (action)
    {
        case CommAction.Send:
        case CommAction.Query:
            if (slotIndex >= 0)
            {
                // 记录本次请求的槽位和共享模式
                _requestQueues.GetOrAdd(address, _ => new ConcurrentQueue<(int, bool)>())
                              .Enqueue((slotIndex, options.IsShared));
            }
            await SendToDevice(address, payload);
            break;
    }
}

// 后台读取线程中：收到响应时出队
private void OnDeviceResponse(string address, string raw)
{
    if (_requestQueues.TryGetValue(address, out var queue) && queue.TryDequeue(out var request))
    {
        int targetSlot;
        if (request.isShared)
        {
            // 共享模式：从响应内容解析槽位号（设备 1-based → 系统 0-based）
            var match = _slotRegex.Match(raw);
            targetSlot = int.Parse(match.Value) - 1;
        }
        else
        {
            // 独享模式：直接使用请求时记录的 slotIndex
            targetSlot = request.slotIndex;
        }

        _channel?.ReportData(targetSlot, address, raw);
    }
    else
    {
        // 队列空 = 设备主动推送或多余响应，记录日志并按协议决定处理方式
        Host.AddPluginLog(Id, $"[WARN] {address} 收到无主响应: {raw}");
    }
}
```

---

## 7. 常见设备返回模式及处理策略

| 设备返回模式 | 处理方式 |
|------------|---------|
| 请求-响应严格配对（FIFO） | 维护请求队列，发请求时 slotIndex 入队，收响应时出队取回 |
| 响应中自带工位标识（如 `HOME_OK 1`） | 从响应内容解析工位号（注意 1-based 转 0-based） |
| 一次返回所有工位数据（如 `3.3,3.4,3.5`） | 按位置拆分，逐个 `ReportData`，每个工位一次 |
| 按固定通道顺序返回 | 用预先知道的通道→工位映射表换算 |
| 设备主动推送，不对应任何请求 | 用协议约定判断归属；无法判断的，记录日志并丢弃 |

---

## 8. 典型错误：闭包捕获 slotIndex

串口/Socket 通讯器常见的一种错误是在连接时把 `slotIndex` 闭包捕获到 `DataReceived` 回调中：

```csharp
// 错误做法
private async Task HandleConnect(int slotIndex, string portName, ...)
{
    wrapper.SetDataReceivedHandler((sender, e) => {
        string line = ReadLine(sender);
        _channel?.ReportData(slotIndex, portName, line);  // 用的是连接时的 slotIndex
    });
}
```

**问题：** Slot 0 先连接，闭包捕获 `slotIndex = 0`。Slot 1 再连接同一设备时发现已连接直接返回，闭包中的 0 没有更新。之后所有数据都上报到 Slot 0。

**正确做法：** 使用请求队列，在发送时记录 slotIndex，在收到响应时从队列取回。参见第 6 节。

---

## 9. 终止符机制

`CommOptions` 提供两个独立的终止符字段：

| 字段 | 作用 | 典型值 |
|------|------|--------|
| `CommandTerminator` | 追加到发送命令末尾 | `"\n"`、`"\r\n"` |
| `ResponseTerminator` | 判断一条响应是否结束 | `"\n"`、`"\r\n"` 或自定义字符串 |

两者的含义不同：`CommandTerminator` 是"发出去时追加的"，`ResponseTerminator` 是"收回来时判断帧结束的"。实际使用中两者值可能相同，但语义是独立的。

如果 `ResponseTerminator` 为空，插件应立即返回当前缓冲区所有可用数据（Raw 模式），交给上层处理。

---

## 10. 插件互调

处理器插件可以通过 `ICommChannel.GetCommunicator(protocolOrId)` 获取其他通讯器实例，然后通过 `CommunicatorExtensions` 调用其方法：

```csharp
public async Task ExecuteAsync(int slotIndex, CancellationToken ct)
{
    var serial = _channel?.GetCommunicator("serial");
    if (serial == null)
        throw new InvalidOperationException("串口插件未加载");

    await serial.ConnectAsync(slotIndex, "COM3", 5000, ct);
    await serial.SendAsync(slotIndex, "COM3", "MEAS:VOLT?\n", ct);
    await serial.ReadAsync(slotIndex, "COM3", 1000, ct);
}
```

注意：扩展方法的第一个参数始终是 `slotIndex`，确保数据路由正确。

---

## 11. 线程安全

| 组件 | 保证 |
|------|------|
| `Host` 静态方法 | 线程安全 |
| `Host` 事件订阅/取消订阅 | 线程安全（内部加锁） |
| `IHostBridge` 实现 | Host 侧保证线程安全 |
| 插件自身的 `Execute` | **可能被多线程并发调用**，需自行保证线程安全 |

推荐使用 `ConcurrentDictionary`、`SemaphoreSlim` 等原语保护共享状态。参见 [SocketClient](SocketClient/SocketClient/Plugin/SocketCommunicator.cs) 和 [CatalyticSerialPort](CatalyticSerialPort/CatalyticSerialPort/SerialPortPlugin.cs) 的实现。
