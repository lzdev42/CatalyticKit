# Plugin System Design

## Overview

Catalytic Host uses a plugin-based architecture to support various communication protocols and custom tasks. Plugins are discovered at startup from the `plugins/` directory.

## Directory Structure

```
catalytic/
├── Catalytic.exe
├── config.json
└── plugins/
    ├── catalytic.serial/
    │   ├── manifest.json
    │   └── Catalytic.Serial.dll
    ├── acme.modbus-driver/
    │   ├── manifest.json
    │   └── ModbusDriver.dll
    └── vendor.firmware-burner/
        ├── manifest.json
        └── FirmwareBurner.dll
```

**Rules:**
- Each plugin must be in its own directory
- Directory name = Plugin ID
- Each directory must contain a `manifest.json`

---

## Plugin Manifest

**manifest.json:**

```json
{
    "id": "acme.scpi-driver",
    "name": "SCPI Protocol Driver",
    "version": "1.0.0",
    "author": "Acme Corp",
    "entry": "AcmeScpiDriver.dll",
    "capabilities": {
        "protocols": ["serial"],
        "tasks": []
    }
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `id` | Yes | Unique plugin ID (format: `publisher.name`) |
| `name` | Yes | Human-readable display name |
| `version` | Yes | Semantic version |
| `author` | No | Author name |
| `entry` | Yes | Entry DLL filename |
| `capabilities.protocols` | No | List of protocols this plugin handles |
| `capabilities.tasks` | No | List of host tasks this plugin handles |

---

## Plugin Interfaces

Plugins must implement interfaces from `CatalyticKit`:

### IPlugin (Base Interface)

```csharp
public interface IPlugin
{
    string Id { get; }
    Task ActivateAsync(IPluginContext context);
    Task DeactivateAsync();
}
```

### ICommunicator (For EngineControlled Mode)

```csharp
public interface ICommunicator : IPlugin
{
    string Protocol { get; }
    
    Task<byte[]> ExecuteAsync(
        string address, 
        string action, 
        byte[] payload, 
        int timeoutMs, 
        CancellationToken ct);
}
```

### IProcessor (For HostControlled Mode)

```csharp
public interface IProcessor : IPlugin
{
    string TaskName { get; }
    
    Task<byte[]> ExecuteAsync(string parametersJson, CancellationToken ct);
}
```

---

## Plugin Discovery and Matching

### How Host Finds the Right Plugin

When Engine sends a command via `EngineTaskCallback`:

```
EngineTaskCallback(slot_id, task_id, device_type, device_address, protocol, ...)
```

Host uses this logic:

```
1. Get device_type from callback
2. Check device configuration: does this device_type have a specific plugin_id?
   ├─ YES → Use that plugin directly
   └─ NO  → Find plugin by protocol name
```

### Configuration Example

```json
{
  "device_types": {
    "dmm": {
      "protocol": "unused",
      "plugin_id": "catalytic.serial"
    },
    "special_instrument": {
      "protocol": "scpi",
      "plugin_id": "vendor.custom-scpi"
      // Explicitly use this specific plugin
    }
  }
}
```

### Matching Priority

| Priority | Condition | Action |
|----------|-----------|--------|
| 1 | `plugin_id` specified in device config | Use that exact plugin |
| 2 | No `plugin_id`, use `protocol` | Find plugin by `capabilities.protocols` |
| 3 | No matching plugin | Return error to Engine |

---

## Plugin Lifecycle

```
Host Startup
    │
    ├─ Scan plugins/ directory
    ├─ Load manifest.json for each plugin
    ├─ Build lookup tables:
    │   ├─ _pluginsById["acme.scpi-driver"] = ...
    │   └─ _pluginsByProtocol["scpi"] = ...
    │
    └─ For each plugin: call ActivateAsync()

During Runtime
    │
    ├─ Engine sends EngineTaskCallback(protocol="scpi")
    ├─ Host finds matching plugin
    └─ Host calls plugin.ExecuteAsync()

Host Shutdown
    │
    └─ For each plugin: call DeactivateAsync()
```

---

## Plugin Context

Plugins receive an `IPluginContext` when activated:

```csharp
public interface IPluginContext
{
    /// Plugin's directory path (for accessing bundled resources)
    string PluginDirectory { get; }
    
    /// Log a message (forwarded to Host's logging system)
    void Log(LogLevel level, string message);
    
    /// Get another protocol driver (for inter-plugin communication)
    ICommunicator? GetCommunicator(string protocolOrId);
    
    /// Push event to Host
    void PushEvent(string eventType, byte[] data);
}
```

---

## FAQ

### Can one plugin support multiple protocols?

Yes. Declare them in `capabilities.protocols`:

```json
{
    "capabilities": {
        "protocols": ["scpi", "scpi-raw", "visa"]
    }
}
```

### What if two plugins claim the same protocol?

Host startup will fail with an error. User must remove one plugin or explicitly assign `plugin_id` in device configuration.

### Can a plugin be both ICommunicator and IProcessor?

Yes. Declare both in `capabilities`:

```json
{
    "capabilities": {
        "protocols": ["scpi"],
        "tasks": ["calibrate", "self_test"]
    }
}
```

---

## Next Steps

- [ ] Implement `PluginManager` class in Host
- [ ] Implement `IPluginContext`
- [ ] Create `Catalytic.Plugin` library (interfaces for plugin developers)
- [ ] Create sample plugin
