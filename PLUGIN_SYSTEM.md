# Plugin System Design

> SDK Version: 0.6.0 | Updated: 2026-03-25

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
    └── my-company.custom-logic/
        ├── manifest.json
        └── CustomLogic.dll
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
    
    // Execute task (no return value, result reported via PushEvent)
    Task ExecuteTask(
        int slotIndex,
        string address,
        CommAction action,
        string payload,
        ExecuteOptions options,
        CancellationToken ct);
}

public class ExecuteOptions
{
    public int TimeoutMs { get; set; }
    public string? Terminator { get; set; }      // Serialized delimiter
    public bool IsShared { get; set; }          // Dedicated vs Shared Resource Mode
}
```

### IProcessor

```csharp
public interface IProcessor : IPlugin
{
    string TaskName { get; }
    
    Task ExecuteAsync(int slotIndex, CancellationToken ct);
}
```

### IInterceptor

```csharp
public interface IInterceptor : IPlugin
{
    // Return true to allow, false to skip/fail
    Task<bool> BeforeStepAsync(int slotIndex, int stepId, string stepName, CancellationToken ct);
    
    // Notification after step completes
    Task AfterStepAsync(int slotIndex, int stepId, string stepName, bool passed);
}
```

> **Restriction**: Only ONE `IInterceptor` is allowed globally.

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
    └─ Host calls plugin.ExecuteTask()
       (Plugin later calls context.PushEvent to report result)

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
    
    /// Get another protocol driver (for inter-plugin communication)
    ICommunicator? GetCommunicator(string protocolOrId);
    
    /// Push event to Host (Result will be routed to Engine)
    void PushEvent(int slotIndex, string address, PluginEventType eventType, string data);

    /// Notify device connection state change (Routed to DeviceManager by address)
    void NotifyConnectionStateChanged(string address, PluginDeviceConnectionState state);
}
```

---

## Parameter Transmission Design

For `HostControlled` tasks (Extended mode), the system provides a **"Raw Conduit"** for custom parameters.

### User Interface Layer (Kotlin)
1. User enters a string in the "Parameters" text box.
2. The UI layer encodes this string using **Base64**.
3. It is stored in the flow script as a plain JSON string (avoiding escaping issues with special characters in nested JSON).

### Host Layer (C#)
1. When the Engine status reports a step index, the Host looks up the corresponding `StepDefinition`.
2. The `Params` field is **decoded from Base64** back into its original UTF-8 string.
3. This "Original String" is stored in the `StepContext` for that slot.

### Plugin Layer (C#)
1. The plugin calls `Service.Slot(x).GetCurrentStep().Params`.
2. It receives the **identical string** as entered in the UI, regardless of line breaks, quotes, or JSON-like syntax.
3. The plugin developer is responsible for interpreting the content (e.g., using `JsonConvert.DeserializeObject` if the expected format is JSON).

> [!TIP]
> This design ensures that the system is entirely agnostic to the parameter format. The parameter box acts as a binary-safe buffer between the user and the plugin.

---

## Service API

Plugins can actively control the test flow via the static `Service` class:

```csharp
// Global commands
Service.AddPluginLog(id, msg);   // Record plugin specific log
Service.StartAll();              // Start all slots

// Per-slot commands
Service.Slot(0).Start();         // Start slot 0
Service.Slot(0).Stop();          // Stop slot 0
Service.Slot(0).SetSN("ABC");    // Set SN for slot 0

// Get global flow information [NEW v0.4.1]
var flow = Service.GetFlowDefinition();   // Get all steps, limits, labels
var folder = Service.ReportFolder();      // Get absolute path to {WorkDir}/reports

// Read variables (extracted by Engine steps)
var voltage = Service.Slot(0).GetVariable("voltage");  // Returns JSON string or null

// Get full test history after test completes (call in TestFinished handler)
Service.Slot(0).TestFinished += (passed, _) =>
{
    var record = Service.Slot(0).GetTestHistory();  // Returns TestRecord? 
    // record.Steps contains StepRecord list with IsTestItem, Check (strongly-typed), etc.
};

// Per-slot events
Service.Slot(0).TestStarted += () => { /* ... */ };
Service.Slot(0).TestFinished += (passed, msg) => { /* ... */ };
Service.Slot(0).StepFinished += (stepIndex, passed) => { /* ... */ };
```

> **Note**: `SetVariable` is a no-op. Variables are managed by Engine steps (via parse rules) and read-only from the plugin side. Use `GetVariable` to read extracted values.

### IHostBridge (Host Must Implement)

Host provides the actual implementation via `Service.SetBridge(bridge)` at startup:

```csharp
public interface IHostBridge
{
    // Global Commands
    void AddPluginLog(string pluginId, string message); // 记录日志
    void StartAll();
    void StopAll();

    // Slot Commands
    void SlotStart(int slotIndex);
    void SlotStop(int slotIndex);
    void SlotSetSN(int slotIndex, string sn);
    void SlotSetVariable(int slotIndex, string name, string jsonValue);  // No-op (variables managed by Engine)
    string? SlotGetVariable(int slotIndex, string name);  // Reads from Engine VariablePool
    TestRecord? SlotGetHistory(int slotIndex);  // Returns full typed test history; call after TestFinished
    
    // Global flow info
    FlowDefinition? GetFlowDefinition();
    string GetReportFolder();

    // Event Subscription
    void SubscribeSlotEvents(int slotIndex, ISlotEventHandler handler);
    void UnsubscribeSlotEvents(int slotIndex, ISlotEventHandler handler);

    // Error Reporting
    void ReportPluginError(string pluginId, Exception exception);
}
```

> **Note**: All `IHostBridge` methods may be called from any thread concurrently. Implementations must be thread-safe.
>
> **Implementation Detail**: Engine automatically resets all slot data (variables, step results, errors) when `SlotStart` is called, ensuring clean state for each test run.

---

## Thread Safety Contracts

| Component | Guarantee |
|-----------|-----------|
| `Service` static methods | Thread-safe (volatile + ConcurrentDictionary) |
| `ISlot` event subscribe/unsubscribe | Thread-safe (lock-protected) |
| `ISlot` event invocation | Snapshot pattern (no deadlock if handler calls Service) |
| `ISlot` event handler exceptions | Caught by SDK, reported via `IHostBridge.ReportPluginError()` |
| `IHostBridge` implementation | **Must be thread-safe** (Host responsibility) |

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
        "tasks": ["my_extension"]
    }
}
```
