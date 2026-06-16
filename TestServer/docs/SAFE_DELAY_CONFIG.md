# 安全延迟配置表

## 📌 说明

本文档列出每个命令/动作的**安全延迟时间**（Safe Delay），即客户端发送命令后**必须等待的最小时间**，以确保设备完成动作并返回响应。

**重要**: 按照这些延迟设定，**保证不会出错**。

---

## 🎯 MotionController 命令安全延迟

| 命令 | 描述 | 服务器执行时间 | 安全延迟（建议） | 说明 |
|------|------|---------------|----------------|------|
| `HOME <slot>` | 回原点 | 1000 ms | **1100 ms** | 需等待轴回零 |
| `CLAMP <slot>` | 夹紧治具 | 500 ms | **600 ms** | 气缸动作时间 |
| `UNCLAMP <slot>` | 松开治具 | 500 ms | **600 ms** | 气缸动作时间 |
| `VERSION <slot>` | 获取版本号 | ~20 ms | **100 ms** | 立即响应 |

> ⚠️ 所有命令都必须带 `<slot>` 参数（包括 `VERSION`）。

**总计（单次完整流程 — 机械动作）**:
- HOME: 1100 ms
- CLAMP: 600 ms
- 测试...（见下表）
- UNCLAMP: 600 ms
- **机械动作总时间**: ~2300 ms

---

## 🧪 LegacyDevice 测试命令安全延迟

| 命令 | 测试项 ID | 服务器延迟范围 | 安全延迟（建议） | 说明 |
|------|----------|---------------|----------------|------|
| `*IDN?` | TEST_IDN_VERIFY | 10~50 ms | **100 ms** | SCPI 设备识别 |
| `SYST:ERR?` | TEST_SYS_ERR_CHECK | 10~50 ms | **100 ms** | 系统错误查询 |
| `MEAS:VOLT?` | TEST_VOLT_MEASURE | 10~50 ms | **100 ms** | 电压测量（末两位=slot） |
| `MEAS:CURR?` | TEST_CURR_MEASURE | 10~50 ms | **100 ms** | 电流测量（末两位=slot） |
| `SYST:STATUS?` | TEST_HEALTH_STATUS | 10~50 ms | **100 ms** | 系统状态查询（含 SLOT 字段） |
| `CONF:GET?` | TEST_CONFIG_BACKUP | 10~50 ms | **100 ms** | 配置查询（含 SLOT 字段） |
| `MEM:DUMP?` | TEST_MEMORY_DUMP | 10~50 ms | **150 ms** | 内存转储（多行数据，首行末字节=slot） |
| `WAVE:RAW?` | TEST_WAVEFORM_CSV | 10~50 ms | **150 ms** | 波形数据（8个点，首点=100+slot） |
| `TEST:GLITCH` | TEST_STRESS_GLITCH | 10~50 ms | **100 ms** | 乱码数据测试（后缀 _S{slot}） |
| `TEST:SLOW` | TEST_STRESS_SLOW | 1000 ms | **1200 ms** | 慢响应测试（后缀 _S{slot}） |
| `TEST:DELAY` | TEST_STRESS_DELAY | 3000 ms | **3200 ms** | 超长延迟测试（后缀 _S{slot}） |

**测试项总时间**（不含压力测试）:
- 8 个基础测试项: 8 × 100 ms = 800 ms
- 2 个多行数据测试: 2 × 150 ms = 300 ms
- **基础测试总时间**: ~1100 ms

**含压力测试总时间**:
- 基础测试: 1100 ms
- TEST:GLITCH: 100 ms
- TEST:DELAY: 3200 ms
- **完整测试总时间**: ~4400 ms

---

## ⏱️ 单个产品完整测试流程时间预算

### 标准流程（不含压力测试）

| 步骤 | 操作 | 设备 | 安全延迟 |
|-----|------|------|---------|
| 0 | HOME | MotionController | 1100 ms |
| 1 | CLAMP | MotionController | 600 ms |
| 2.1 | *IDN? | LegacyDevice Slot 1 | 100 ms |
| 2.2 | SYST:ERR? | LegacyDevice Slot 1 | 100 ms |
| 3.1 | SYST:ERR? | LegacyDevice Slot 1 | 100 ms |
| 3.2 | SYST:STATUS? | LegacyDevice Slot 1 | 100 ms |
| 4.1 | MEAS:VOLT? | LegacyDevice Slot 1 | 100 ms |
| 4.2 | MEAS:CURR? | LegacyDevice Slot 1 | 100 ms |
| 5 | 计算步骤 | 软件侧 | 0 ms |
| 6.1 | WAVE:RAW? | LegacyDevice Slot 1 | 150 ms |
| 6.2 | MEM:DUMP? | LegacyDevice Slot 1 | 150 ms |
| 7.1 | TEST:GLITCH | LegacyDevice Slot 1 | 100 ms |
| 8 | UNCLAMP | MotionController | 600 ms |
| **总计** | | | **3200 ms** |

**建议循环时间**: **4 秒/产品**（含网络延迟和软件处理时间余量）

---

### 完整流程（含压力测试）

在标准流程基础上增加：
- TEST:DELAY: 3200 ms

**总计**: 3200 + 3200 = **6400 ms**

**建议循环时间**: **7 秒/产品**

---

## 🔄 12 工位并行测试策略

### 方案 A：流水线模式（推荐）

每个工位的测试可以并行进行，机械动作也可并发执行（服务器支持并发处理）。

**时间轴示例**（测试并行，机械动作也可并发）:

```
时间轴:
0ms      ━━ Slot 1: CLAMP (600ms)
600ms    ━━ Slot 1: 开始测试 (1100ms)
         ━━ Slot 2: CLAMP (600ms)
1200ms   ━━ Slot 2: 开始测试 (1100ms)
1700ms   ━━ Slot 1: 测试完成
         ━━ Slot 3: CLAMP (600ms)
2300ms   ━━ Slot 1: UNCLAMP (600ms)
...
```

**理论吞吐量**:
- 每个 Slot 从上料到下料完成: ~3200 ms
- 12 个 Slot 流水线稳态: 每 **600 ms** 完成 1 个产品
- **每小时产能**: 6000 产品

**实际建议吞吐量**（考虑 50% 余量）:
- **每 1.2 秒** 完成 1 个产品
- **每小时产能**: 3000 产品

---

### 方案 B：批量模式

一次性上料 12 个产品，并行测试，再批量下料。

**时间预算**:
- 批量上料 (CLAMP): 12 × 600 = 7200 ms
- 并行测试: 1100 ms（所有工位同时测试）
- 批量下料 (UNCLAMP): 12 × 600 = 7200 ms
- **批次总时间**: 15500 ms

**每小时产能**: 12 × (3600 / 15.5) ≈ **2787 产品**

---

## 📊 客户端超时设置建议

### TCP 连接超时
```
连接超时: 5000 ms
```

### 命令响应超时（按命令分类）

| 命令类型 | 推荐超时 | 说明 |
|---------|---------|------|
| MotionController: HOME | 2000 ms | 最长机械动作（1000ms） |
| MotionController: CLAMP/UNCLAMP | 1500 ms | 气缸动作（500ms） |
| MotionController: VERSION | 500 ms | 立即响应 |
| LegacyDevice: 基础测试 | 1000 ms | 快速响应命令 |
| LegacyDevice: MEM/WAVE | 1000 ms | 多行数据传输 |
| LegacyDevice: TEST:GLITCH | 1000 ms | 快速响应 |
| LegacyDevice: TEST:DELAY | 3500 ms | 超长延迟（3000ms） |

**通用超时设置**:
```csharp
// 保守策略（推荐，统一超时）
client.ReceiveTimeout = 3500; // 3.5秒，可应对所有命令

// 激进策略（需分命令设置）
switch (command)
{
    case "HOME":
        client.ReceiveTimeout = 2000;
        break;
    case "TEST:DELAY":
        client.ReceiveTimeout = 3500;
        break;
    default:
        client.ReceiveTimeout = 1000;
        break;
}
```

---

## ⚠️ 注意事项

1. **安全延迟 = 服务器执行时间 + 网络延迟 + 安全余量**
   - 网络延迟: 假设 LAN 内 < 10 ms
   - 安全余量: 通常设为执行时间的 10~20%

2. **MotionController 并发处理**
   - 服务器支持并发处理，多个命令可同时执行
   - 不会返回 BUSY 状态
   - 建议仍使用命令队列控制节拍

3. **LegacyDevice 并行能力**
   - 12 个实例相互独立
   - 可同时向 12 个端口发送测试命令
   - 每个端口的响应都带有 Slot 编码痕迹，可用于校验数据是否错乱

4. **Slot 编码与数据校验**
   - LegacyDevice 每个返回值都嵌入了 slotId
   - 如果连接的是 Slot 3（端口 12305），但返回值里的 slot 痕迹不是 3，说明数据错乱
   - 详见 [LEGACY_DEVICE_SPEC](LEGACY_DEVICE_SPEC.md) 的"Slot 编码机制"章节

5. **压力测试项是否必需**
   - `TEST:GLITCH` / `TEST:DELAY` 主要用于验证客户端健壮性
   - 正常生产测试可跳过，节省 ~3.3 秒/产品

---

## 🛠️ 配置示例（JSON）

```json
{
  "MotionController": {
    "host": "127.0.0.1",
    "port": 12301,
    "commands": {
      "HOME": { "delay_ms": 1100, "timeout_ms": 2000 },
      "CLAMP": { "delay_ms": 600, "timeout_ms": 1500 },
      "UNCLAMP": { "delay_ms": 600, "timeout_ms": 1500 },
      "VERSION": { "delay_ms": 100, "timeout_ms": 500 }
    }
  },
  "LegacyDevices": {
    "host": "127.0.0.1",
    "ports": [12303, 12304, 12305, 12306, 12307, 12308, 12309, 12310, 12311, 12312, 12313, 12314],
    "commands": {
      "*IDN?": { "delay_ms": 100, "timeout_ms": 1000 },
      "SYST:ERR?": { "delay_ms": 100, "timeout_ms": 1000 },
      "MEAS:VOLT?": { "delay_ms": 100, "timeout_ms": 1000 },
      "MEAS:CURR?": { "delay_ms": 100, "timeout_ms": 1000 },
      "SYST:STATUS?": { "delay_ms": 100, "timeout_ms": 1000 },
      "CONF:GET?": { "delay_ms": 100, "timeout_ms": 1000 },
      "MEM:DUMP?": { "delay_ms": 150, "timeout_ms": 1000 },
      "WAVE:RAW?": { "delay_ms": 150, "timeout_ms": 1000 },
      "TEST:GLITCH": { "delay_ms": 100, "timeout_ms": 1000 },
      "TEST:SLOW": { "delay_ms": 1200, "timeout_ms": 2000 },
      "TEST:DELAY": { "delay_ms": 3200, "timeout_ms": 3500 }
    }
  },
  "TestSequence": {
    "standard_cycle_time_ms": 4000,
    "full_cycle_time_ms": 7000,
    "skip_stress_tests": true
  }
}
```

---

**最后更新**: 2026-06-16
