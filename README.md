# CatalyticKit

Catalytic 插件开发 SDK。

## 安装

从 NuGet 安装（发布后可用）：

```bash
dotnet add package CatalyticKit
```

或直接引用 DLL：

```xml
<Reference Include="CatalyticKit">
  <HintPath>lib/CatalyticKit.dll</HintPath>
</Reference>
```

## 快速开始

```csharp
using CatalyticKit;

public class MyPlugin : ICommunicator
{
    public string Id => "my-company.my-plugin";
    public string Protocol => "my-protocol";

    public Task ActivateAsync(IPluginContext context) => Task.CompletedTask;
    public Task DeactivateAsync() => Task.CompletedTask;

    public Task<byte[]> ExecuteAsync(string address, string action, byte[] payload, int timeoutMs, CancellationToken ct)
    {
        // 你的通讯逻辑
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<byte[]> ExecuteAsync(string address, string action, byte[] payload, ExecuteOptions options, CancellationToken ct)
        => ExecuteAsync(address, action, payload, options.TimeoutMs, ct);
}
```

## 文档

- [插件系统设计](PLUGIN_SYSTEM.md) - 架构概览
- [插件开发指南](PLUGIN_DEVELOPER_GUIDE.md) - 完整教程

## 示例

| 示例 | 说明 |
|------|------|
| [SocketClient](SocketClient/) | TCP/IP 通讯插件 |

## License

MIT
