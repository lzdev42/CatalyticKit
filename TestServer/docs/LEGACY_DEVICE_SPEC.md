# LegacyDevice 命令规格

## 概述

LegacyDevice 模拟一个早期的、设计不规范的工业设备，默认监听端口 **12303**。
在 **1v12 模式**下，将启动 **12 个独立实例**，分别监听端口 **12303~12314** (Slot 1~12)。

它的主要目的是通过提供各种不规范、异构的响应格式，以及模拟特定的网络异常行为，来测试客户端软件（如 Catalytic OQC）的数据解析能力和健壮性。

### 🧩 重要区别：LegacyDevice vs MotionController

**LegacyDevice = 独立设备**（12 台仪器）
- 有 **12 台独立的** LegacyDevice
- 每台监听不同端口（12303~12314）
- **命令格式**：`<COMMAND>\n`（**不需要 Slot 参数**）
- **区分方法**：通过连接不同端口来区分工位

**对比 MotionController = 共享设备**（1 个控制器）
- 只有 **1 台** MotionController
- 监听端口 **12301**
- **命令格式**：`<COMMAND> <SLOT>\n`（**必须带 Slot 参数**）
- **区分方法**：通过命令中的 Slot 参数来区分工位

**示例对比**：
```
// LegacyDevice (独立设备，12 台)
连接 127.0.0.1:12303  → 发送: *IDN?\n     ← 不需要 Slot 参数
连接 127.0.0.1:12304  → 发送: *IDN?\n     ← 同样的命令，不同端口

// MotionController (共享设备，1 台)
连接 127.0.0.1:12301  → 发送: HOME 1\n    ← 必须指定 Slot
连接 127.0.0.1:12301  → 发送: HOME 2\n    ← 同一个端口，不同 Slot
```

---

## 🔑 Slot 编码机制（核心设计）

**每个 LegacyDevice 实例的返回值都嵌入了 Slot ID 痕迹**。这是本 Mock Server 的核心验证机制——通过对照结果中的 slot 标记，可以立即发现 slot 数据是否发生了错乱。

例如：如果你连接的是 Slot 3 的仪器（端口 12305），但返回值里的 slot 痕迹显示是 `S05`，那说明数据发生了错乱。

### 各命令的 Slot 编码位置

| 命令 | 编码位置 | 示例（Slot 1） | 示例（Slot 12） |
|------|----------|---------------|-----------------|
| `*IDN?` | 序列号 `S{slot:D2}` | `SN-998877-S01` | `SN-998877-S12` |
| `SYST:ERR?` | 无编码 | `0,"No error"` | `0,"No error"` |
| `MEAS:VOLT?` | 末两位 = slot | `12.301 VDC` | `12.312 VDC` |
| `MEAS:CURR?` | 末两位 = slot | `0.401A (OK)` | `0.412A (OK)` |
| `SYST:STATUS?` | 追加 `SLOT:{slot}` | `...;SLOT:1` | `...;SLOT:12` |
| `CONF:GET?` | 追加 `[SLOT={slot}]` | `...[SLOT=1]` | `...[SLOT=12]` |
| `MEM:DUMP?` | 首行末字节 = slot 十六进制 | `0010: AA BB CC 01` | `0010: AA BB CC 0C` |
| `WAVE:RAW?` | 首点 = 100+slot | `101,105,...` | `112,105,...` |
| `TEST:SLOW` | 后缀 `_S{slot}` | `PART1_PART2_END_S1` | `PART1_PART2_END_S12` |
| `TEST:DELAY` | 后缀 `_S{slot}` | `DELAYED_RESPONSE_S1` | `DELAYED_RESPONSE_S12` |
| `TEST:GLITCH` | 后缀 `_S{slot}` | `V@L#UE: 12.34_$$_S1` | `V@L#UE: 12.34_$$_S12` |

> ⚠️ `_slotId` 在 `Program.cs` 中为 `1~12`（1-based），与端口 12303+slotId 对应。

---

## 🛠 配置指南 (Configuration Guide)

| 参数项 | 配置值 |
| :--- | :--- |
| **设备类型** | TCP 文本行设备 (ASCII Encoding) |
| **连接地址** | `127.0.0.1` |
| **端口** | `12303~12314` (Slot 1~12) |
| **命令终止符** | `\n` (LF) |
| **响应终止符** | `\n` (LF) |
| **超时建议** | 5000ms (应大于 `TEST:DELAY` 的 3000ms 延迟) |

---

## 📋 命令与测试项列表

| 命令 | 测试项 ID (Name) | 描述 | 响应格式类型 | 测试目标 |
| :--- | :--- | :--- | :--- | :--- |
| `*IDN?` | `TEST_IDN_VERIFY` | 设备识别 | SCPI标准 | 基础通讯 |
| `SYST:ERR?` | `TEST_SYS_ERR_CHECK` | 系统错误 | SCPI标准 | 错误解析 |
| `MEAS:VOLT?` | `TEST_VOLT_MEASURE` | 电压测量 | 带单位文本 | 数值提取 |
| `MEAS:CURR?` | `TEST_CURR_MEASURE` | 电流测量 | 带单位文本 | 数值提取 |
| `SYST:STATUS?` | `TEST_HEALTH_STATUS` | 系统状态 | KV混合格式 | KV解析 |
| `CONF:GET?` | `TEST_CONFIG_BACKUP` | 配置查询 | 括号KV格式 | KV解析 |
| `MEM:DUMP?` | `TEST_MEMORY_DUMP` | 内存转储 | 多行HEX | 多行解析 |
| `WAVE:RAW?` | `TEST_WAVEFORM_CSV` | 波形数据 | CSV数值列表 | 数组解析 |
| `TEST:SLOW` | `TEST_STRESS_SLOW` | 慢响应 (1s) | 简单文本 | 超时处理 |
| `TEST:DELAY` | `TEST_STRESS_DELAY` | 长延迟 (3s) | 简单文本 | 超时机制 |
| `TEST:GLITCH` | `TEST_STRESS_GLITCH` | 乱码数据 | 带乱码文本 | 容错解析 |

---

## 请求格式

所有命令均为文本格式，客户端发送：
```
<COMMAND>\n
```

⚠️ **重要**：LegacyDevice 的命令**不需要 Slot 参数**，因为每个工位有独立的 LegacyDevice 实例，通过连接不同端口来区分工位。

**正确示例**：
```
连接到 127.0.0.1:12303 (Slot 1 的仪器)
发送: *IDN?\n          ← 不需要 "1" 参数
发送: MEAS:VOLT?\n     ← 不需要 "1" 参数

连接到 127.0.0.1:12304 (Slot 2 的仪器)
发送: *IDN?\n          ← 不需要 "2" 参数
发送: MEAS:VOLT?\n     ← 不需要 "2" 参数
```

**错误示例**（与 MotionController 混淆）：
```
❌ *IDN? 1\n           ← 错误！LegacyDevice 不需要 Slot 参数
❌ MEAS:VOLT? 1\n      ← 错误！这不是 MotionController
```

---

## 响应格式

### 通用结构

```
<DATA>\n
```

服务器会自动在响应数据后添加 `\n` 终止符。

---

## 命令详细规格

### 1. 标准 SCPI 格式

#### `*IDN?` - 设备识别 ⭐ 永远 PASS

**返回格式**：标准 SCPI IDN 格式（逗号分隔），序列号含 Slot 编码

```
Acme Corp,LegacyModel-X,SN-998877-S01,v3.14
```
（上例为 Slot 1 的返回值；Slot 12 则返回 `SN-998877-S12`）

| 字段 | 含义 | 示例值 |
|------|------|--------|
| Vendor | 制造商 | Acme Corp |
| Model | 型号 | LegacyModel-X |
| Serial | 序列号（含 Slot 编码） | SN-998877-S01 |
| Version | 固件版本 | v3.14 |

**解析目标**：按逗号分隔，提取4个字段

**Pass 判定**：能成功解析出4个字段即 Pass

---

#### `SYST:ERR?` - 系统错误查询 ⭐ 永远 PASS

**返回格式**：SCPI 错误格式（无 Slot 编码）

```
0,"No error"
```

| 字段 | 含义 | 示例值 |
|------|------|--------|
| ErrorCode | 错误代码 | 0（无错误） |
| Message | 错误描述 | "No error" |

**解析目标**：提取错误代码（整数）和错误消息（字符串）

**Pass 判定**：能成功解析出错误代码和消息即 Pass

---

### 2. 带单位/前缀的数值格式（含 Slot 编码）

#### `MEAS:VOLT?` - 电压测量

**返回格式**：`KEY = VALUE UNIT` 格式，数值末两位嵌入 Slot ID

```
VOLTAGE = 12.301 VDC
```
（上例为 Slot 1；Slot 12 则返回 `12.312 VDC`）

**数值构成**：
- 整数部分：11 或 12（随机）
- 十分位：0~9（随机）
- 百分位和千分位：**固定为 slotId**（`D2` 格式）

| 参数 | 含义 | 实际范围 | 单位 |
|------|------|----------|------|
| 电压值 | 测量电压 | 11.0~12.9（受 slot 编码影响） | V |

**JSON 配置对应**：
- 解析正则：`([\d.]+)\s*VDC`
- 判定范围：`min: 11.5, max: 12.8`

**Pass 概率**：取决于 slot 编号。slot 1~9 的末两位为 `01`~`09`，大多数落在范围内；slot 10~12 的末两位为 `10`~`12`，整数部分为 11 时值约 11.010~11.012，会低于下限 11.5。

**解析目标**：忽略前缀 `VOLTAGE = ` 和后缀 ` VDC`，提取浮点数

---

#### `MEAS:CURR?` - 电流测量

**返回格式**：混合了冒号、单位和状态文本，数值末两位嵌入 Slot ID

```
Current: 0.401A (OK)
```
（上例为 Slot 1；Slot 12 则返回 `0.412A (OK)`）

**数值构成**：
- 十分位：3~6（随机）
- 百分位和千分位：**固定为 slotId**（`D2` 格式）

| 参数 | 含义 | 实际范围 | 单位 |
|------|------|----------|------|
| 电流值 | 测量电流 | 0.3~0.6（受 slot 编码影响） | A |

**JSON 配置对应**：
- 解析正则：`([\d.]+)A`
- 判定范围：`min: 0.35, max: 0.65`

**Pass 概率**：取决于 slot 编号和十分位随机值，大部分情况落在范围内。

**解析目标**：忽略前缀 `Current: ` 和后缀 `A (OK)`，提取浮点数

---

### 3. Key-Value 混合格式

#### `SYST:STATUS?` - 系统状态查询

**返回格式**：分号分隔的 KV 对，追加 `SLOT` 字段

```
TEMP:45.5;HUM:60%;ALARM:0;SLOT:1
```
（上例为 Slot 1；Slot 12 则返回 `SLOT:12`）

| 字段 | 含义 | 固定值/范围 | 单位 |
|------|------|------------|------|
| TEMP | 温度 | **45.5**（写死） | °C |
| HUM | 湿度 | **60%**（写死） | % |
| ALARM | 告警状态 | **0**（无告警） | - |
| SLOT | 工位号 | slotId | - |

**JSON 配置对应**：
- 解析正则：`ALARM:(\d+)`、`TEMP:([\d.]+)`、`HUM:([\d.]+)`
- ALARM 判定：`== 0`

**解析目标**：按分号分隔，每个 KV 对按冒号分隔，提取为 Dictionary

**Pass 判定**：ALARM == 0 即 Pass

---

#### `CONF:GET?` - 配置查询

**返回格式**：括号包围的 KV 对，追加 `SLOT` 字段

```
[MODE=AUTO][RANGE=10][FILTER=ON][SLOT=1]
```
（上例为 Slot 1；Slot 12 则返回 `[SLOT=12]`）

| 字段 | 含义 | 固定值 |
|------|------|--------|
| MODE | 工作模式 | AUTO |
| RANGE | 量程 | 10 |
| FILTER | 滤波器 | ON |
| SLOT | 工位号 | slotId |

**解析目标**：按方括号分隔，提取 KV 对

**Pass 判定**：能成功解析出4个配置项即 Pass

---

### 4. 原始数据 Dump 格式

#### `MEM:DUMP?` - 内存转储

**返回格式**：多行 HEX 数据，首行末字节嵌入 Slot ID（十六进制）

```
0010: AA BB CC 01
0020: 11 22 33 44
END
```
（上例为 Slot 1，末字节 `01`；Slot 12 则末字节为 `0C`）

**数据结构**：
- 前 N 行：`地址: HEX数据`
- 最后一行：`END` 标记

**Slot 编码**：首行的最后一个字节为 slotId 的十六进制（`{slotId:X2}`）

**解析目标**：
1. 逐行读取直到 `END`
2. 提取 HEX 字节：`AA BB CC 01 11 22 33 44`
3. 转换为字节数组：`[0xAA, 0xBB, 0xCC, 0x01, 0x11, 0x22, 0x33, 0x44]`

**Pass 判定**：能成功解析出字节数组即 Pass

---

#### `WAVE:RAW?` - 波形数据查询

**返回格式**：逗号分隔的整数序列，首点嵌入 Slot ID

```
101,105,110,120,135,140,115,108
```
（上例为 Slot 1，首点 `101`；Slot 12 则首点为 `112`）

**数据结构**：**8 个采样点**，首点 = 100 + slotId，其余固定

**Slot 编码**：第一个数据点 = 100 + slotId

**解析目标**：按逗号分隔，解析为整数数组

**Pass 判定**：能成功解析出整数数组即 Pass

**JSON 配置对应**：
- 解析正则：`((?:\d+,){7}\d+)`（匹配 8 个逗号分隔的整数）
- 判定：包含逗号即 Pass

---

### 5. 异常网络行为测试

#### `TEST:SLOW` - 慢响应测试

**行为描述**：
- 服务器延迟 **1000ms** 后返回数据
- 返回：`PART1_PART2_END_S1`（Slot 1）

**Slot 编码**：后缀 `_S{slotId}`

**测试目标**：验证客户端能容忍服务器响应延迟

**Pass 判定**：客户端能在合理超时时间内（>1s）接收到完整数据

---

#### `TEST:DELAY` - 超长延迟测试

**行为描述**：
- 服务器延迟 **3000ms** 后返回数据
- 返回：`DELAYED_RESPONSE_S1`（Slot 1）

**Slot 编码**：后缀 `_S{slotId}`

**测试目标**：验证客户端的超时机制

**Pass 判定**：
- 如果客户端超时设置 > 3s：应成功接收数据
- 如果客户端超时设置 < 3s：应触发超时错误（这是正确行为）

**JSON 配置对应**：
- 超时：3500ms

---

#### `TEST:GLITCH` - 乱码数据测试

**返回格式**：包含非法字符的数据，数值后跟 `$$` 乱码

```
V@L#UE: 12.34_$$_S1
```
（上例为 Slot 1；Slot 12 则返回 `V@L#UE: 12.34_$$_S12`）

**Slot 编码**：后缀 `_S{slotId}`

**乱码字符说明**：
- `V@L#UE:` — 前缀含 `@` 和 `#`
- `12.34` — 有效数值
- `_$$_` — 数字后紧跟 `$$` 乱码（不干扰正则提取）
- `_S1` — Slot 标识

**预期数值**：`12.34`

**测试目标**：验证客户端的容错能力

**JSON 配置对应**：
- 解析正则：`([\d.]+)` → 提取 `12.34`

**Pass 判定**：能从乱码中提取出正确数值 `12.34`

---

## Pass/Fail 判定总结

| 命令 | Pass 策略 | Slot 编码位置 | 主要测试点 |
|------|-----------|--------------|-----------|
| `*IDN?` | ⭐ 永远 PASS | 序列号 S01~S12 | SCPI标准格式解析 |
| `SYST:ERR?` | ⭐ 永远 PASS | 无 | 错误码解析 |
| `MEAS:VOLT?` | 数值范围 | 末两位 = slot | 11.5~12.8V 范围判定 |
| `MEAS:CURR?` | 数值范围 | 末两位 = slot | 0.35~0.65A 范围判定 |
| `SYST:STATUS?` | ALARM==0 | SLOT 字段 | KV解析 + 告警判定 |
| `CONF:GET?` | ⭐ 永远 PASS | SLOT 字段 | 括号KV解析 |
| `MEM:DUMP?` | ⭐ 永远 PASS | 首行末字节 | 多行HEX转换 |
| `WAVE:RAW?` | ⭐ 永远 PASS | 首点 = 100+slot | CSV数组解析 |
| `TEST:SLOW` | 超时容忍 | 后缀 _S{slot} | 1秒延迟容忍度 |
| `TEST:DELAY` | 超时机制 | 后缀 _S{slot} | 3秒超时处理 |
| `TEST:GLITCH` | 容错解析 | 后缀 _S{slot} | 乱码过滤能力 |

---

## 计算测试项（Catalytic Engine 侧执行）

LegacyDevice 返回的原始测量值可供 Catalytic Engine 进行数学运算。以下是 JSON 配置中定义的计算步骤：

| 计算步骤 | 表达式 | 消费的变量 | 来源命令 |
|---------|--------|-----------|---------|
| 计算功率 | `voltage * current` | voltage, current | MEAS:VOLT? + MEAS:CURR? |
| 计算电阻 | `voltage / current` | voltage, current | MEAS:VOLT? + MEAS:CURR? |
| 温度转换 | `temp * 1.8 + 32` | temp | SYST:STATUS? |
| 计算压降 | `12.0 - voltage` | voltage | MEAS:VOLT? |

> 这些计算由 Catalytic Engine 在软件侧执行，mock server 不参与计算。由于 voltage/current 带有 slot 编码痕迹，计算结果也会因 slot 不同而不同，可以继续验证 slot 数据没有错乱。

---

## 模拟延迟

- **基础延迟**：所有命令 10~50 ms（随机，模拟设备处理时间）
- **TEST:SLOW**：额外延迟 1000 ms
- **TEST:DELAY**：额外延迟 3000 ms

---

## 客户端实现建议

### A. 数值提取正则

```csharp
// 适用于 MEAS:VOLT?, MEAS:CURR?, TEST:GLITCH
Regex.Match(response, @"[-+]?\d+\.?\d*").Value
```

### B. KV 解析

```csharp
// 适用于 SYST:STATUS?
var dict = response.Split(';')
    .Select(kv => kv.Split(':'))
    .ToDictionary(parts => parts[0], parts => parts[1].TrimEnd('%'));
```

### C. 多行读取

```csharp
// 适用于 MEM:DUMP?
var lines = new List<string>();
string line;
while ((line = reader.ReadLine()) != "END")
{
    lines.Add(line);
}
```

### D. 超时设置

```csharp
// 适用于 TEST:SLOW, TEST:DELAY
client.ReceiveTimeout = 5000; // 5秒超时
```
