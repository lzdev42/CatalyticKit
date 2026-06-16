# TestServer — OQC 设备模拟器

给 Catalytic OQC 软件当假设备用的。启动后，Catalytic 连上来就能跑测试，不用接真硬件。

---

## 一、启动

要求：装了 .NET 10 SDK。

```bash
dotnet run --project TestServer/TestServer.csproj
```

看到这些就说明成功了：

```
[12:00:00.123] [SYSTEM      ] Starting Device Simulators...
[12:00:00.125] [MotionUpper ] Listening on port 12300 (mode: Batch)
[12:00:00.126] [MotionCtrl  ] Listening on port 12301
[12:00:00.127] [Legacy-S01  ] Listening on port 12303
[12:00:00.128] [Legacy-S02  ] Listening on port 12304
...
[12:00:00.138] [Legacy-S12  ] Listening on port 12314
[12:00:00.139] [SYSTEM      ] All simulators are up and running! Waiting for Catalytic...
```

没有红线，15 个端口都起来，就是 OK。

---

## 二、它干了什么

启动后，会有 **15 个 TCP 服务**同时跑：

| 端口 | 是什么 | 你要连吗 |
|------|--------|---------|
| 12300 | 机械手（主动方） | Catalytic 作为客户端连这个 |
| 12301 | 运动控制器 | Catalytic 连这个发机械命令 |
| 12303~12314 | 12 个测试仪器 | Catalytic 连这些发测试命令 |

12300 比较特殊——它**主动给你发消息**（`start,0,3,7` 表示"这几个 slot 可以测了"），你要回 `pass,0,3` 和 `fail,7`，它会自动校验对不对得上。

---

## 三、怎么判断成功

### 3.1 能连上

用任何 TCP 工具（nc、telnet、Catalytic）连 `127.0.0.1:12303`，发一行：

```
*IDN?
```

回你一行就说明通了：

```
Acme Corp,LegacyModel-X,SN-998877-S01,v3.14
```

> 后面的 `S01` 是 slot 编号痕迹——连的是端口 12303（Slot 1），回的就是 S01。如果对不上，说明数据串了。

### 3.2 数据没乱

每个 slot 返回的值都**带着 slot 编号**，一眼就能看出有没有错乱：

| 命令 | Slot 1 返回 | Slot 3 返回 | 看哪里 |
|------|------------|------------|--------|
| `*IDN?` | ...`SN-998877-S01`... | ...`SN-998877-S03`... | 序列号尾巴 |
| `MEAS:VOLT?` | `VOLTAGE = 12.301 VDC` | `VOLTAGE = 12.303 VDC` | 小数末两位 |
| `MEAS:CURR?` | `Current: 0.401A (OK)` | `Current: 0.403A (OK)` | 小数末两位 |
| `SYST:STATUS?` | `...ALARM:0;SLOT:1` | `...ALARM:0;SLOT:3` | SLOT 字段 |
| `WAVE:RAW?` | `101,105,...` | `103,105,...` | 第一个数 |

**简单记**：如果你连的是 Slot 3，返回值里任何地方应该能看到 `3` 的痕迹。看到了就对，看不到就串了。

### 3.3 运动控制能用

连 12301，发：

```
HOME 1
```

回：

```
HOME_OK 1
```

就对了。（注意：所有命令都要带 slot 号）

### 3.4 12300 的轮次校验

Catalytic 连上 12300 后，模拟器会自动发 `start,...` 消息。控制台会打日志：

```
✅ 正常：Round-001 OK — start=[0,3,7] pass=[0,7] fail=[3]
❌ 出错：Round-002 MISMATCH — missing=[8] extra=[] dup=[]
```

看到 `OK` 就是 Catalytic 回的 pass/fail 和 start 对上了；看到 `MISMATCH` 就是对不上，程序目录下的 `mismatch.log` 有详细记录。

---

## 四、快速验证命令（手动）

不用 Catalytic 也能玩，用 `nc`（netcat）就行：

```bash
# 连 Slot 1 的仪器
nc 127.0.0.1 12303

# 然后敲这些（每行回车）：
*IDN?
MEAS:VOLT?
MEAS:CURR?
SYST:STATUS?
CLAMP 1          # ← 这个要连 12301
```

连 12301 测运动控制：

```bash
nc 127.0.0.1 12301
HOME 1
CLAMP 1
UNCLAMP 1
```

---

## 五、常见问题

| 问题 | 原因 | 解决 |
|------|------|------|
| `dotnet run` 报错没 SDK | 没装 .NET 10 | 装 `dotnet sdk 10.0` |
| 端口被占用 | 上次没关 | `kill` 占用进程或等一会再试 |
| 连上没反应 | 发了命令没按回车 | 所有命令以 `\n` 结尾，回车就行 |
| `ERR:MISSING_SLOT` | 连的 12301 但命令没带 slot | 改成 `HOME 1` 这样带数字 |

---

## 六、文档索引

想看细节的：

- [环境总览](docs/ENVIRONMENT_README.md) — 端口、协议一览
- [运动控制器](docs/MOTION_CONTROLLER_SPEC.md) — HOME/CLAMP/UNCLAMP 命令
- [测试仪器](docs/LEGACY_DEVICE_SPEC.md) — 11 条测试命令 + Slot 编码机制
- [运动控制上层](docs/MOTION_UPPER_SPEC.md) — start/pass/fail 协议
- [完整测试流程](docs/OQC_TEST_PROCEDURE.md) — 从上料到下料全流程
- [安全延迟配置](docs/SAFE_DELAY_CONFIG.md) — 超时和延迟建议值
