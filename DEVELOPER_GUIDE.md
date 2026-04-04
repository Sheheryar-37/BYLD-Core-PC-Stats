# Developer Guide: Plugin Architecture

BYLD Core is designed to be extensible via the `IWidgetPlugin` interface. This guide provides a deep dive into how the host application interacts with 3rd party DLLs.

## 🏗️ The Architecture
The software uses **Managed Extensibility Patterns** (System.Reflection) to load plugins at runtime.

1. **Discovery**: On startup, the `PluginManager` scans the `/Plugins` sub-directory for `.dll` files.
2. **Evaluation**: It opens each assembly and looks for types that implement the `IWidgetPlugin` interface.
3. **Instantiation**: If a valid type is found, an instance is created and added to the `LoadedPlugins` registry.
4. **Lifecycle**:
   - `Initialize(timerService)`: Called once. The plugin receives a reference to the main DispatcherTimer.
   - `GetUI()`: Called once. The plugin provides a WPF `UserControl` which is injected into a `WrapPanel` on the main dashboard.
   - `Update(hardwareData)`: Called every 1000ms. The plugin receives the latest `HardwareMetrics` object.

## 🧱 The IWidgetPlugin Contract
```csharp
public interface IWidgetPlugin
{
    string Name { get; }
    string Version { get; }
    string Author { get; }

    // Setup logic
    void Initialize(dynamic timerService);
    
    // Continuous logic (1s interval)
    void Update(dynamic hardwareData);
    
    // Visual entry point
    System.Windows.Controls.UserControl GetUI();
}
```

## 🔌 Integration Details
- **Dependency**: Your plugin must reference `PcStatsMonitor.PluginApi.dll`.
- **UI Container**: Plugins are rendered inside an `ItemsControl` that use a `WrapPanel`. Ensure your widget has a defined `Width` and `Height` (Recommended: ~200x60 for bars, or ~120x120 for small gauges).
- **Data Binding**: You can use the `Update` method to push data into your local ViewModel, or directly update UI properties via the Dispatcher.
