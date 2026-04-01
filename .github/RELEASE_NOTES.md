## 下载说明

### Catalytic 主程序

| 文件 | 平台 |
|------|------|
| `Catalytic-arm64.dmg` | macOS（Apple Silicon） |
| `Catalytic-x64.dmg` | macOS（Intel） |
| `Catalytic-arm64.AppImage` | Linux ARM64 |
| `Catalytic-x64.AppImage` | Linux x64 |
| `Catalytic-arm64.zip` | Windows ARM64 |
| `Catalytic-x64.zip` | Windows x64 |

### 插件

下载插件 zip 后解压，将解压出的**整个文件夹**放入 Catalytic 工作目录的 `plugins/` 目录下：

```
plugins/
├── CatalyticSerialPort/
│   ├── CatalyticSerialPort.dll
│   └── manifest.json
├── CsvReporter/
│   ├── CsvReporter.dll
│   └── manifest.json
└── SocketClient/
    ├── SocketClient.dll
    └── manifest.json
```

| 插件 | 说明 |
|------|------|
| `CatalyticSerialPort` | 串口通信插件 |
| `CsvReporter` | CSV 数据导出插件 |
| `SocketClient` | Socket 客户端插件 |
