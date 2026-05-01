# Plugin System Design

> SDK Version: 0.7.0 | Updated: 2026-04-30

## Overview

Catalytic Host uses a plugin-based architecture to support various communication protocols and custom tasks. Plugins are discovered at startup from the `plugins/` directory.

## Directory Structure

```
catalytic/
├── Catalytic.exe
├── config.json
└── plugins/
    ├── catalytic.socket-client/
    │   ├── manifest.json
    │   └── SocketClient.dll
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
        "protocols": ["tcp"],
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
    Task ActivateAsync(ICommChannel channel);
    Task DeactivateAsync();
}
```

### ICommunicator (For EngineControlled Mode)

```csharp
public interface ICommunicator : IPlugin
{
    string Protocol { get; }
    
    // Execute communication (results reported via channel.ReportData)
    Task Execute(
        int slotIndex,
        string address,
        CommAction action,
        string payload,
        CommOptions options,
        CancellationToken ct);
}

public class CommOptions
{
    public int TimeoutMs { get; set; }
    public string? Terminator { get; set; }      // Serialized delimiter
    public bool IsShared { get; set; }          // Dedicated vs Shared Resource Mode
}
```

### IProcessor (For Custom Business Logic)

```csharp
public interface IProcessor : IPlugin
{
    /// <summary>
    /// The command name supported by this processor.
    /// Used to reference this plugin in flow scripts.
    /// </summary>
    string Command { get; }
    
    Task ExecuteAsync(int slotIndex, CancellationToken ct);
}
```

### ICoordinator (For Flow Control/Interception)

```csharp
public interface ICoordinator : IPlugin
{
    // Return true to allow, false to skip/fail
    Task<bool> BeforeStepAsync(int slotIndex, int stepId, string stepName, CancellationToken ct);
    
    // Notification after step completes
    Task AfterStepAsync(int slotIndex, int stepId, string stepName, bool passed);
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
      "plugin_id": "catalytic.socket-client"
    },
    "special_instrument": {
      "protocol": "scpi",
      "plugin_id": "vendor.custom-scpi"
    }
  }
}
```

---

## Plugin Lifecycle

```
Host Startup
    │
    ├─ Scan plugins/ directory
    ├─ Load manifest.json for each plugin
    ├─ For each plugin: call ActivateAsync(channel)
    └─ Plugin is ready
    
During Runtime
    │
    ├─ Engine sends command
    ├─ Host finds matching plugin
    └─ Host calls plugin.Execute() or plugin.ExecuteAsync()
       └─ Plugin reports data/results back via channel or slot object

Host Shutdown
    │
    └─ For each plugin: call DeactivateAsync()
```

---

## Communication Channel (ICommChannel)

Plugins receive an `ICommChannel` when activated:

```csharp
public interface ICommChannel
{
    string PluginDirectory { get; }
    
    ICommunicator? GetCommunicator(string protocolOrId);
    
    /// Report raw data back to Host (Engine will then perform check rules)
    void ReportData(int slotIndex, string address, string data);

    /// Notify device connection state changes
    void NotifyState(string address, DeviceState state);
}
```

---

## Parameter Transmission Design

For `HostControlled` tasks (Extended mode), the system provides a **"Raw Conduit"** for custom parameters.

### User Interface Layer (Kotlin)
1. User enters a string in the "Parameters" text box.
2. The UI layer encodes this string using **Base64**.
3. It is stored in the flow script as a plain JSON string.

### Host Layer (C#)
1. When the Engine status reports a step index, the Host looks up the corresponding `Step` configuration.
2. The `Params` field is **decoded from Base64** back into its original string.

### Plugin Layer (C#)
1. The plugin calls `Host.Slot(x).GetCurrentStep().Params`.
2. It receives the **identical string** as entered in the UI.
3. The plugin developer interprets the content (e.g., JSON parsing).

---

## Host API

// Global commands
Host.AddPluginLog(id, msg);   
Host.StartAll();              

// Per-slot commands
Host.Slot(0).Start();         
Host.Slot(0).Stop();          
Host.Slot(0).SetSn("ABC");    

// Reporting (For IProcessor plugins)
Host.Slot(0).SubmitValue("3.31");                  // Submit value for Engine to judge
Host.Slot(0).Report(true, "3.31", "Check OK");     // Direct report (pass/fail + value)

// Get global flow information
var flow = Host.GetFlowDefinition();   // Returns TestFlow object
var folder = Host.ReportFolder();      

// Get current step info
var step = Host.Slot(0).GetCurrentStep(); // Returns Step object (Id, Name, Params, etc.)

// Read variables
var voltage = Host.Slot(0).GetVariable("voltage");  

// Global event subscription
Host.NotifySlotFinished += (args) =>
{
    var slotIdx = args.SlotIndex;
    var record = Host.Slot(slotIdx).GetTestHistory();  // Returns TestRecord? 
    // record.Steps contains StepRecord list with IsTestItem, CheckResult (strongly-typed), etc.
};
```

### IHostBridge (Host Implementation Interface)

```csharp
public interface IHostBridge
{
    void AddPluginLog(string pluginId, string message);
    void StartAll();
    void StopAll();

    void SlotStart(int slotIndex);
    void SlotStop(int slotIndex);
    void SetSlotSn(int slotIndex, string sn);
    string? SlotGetVariable(int slotIndex, string name);
    TestRecord? SlotGetHistory(int slotIndex);
    
    TestFlow? GetFlowDefinition();
    string GetReportFolder();

    Step? GetCurrentStep(int slotIndex);
    void ReportStepResult(int slotIndex, bool passed, string? failReason);
    void SubmitStepValue(int slotIndex, string value);
    void ReportStepResultWithValue(int slotIndex, bool passed, string value, string? reason);
}
```

---

## Thread Safety Contracts

| Component | Guarantee |
|-----------|-----------|
| `Host` static methods | Thread-safe |
| `Host` event subscribe/unsubscribe | Thread-safe |
| `IHostBridge` implementation | **Must be thread-safe** |
