# MotionController 命令规格

## 概述

MotionController 模拟一个运动控制器（控制卡），监听端口 **12301**。

它控制一个有 **12 个工位（Slot）** 的夹具，每个工位可独立执行机械动作。

### 🧩 什么是 MotionController？

MotionController 控制的是一个**共享设备**——一个有 12 个工位的夹具。与 LegacyDevice（12 个独立仪器，每台一个端口）不同，这里只有 **1 个控制器、1 个端口**，通过命令中的 Slot 参数来区分操作哪个工位。

**所以规则是**：**所有命令都必须带 Slot 参数**（包括 `VERSION`）。

---

## 🛠 配置指南 (Configuration Guide)

| 参数项 | 配置值 |
| :--- | :--- |
| **设备类型** | TCP 文本行设备 (ASCII Encoding) |
| **连接地址** | `127.0.0.1` |
| **端口** | `12301` |
| **命令终止符** | `\n` (LF) |
| **响应终止符** | `\n` (LF) |
| **命令格式** | `<COMMAND> <SLOT>` (例如 `HOME 1`) |

---

## 协议格式

使用**文本协议**，命令和响应均为 ASCII 字符串，以 `\n` 结尾。

### 请求格式

```
<COMMAND> <SLOT>\n
```

**参数说明**：
- `<COMMAND>`: 指令名（不区分大小写）
- `<SLOT>`: **必填**。Slot 编号（整数），指定操作哪个工位
  - ⚠️ **注意**：所有命令（包括 `VERSION`）都必须带 Slot 参数
  - 服务器不校验 slot 范围，接受任意整数并回显

**正确示例**：
```
HOME 1\n          ← 让 Slot 1 回原点
CLAMP 3\n         ← 夹紧 Slot 3
UNCLAMP 5\n       ← 松开 Slot 5
VERSION 1\n        ← 查询版本（也需要 Slot）
```

**错误示例**：
```
HOME\n            ← ❌ 错误！缺少 Slot 参数，返回 ERR:MISSING_SLOT
VERSION\n         ← ❌ 错误！VERSION 也需要 Slot 参数
```

### 响应格式

由于 MotionController 是共享设备（控制 12 个 slot），响应必须包含 slot 信息，否则客户端无法区分是哪个 slot 完成了操作。

**成功响应**：
```
<CMD>_OK <SLOT>\n
```

**错误响应**：
```
ERR:<ERROR_CODE> <SLOT>\n
```

**示例**：
- 发送：`HOME 1\n` → 响应：`HOME_OK 1\n`
- 发送：`CLAMP 5\n` → 响应：`CLAMP_OK 5\n`
- 发送：`UNCLAMP 3\n` → 响应：`UNCLAMP_OK 3\n`
- 发送：`VERSION 1\n` → 响应：`v1.0.0 1\n`

| 响应格式 | 示例 | 含义 |
|---------|------|------|
| `<CMD>_OK <SLOT>` | `HOME_OK 1` | 命令执行成功 |
| `ERR:MISSING_SLOT` | `ERR:MISSING_SLOT` | 缺少 Slot 参数 |
| `ERR:UNKNOWN_CMD <SLOT>` | `ERR:UNKNOWN_CMD 5` | 未知命令 |
| `v1.0.0 <SLOT>` | `v1.0.0 1` | VERSION 命令的响应 |

**为什么响应要包含 SLOT？**

因为 MotionController 是共享设备，可能同时收到多个 slot 的命令请求（服务器是并发处理的）：
- 如果只返回 `OK`，客户端不知道是哪个 slot 完成了
- 返回 `HOME_OK 1` 就很清楚：Slot 1 的 HOME 命令完成了

---

## 命令列表

| 命令 | 描述 | 成功响应 | 模拟延迟 |
|------|------|----------|----------|
| `HOME <slot>` | 让指定 Slot 回原点 | `HOME_OK <slot>` | 1000 ms |
| `CLAMP <slot>` | 夹紧指定 Slot 的治具 | `CLAMP_OK <slot>` | 500 ms |
| `UNCLAMP <slot>` | 松开指定 Slot 的治具 | `UNCLAMP_OK <slot>` | 500 ms |
| `VERSION <slot>` | 获取版本号 | `v1.0.0 <slot>` | ~20 ms |

> ⚠️ 所有命令都必须带 `<slot>` 参数。缺少时返回 `ERR:MISSING_SLOT`。

### 📝 命令详解

#### `HOME <slot>` - 回原点
- **作用**：让指定工位的机械臂回到零点位置
- **示例**：
  - `HOME 1` → `HOME_OK 1` ✅
  - `HOME 5` → `HOME_OK 5` ✅
  - ~~`HOME`~~ → `ERR:MISSING_SLOT` ❌

#### `CLAMP <slot>` - 夹紧治具
- **作用**：夹紧指定工位的治具（夹爪）
- **示例**：
  - `CLAMP 2` → `CLAMP_OK 2` ✅
  - `CLAMP 8` → `CLAMP_OK 8` ✅

#### `UNCLAMP <slot>` - 松开治具
- **作用**：松开指定工位的治具（夹爪）
- **示例**：
  - `UNCLAMP 2` → `UNCLAMP_OK 2` ✅
  - `UNCLAMP 8` → `UNCLAMP_OK 8` ✅

#### `VERSION <slot>` - 查询版本
- **作用**：查询控制器固件版本号
- **注意**：VERSION 命令也需要 Slot 参数
- **示例**：`VERSION 1` → `v1.0.0 1`

---

## 并发处理

MotionController 服务器采用**并发处理**模式：
- 每个请求独立处理，互不阻塞
- 不会返回 BUSY 状态
- 多个 slot 的命令可以同时执行

---

## 示例会话

### ✅ 正确的会话流程
```
Client: HOME 1\n
Server: HOME_OK 1\n
说明：Slot 1 回原点成功（注意响应格式：命令_OK + Slot）

Client: CLAMP 2\n
Server: CLAMP_OK 2\n
说明：Slot 2 夹紧成功

Client: UNCLAMP 2\n
Server: UNCLAMP_OK 2\n
说明：Slot 2 松开成功

Client: VERSION 1\n
Server: v1.0.0 1\n
说明：查询版本号（注意也需要 Slot 参数）
```

### ❌ 错误的会话示例
```
Client: HOME\n
Server: ERR:MISSING_SLOT\n
说明：❌ 缺少 Slot 参数

Client: CLAMP\n
Server: ERR:MISSING_SLOT\n
说明：❌ 缺少 Slot 参数

Client: VERSION\n
Server: ERR:MISSING_SLOT\n
说明：❌ VERSION 也需要 Slot 参数

Client: INVALID 1\n
Server: ERR:UNKNOWN_CMD 1\n
说明：❌ 未知命令（注意响应也包含 Slot）
```

### 🔀 并发场景
```
场景：同时操作多个 Slot（服务器并发处理）

Client: HOME 1\n
Client: HOME 2\n     ← 不需要等待 Slot 1 完成
Server: HOME_OK 1\n
Server: HOME_OK 2\n
说明：两个命令并发执行，独立响应
```

---

## 注意事项

### ⚠️ 常见错误和解决方法

#### 1. **忘记带 Slot 参数**
**错误现象**：发送 `HOME\n`，收到 `ERR:MISSING_SLOT\n`

**原因**：所有命令都必须指定 Slot

**解决方法**：改为 `HOME 1\n`

#### 2. **Slot 编号范围**
代码不校验 slot 范围，接受任意整数并原样回显。但实际工位为 1~12。

#### 3. **Slot 编号体系差异**
| 设备 | Slot 编号范围 | Based |
|------|-------------|-------|
| **Mock 运动控制上层** | **0~11** | **0-based** |
| **MotionController** | **1~12** | **1-based** |
| **LegacyDevice** | **1~12** | **1-based** |

这是**故意设计的差异**，用于测试 Catalytic 是否能正确处理不同设备的 slot 编号体系。
