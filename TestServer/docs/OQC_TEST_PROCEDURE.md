# OQC 测试流程说明书

## 📋 概述

本文档描述了使用 **MotionController** 和 **LegacyDevice** 进行完整产品测试的标准流程。

### 🧩 新手必读：理解测试系统架构

在开始之前，你需要理解两种不同类型的设备：

#### 1️⃣ MotionController（运动控制器）= 共享设备
- **是什么**：一个控制卡，控制一个有 **12 个工位（Slot）** 的大夹具
- **重要特点**：
  - 只有 **1 台** MotionController
  - 它控制 **12 个工位** 的机械动作
  - **所有命令都必须指定操作哪个工位**（包括 `VERSION`）
  - 例如：`HOME 1` 表示"让 Slot 1 回原点"

#### 2️⃣ LegacyDevice（测试仪器）= 独立设备
- **是什么**：测试仪器，每个工位有一台独立的仪器
- **重要特点**：
  - 有 **12 台** LegacyDevice（Slot 1~12）
  - 每台监听不同端口（12303~12314）
  - **命令不需要指定 Slot**，因为你连接哪个端口就是操作哪个工位
  - **每个返回值都带 Slot 编码痕迹**，可对照发现数据错乱

**简单记忆**：
- MotionController：1 个控制器管 12 个工位 → **命令要带 Slot 号**
- LegacyDevice：12 个独立仪器 → **从端口区分，命令不带 Slot 号**

---

## 🔧 设备配置

### MotionController（运动控制器）
- **端口**: `127.0.0.1:12301`
- **协议**: 文本协议（命令以 `\n` 结尾）
- **数量**: **1 台**（控制 12 个工位的共享设备）
- **作用**: 控制 12 个工位的机械动作（复位、夹紧、松开等）
- ⚠️ **重要**：所有命令必须指定工位号（Slot）

### LegacyDevice（测试仪器）
**数量**: **12 台**（每个工位一台独立的仪器）

| 工位 (Slot) | 端口 | 用途 | 命令是否需要 Slot？ |
|------------|------|------|------------------|
| Slot 1 | 127.0.0.1:12303 | 测试仪器 #1 | ❌ 不需要（端口已区分） |
| Slot 2 | 127.0.0.1:12304 | 测试仪器 #2 | ❌ 不需要 |
| Slot 3 | 127.0.0.1:12305 | 测试仪器 #3 | ❌ 不需要 |
| Slot 4 | 127.0.0.1:12306 | 测试仪器 #4 | ❌ 不需要 |
| Slot 5 | 127.0.0.1:12307 | 测试仪器 #5 | ❌ 不需要 |
| Slot 6 | 127.0.0.1:12308 | 测试仪器 #6 | ❌ 不需要 |
| Slot 7 | 127.0.0.1:12309 | 测试仪器 #7 | ❌ 不需要 |
| Slot 8 | 127.0.0.1:12310 | 测试仪器 #8 | ❌ 不需要 |
| Slot 9 | 127.0.0.1:12311 | 测试仪器 #9 | ❌ 不需要 |
| Slot 10 | 127.0.0.1:12312 | 测试仪器 #10 | ❌ 不需要 |
| Slot 11 | 127.0.0.1:12313 | 测试仪器 #11 | ❌ 不需要 |
| Slot 12 | 127.0.0.1:12314 | 测试仪器 #12 | ❌ 不需要 |

---

## 🔄 完整测试流程

### 第 0 步：系统初始化

#### 0.1 运动控制器回原点

HOME 命令必须指定 Slot：

```
发送命令: HOME 1\n
目标设备: MotionController (127.0.0.1:12301)
预期响应: HOME_OK 1\n      ← 注意：响应格式为 命令_OK + Slot
服务器执行时间: 1000 ms
安全延迟（客户端等待）: 1100 ms
超时设置: 2000 ms
说明: Slot 1 回原点
```

如果要初始化所有 12 个工位，需要依次发送 12 次命令（服务器并发处理，可不等待上一个完成）：
```
HOME 1\n → HOME_OK 1
HOME 2\n → HOME_OK 2
...
HOME 12\n → HOME_OK 12
```

**简化方案**（推荐）：如果只测试某个工位（例如 Slot 1），只需要 `HOME 1`。

---

### 第 1 步：产品上料（以 Slot 1 为例）

#### 1.1 夹紧产品
```
发送命令: CLAMP 1\n
目标设备: MotionController (127.0.0.1:12301)
预期响应: CLAMP_OK 1\n     ← 响应格式：命令_OK + Slot
服务器执行时间: 500 ms
安全延迟（客户端等待）: 600 ms
超时设置: 1500 ms
说明: 夹紧 Slot 1 的夹爪，固定产品
```

---

### 第 2 步：设备识别测试

#### 2.1 测试项：`TEST_IDN_VERIFY` - 设备识别
```
发送命令: *IDN?\n
目标设备: LegacyDevice (127.0.0.1:12303) [Slot 1]
预期响应: Acme Corp,LegacyModel-X,SN-998877-S01,v3.14\n
                                          ↑ Slot 编码痕迹
服务器响应时间: 10~50 ms（随机）
安全延迟（客户端等待）: 100 ms
超时设置: 1000 ms
判定标准: ⭐ 永远 PASS（解析成功即通过）
```

**解析目标**: 提取 4 个字段（Vendor, Model, Serial, Version）

#### 2.2 测试项：`TEST_SYS_ERR_CHECK` - 系统错误检查
```
发送命令: SYST:ERR?\n
目标设备: LegacyDevice (127.0.0.1:12303) [Slot 1]
预期响应: 0,"No error"\n
服务器响应时间: 10~50 ms（随机）
安全延迟（客户端等待）: 100 ms
超时设置: 1000 ms
判定标准: ⭐ 永远 PASS（解析成功即通过）
```

---

### 第 3 步：防呆自检

#### 3.1 测试项：`TEST_SYS_ERR_CHECK` - 查错误
```
发送命令: SYST:ERR?\n
目标设备: LegacyDevice (127.0.0.1:12303) [Slot 1]
预期响应: 0,"No error"\n
解析正则: ^(\d+)
判定标准: err_code == 0
```

#### 3.2 测试项：`TEST_HEALTH_STATUS` - 查状态
```
发送命令: SYST:STATUS?\n
目标设备: LegacyDevice (127.0.0.1:12303) [Slot 1]
预期响应: TEMP:45.5;HUM:60%;ALARM:0;SLOT:1\n
                                       ↑ Slot 编码痕迹
解析变量: alarm=ALARM:(\d+), temp=TEMP:([\d.]+), humidity=HUM:([\d.]+)
判定标准: alarm == 0
```

---

### 第 4 步：核心电气参数测试

#### 4.1 测试项：`TEST_VOLT_MEASURE` - 电压测量
```
发送命令: MEAS:VOLT?\n
目标设备: LegacyDevice (127.0.0.1:12303) [Slot 1]
预期响应: VOLTAGE = 12.301 VDC\n
                         ↑↑ 末两位=Slot 编码 (01=Slot1)
解析正则: ([\d.]+)\s*VDC
判定标准: 范围 11.5 ~ 12.8 V
安全延迟（客户端等待）: 100 ms
超时设置: 1000 ms
```

#### 4.2 测试项：`TEST_CURR_MEASURE` - 电流测量
```
发送命令: MEAS:CURR?\n
目标设备: LegacyDevice (127.0.0.1:12303) [Slot 1]
预期响应: Current: 0.401A (OK)\n
                        ↑↑ 末两位=Slot 编码 (01=Slot1)
解析正则: ([\d.]+)A
判定标准: 范围 0.35 ~ 0.65 A
安全延迟（客户端等待）: 100 ms
超时设置: 1000 ms
```

---

### 第 5 步：计算测试项（Catalytic Engine 侧执行）

以下计算由 Catalytic Engine 在软件侧执行，mock server 不参与计算。

#### Calc 1 - 计算功率（乘法）
```
表达式: power = voltage * current
判定标准: 范围 4.0 ~ 8.5
说明: voltage 和 current 来自第 4 步的测量值
```

#### Calc 2 - 计算电阻（除法）
```
表达式: resistance = voltage / current
判定标准: resistance >= 15.0
说明: 用 voltage 和 current 计算
```

#### Calc 3 - 温度转换（乘法+加法）
```
表达式: temp_f = temp * 1.8 + 32
判定标准: 无（仅计算，不判定）
说明: temp 来自第 3.2 步 SYST:STATUS? 的解析
```

#### Calc 4 - 计算压降（减法）
```
表达式: voltage_drop = 12.0 - voltage
判定标准: 表达式 voltage_drop < 1.0
说明: voltage 来自第 4.1 步
```

> 由于 voltage/current 带有 slot 编码痕迹，计算结果也会因 slot 不同而不同，可以继续验证 slot 数据没有错乱。

---

### 第 6 步：数据采集测试

#### 6.1 测试项：`TEST_WAVEFORM_CSV` - 波形数据采集
```
发送命令: WAVE:RAW?\n
目标设备: LegacyDevice (127.0.0.1:12303) [Slot 1]
预期响应: 101,105,110,120,135,140,115,108\n
              ↑ 首点=100+Slot (101=Slot1)
解析正则: ((?:\d+,){7}\d+)
判定标准: 包含逗号即 Pass（验证 CSV 格式正确）
安全延迟（客户端等待）: 150 ms
超时设置: 1000 ms
```

**响应格式**: 8 个逗号分隔的整数，首点 = 100 + slotId

#### 6.2 测试项：`TEST_MEMORY_DUMP` - 内存转储
```
发送命令: MEM:DUMP?\n
目标设备: LegacyDevice (127.0.0.1:12303) [Slot 1]
预期响应:
  0010: AA BB CC 01\n
  0020: 11 22 33 44\n
  END\n
                  ↑ 首行末字节=Slot 十六进制 (01=Slot1)
响应终止符: END
判定标准: ⭐ 永远 PASS（解析成功即通过）
安全延迟（客户端等待）: 150 ms
超时设置: 1000 ms
```

---

### 第 7 步：健壮性考核

#### 7.1 测试项：`TEST_STRESS_GLITCH` - 乱码数据容错测试
```
发送命令: TEST:GLITCH\n
目标设备: LegacyDevice (127.0.0.1:12303) [Slot 1]
预期响应: V@L#UE: 12.34_$$_S1\n
                              ↑ Slot 编码痕迹
解析正则: ([\d.]+) → 提取出 12.34
安全延迟（客户端等待）: 100 ms
超时设置: 1000 ms
判定标准: 无（仅验证能从乱码中提取数值）
```

#### 7.2 测试项：`TEST_STRESS_DELAY` - 超长延迟测试
```
发送命令: TEST:DELAY\n
目标设备: LegacyDevice (127.0.0.1:12303) [Slot 1]
预期响应: DELAYED_RESPONSE_S1\n
                           ↑ Slot 编码痕迹
服务器执行时间: 3000 ms（固定延迟）
安全延迟（客户端等待）: 3200 ms
超时设置: 3500 ms
判定标准: 能收到响应即 Pass
```

---

### 第 8 步：产品下料

#### 8.1 松开治具
```
发送命令: UNCLAMP 1\n
目标设备: MotionController (127.0.0.1:12301)
预期响应: UNCLAMP_OK 1\n    ← 响应格式：命令_OK + Slot
服务器执行时间: 500 ms
安全延迟（客户端等待）: 600 ms
超时设置: 1500 ms
说明: 松开 Slot 1 的夹爪，释放产品
```

---

## 📊 完整流程时间估算（单个 Slot）

### 标准流程（不含压力测试和计算）

| 步骤 | 命令 | 安全延迟（ms） |
|-----|------|---------------|
| 0.1 | HOME | 1100 |
| 1.1 | CLAMP | 600 |
| 2.1 | *IDN? | 100 |
| 2.2 | SYST:ERR? | 100 |
| 3.1 | SYST:ERR? | 100 |
| 3.2 | SYST:STATUS? | 100 |
| 4.1 | MEAS:VOLT? | 100 |
| 4.2 | MEAS:CURR? | 100 |
| 5.1~5.4 | 计算步骤 | 0（软件侧） |
| 6.1 | WAVE:RAW? | 150 |
| 6.2 | MEM:DUMP? | 150 |
| 7.1 | TEST:GLITCH | 100 |
| 8.1 | UNCLAMP | 600 |
| **总计** | | **3200 ms** |

**建议循环时间**: **4 秒/产品**（含网络延迟和软件处理余量）

### 完整流程（含压力测试）

在标准流程基础上增加：
- TEST:DELAY: 3200 ms

**总计**: 3200 + 3200 = **6400 ms**

**建议循环时间**: **7 秒/产品**

**说明**:
- **安全延迟** = 服务器执行时间 + 网络延迟 + 安全余量
- 详细配置请参考：[安全延迟配置表](SAFE_DELAY_CONFIG.md)

---

## 📝 测试项汇总表

| 测试项 ID | 测试名称 | 命令 | Slot 编码位置 | Pass 概率 | 是否关键 |
|----------|---------|------|-------------|----------|---------|
| `TEST_IDN_VERIFY` | 设备识别 | `*IDN?` | 序列号 S01~S12 | 100% | 否 |
| `TEST_SYS_ERR_CHECK` | 系统错误检查 | `SYST:ERR?` | 无 | 100% | 否 |
| `TEST_VOLT_MEASURE` | 电压测量 | `MEAS:VOLT?` | 末两位 = slot | 随机 | ✅ 是 |
| `TEST_CURR_MEASURE` | 电流测量 | `MEAS:CURR?` | 末两位 = slot | 随机 | ✅ 是 |
| `TEST_HEALTH_STATUS` | 健康状态 | `SYST:STATUS?` | SLOT 字段 | 100% | 否 |
| `TEST_CONFIG_BACKUP` | 配置备份 | `CONF:GET?` | SLOT 字段 | 100% | 否 |
| `TEST_MEMORY_DUMP` | 内存转储 | `MEM:DUMP?` | 首行末字节 | 100% | 否 |
| `TEST_WAVEFORM_CSV` | 波形采集 | `WAVE:RAW?` | 首点 = 100+slot | 100% | 否 |
| `TEST_STRESS_GLITCH` | 乱码容错 | `TEST:GLITCH` | 后缀 _S{slot} | 100%* | 否 |
| `TEST_STRESS_DELAY` | 超长延迟 | `TEST:DELAY` | 后缀 _S{slot} | 可变** | 否 |

**注释**:
- \* 需要正确实现容错解析
- \*\* 取决于客户端超时设置

---

## ⚠️ 注意事项

### 1️⃣ **最重要的区别：命令格式**

| 设备 | 是否需要 Slot 参数？ | 原因 | 示例 |
|-----|------------------|------|------|
| **MotionController** | ✅ **必须带** | 1 个控制器管 12 个工位 | `HOME 1`, `CLAMP 2` |
| **LegacyDevice** | ❌ **不需要** | 12 个独立仪器，端口已区分 | `*IDN?`, `MEAS:VOLT?` |

### 2️⃣ **Slot 编码机制**
- LegacyDevice 每个返回值都嵌入了 slotId
- 对照结果中的 slot 标记，可以发现数据是否错乱
- 详见 [LegacyDevice 命令规格](LEGACY_DEVICE_SPEC.md) 的"Slot 编码机制"章节

### 3️⃣ **超时设置**
- 建议客户端默认超时 1000ms
- TEST:DELAY 需要超时 3500ms
- MotionController HOME 建议超时 2000ms

### 4️⃣ **响应格式不统一**
- LegacyDevice 故意返回多种格式
- 需要针对每个命令实现专用解析器

### 5️⃣ **随机 Pass/Fail**
- 电压和电流测试项会产生随机结果
- 这是预期行为（模拟真实产品质量分布）

### 6️⃣ **Slot 编号范围**
- LegacyDevice：1~12，端口 12303~12314，端口 = 12302 + slotId
- MotionController：接受任意整数，不校验范围
- Mock 运动控制上层：**0-based**（0~11），与其他设备不同

### 7️⃣ **并发处理**
- MotionController 采用并发处理，多个命令可同时执行
- 12 个 LegacyDevice 实例相互独立，可并行测试

---

## 📞 技术支持

如有问题，请参考：
- [MotionController 命令规格](MOTION_CONTROLLER_SPEC.md)
- [LegacyDevice 命令规格](LEGACY_DEVICE_SPEC.md)
- [Mock 运动控制上层规格](MOTION_UPPER_SPEC.md)
- [环境使用指南](ENVIRONMENT_README.md)
