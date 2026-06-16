# Mock 运动控制上层 命令规格

## 概述

Mock 运动控制上层模拟**机械手控制程序**（运动控制上位机），监听端口 **12300**。

与 TestServer 中其他模拟设备不同，运动控制上层是**主动驱动方**——它主动通知 Catalytic 测试程序"可以开始测试了"，并等待 Catalytic 回复测试结果。

### 🧩 什么是运动控制上层？

在 OQC 测试系统中，完整的测试流程是：

```
机械手(运动控制上层) → 把 DUT 放到夹具上 → 通知测试程序"可以测试了"
                                                    ↓
                                            测试程序执行测试
                                                    ↓
测试程序通知运动控制上层"测试完成" → 机械手把 DUT 从夹具上拿走
```

**运动控制上层 = 机械手的大脑**，它负责：
1. 通知测试程序哪些 slot 的 DUT 已经放好，可以开始测试
2. 接收测试程序返回的测试结果（pass/fail）
3. 根据结果执行后续动作（把良品/不良品放到对应位置）

### 🔑 为什么需要 Mock？

因为我们要**测试的是 Catalytic 测试程序本身**，所以需要一个 Mock 来模拟机械手的行为，验证 Catalytic 是否能正确地：
- 接收 start 通知并开始测试
- 返回正确的 pass/fail 结果
- 结果的 slot 集合与 start 的 slot 集合完全对应

---

## 🛠 配置指南

### Catalytic 接入配置

| 参数项 | 配置值 |
| :--- | :--- |
| **设备类型** | TCP 文本行设备 (ASCII Encoding) |
| **连接地址** | `127.0.0.1` |
| **端口** | `12300` |
| **消息终止符** | `\n` (LF) |
| **消息格式** | CSV 格式（逗号分隔） |

### 运行模式配置

在 `Program.cs` 中通过 `batchMode` 参数切换：

```csharp
// 批量模式（同步）— 默认
var motionUpper = new MotionControlUpper(batchMode: false);

// 独立模式（异步）
var motionUpper = new MotionControlUpper(batchMode: true);
```

---

## 协议格式

使用 **CSV 文本协议**，命令和响应均为 ASCII 字符串，以 `\n` 结尾。

### 消息方向

| 方向 | 发送方 | 接收方 | 描述 |
|------|--------|--------|------|
| MCU → CAT | Mock 运动控制上层 | Catalytic 测试程序 | 通知可以测试 |
| CAT → MCU | Catalytic 测试程序 | Mock 运动控制上层 | 返回测试结果 |

### MCU → CAT：start 消息

通知 Catalytic 指定 slot 的 DUT 已放好，可以开始测试。

**格式**：
```
start,<slot0>,<slot1>,...,<slotN>\n
```

**参数说明**：
- `start`：固定前缀，表示"可以开始测试"
- `<slotX>`：Slot 编号，**0-based**（0~11）
- 多个 slot 用逗号分隔

**示例**：
```
start,0,1,5,8\n     ← Slot 0, 1, 5, 8 可以测试
start,3\n            ← 仅 Slot 3 可以测试
start,0,1,2,3,4,5,6,7,8,9,10,11\n  ← 全部 12 个 slot 都可以测试
```

> ⚠️ **注意**：Slot 编号是 **0-based**（0~11），与 MotionController 的 **1-based**（1~12）不同。这是故意设计的差异，用于测试 Catalytic 的容错能力。

### CAT → MCU：pass 消息

通知运动控制上层指定 slot 的测试通过。

**格式**：
```
pass,<slot0>,<slot1>,...,<slotN>\n
```

**示例**：
```
pass,0,5\n           ← Slot 0, 5 测试通过
pass,3\n             ← 仅 Slot 3 测试通过
```

### CAT → MCU：fail 消息

通知运动控制上层指定 slot 的测试失败。

**格式**：
```
fail,<slot0>,<slot1>,...,<slotN>\n
```

**示例**：
```
fail,1,8\n           ← Slot 1, 8 测试失败
fail,3\n             ← 仅 Slot 3 测试失败
```

---

## 两种运行模式

### 批量模式（Batch Mode, `batchMode = false`）

**默认模式**。一次性把所有料放好，发一条 `start` 消息。

**流程**：
```
MCU → CAT: start,0,1,5,8\n
（等待 Catalytic 回完 pass + fail）
CAT → MCU: pass,0,5\n
CAT → MCU: fail,1,8\n
（校验通过，进入下一轮）
...随机等待 10~60 秒...
MCU → CAT: start,2,3,7,9,10\n
...
```

**特点**：
- 一轮只发一条 start 消息
- 校验不匹配时**继续下一轮**（数据一次性给了，对不上就是 bug）

### 独立模式（Independent Mode, `batchMode = true`）

每个 slot 独立放料，分别发 `start` 消息，间隔随机 0.5~2 秒。

**流程**：
```
MCU → CAT: start,0\n
（0.5~2 秒间隔）
MCU → CAT: start,5\n
（0.5~2 秒间隔）
MCU → CAT: start,8\n
（等待 Catalytic 回完本轮所有 pass + fail）
CAT → MCU: pass,0,5\n
CAT → MCU: fail,8\n
（校验通过，进入下一轮）
...
```

**特点**：
- 一轮发多条 start 消息，每条间隔 0.5~2 秒
- 校验不匹配时**卡住等待**，直到对上或超时（120 秒）
- 超时后记录不匹配日志，强制进入下一轮

---

## 校验逻辑

### 核心规则

每轮校验：
1. **pass 集合 ∪ fail 集合** 应该等于 **start 集合**
2. **pass 集合 ∩ fail 集合** 应该为空（一个 slot 不能既 pass 又 fail）
3. **数量**：|pass| + |fail| 应该等于 |start|

### 不匹配类型

| 不匹配类型 | 描述 | 示例 |
|------------|------|------|
| 缺少 slot | Catalytic 没有返回某个 slot 的结果 | start={0,1,5,8}, pass={0,5}, fail={1} → 缺少 slot 8 |
| 多余 slot | Catalytic 返回了 start 中没有的 slot | start={0,1}, pass={0,3} → 多余 slot 3 |
| 重复判定 | 同一 slot 同时出现在 pass 和 fail 中 | start={0}, pass={0}, fail={0} → slot 0 重复判定 |

### 日志文件

**只记录校验不通过的情况**，保存到程序运行目录下的 `mismatch.log`。

**日志格式**：
```
[2026-04-28 11:30:45.123] [Round-003] MISMATCH
  Start:     [0, 1, 5, 8]
  Pass:      [0, 5]
  Fail:      [1]
  Missing:   [8]
  Extra:     []
  Duplicate: []
```

---

## 示例会话

### ✅ 正常的批量模式会话

```
MCU → CAT: start,0,3,7\n
CAT → MCU: pass,0,7\n
CAT → MCU: fail,3\n
说明：start={0,3,7}, pass={0,7}, fail={3} → 校验通过
```

### ✅ 正常的独立模式会话

```
MCU → CAT: start,2\n
（1 秒后）
MCU → CAT: start,5\n
（0.8 秒后）
MCU → CAT: start,11\n
CAT → MCU: pass,2,11\n
CAT → MCU: fail,5\n
说明：start={2,5,11}, pass={2,11}, fail={5} → 校验通过
```

### ❌ 不匹配的会话（Catalytic 漏掉了 slot 7）

```
MCU → CAT: start,0,3,7\n
CAT → MCU: pass,0\n
CAT → MCU: fail,3\n
说明：start={0,3,7}, pass={0}, fail={3} → 缺少 slot 7
      → 记录到 mismatch.log
      → 批量模式：继续下一轮
      → 独立模式：卡住等待，超时后继续
```

### ❌ 不匹配的会话（Catalytic 返回了多余的 slot）

```
MCU → CAT: start,0,3\n
CAT → MCU: pass,0,3,7\n
说明：start={0,3}, pass={0,3,7} → 多余 slot 7
      → 记录到 mismatch.log
```

---

## 随机行为说明

| 行为 | 范围 | 说明 |
|------|------|------|
| 每轮 slot 数量 | 1~12 | 随机决定本轮测试几个 slot |
| 轮次间隔 | 10~60 秒 | 模拟真实节拍不确定性 |
| 独立模式发送间隔 | 0.5~2 秒 | 模拟机械手逐个放料的间隔 |

---

## 端口分配

| 设备 | 端口 | 协议 | 角色 |
|------|------|------|------|
| **Mock 运动控制上层** | **12300** | **CSV 文本协议** | **主动驱动方** |
| MotionController | 12301 | 文本协议 | 被动响应方 |
| LegacyDevice S1~S12 | 12303~12314 | SCPI 文本协议 | 被动响应方 |

---

## 超时设置

| 场景 | 超时时间 | 说明 |
|------|----------|------|
| 轮次完成超时 | 120 秒 | 独立模式下，等待 Catalytic 回复所有 slot 结果的最大时间 |
| 客户端连接等待 | 无限 | Mock 启动后一直等待 Catalytic 连接 |

---

## 注意事项

### ⚠️ Slot 编号差异

| 设备 | Slot 编号范围 | Based |
|------|-------------|-------|
| **Mock 运动控制上层** | **0~11** | **0-based** |
| MotionController | 1~12 | 1-based |
| LegacyDevice | 1~12 | 1-based |

这是**故意设计的差异**，用于测试 Catalytic 是否能正确处理不同设备的 slot 编号体系。

### ⚠️ pass/fail 分两条发送

Catalytic 应该分两条消息分别发送 pass 和 fail 结果：
- `pass,0,5\n` — 测试通过的 slot
- `fail,1,8\n` — 测试失败的 slot

如果某轮全部 pass 或全部 fail，只发一条即可。

### ⚠️ 一轮中可能只有 pass 或只有 fail

这是合法的。例如一轮只 start 了一个 slot，且测试通过，则只发 `pass,0\n`，不需要发 fail 消息。
