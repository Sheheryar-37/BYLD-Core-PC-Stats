# BYLD Core: PC Stats Monitoring Software

**BYLD Core** is a premium, glassmorphism-inspired PC statistics monitoring tool designed for vertically oriented internal HDMI displays. It provides real-time telemetry for CPU, GPU, RAM, Storage, and Network, with a highly customizable layout and a robust plugin architecture.

## 🚀 Application Features
- **Modern Glass UI**: Dark-themed, translucent interface with vibrant accent glows.
- **Multi-Display Targeting**: Automatically detects and snaps to secondary internal displays on startup.
- **Configurable Gauges**: Adjust sizes, positions, and visibility for every hardware sensor.
- **Simulated Hardware Visuals**: High-fidelity SSD/Storage cards that reflect actual hardware vendor names.
- **Dynamic Theming**: Change background images, logos, opacity, and colors at runtime via a built-in settings panel.
- **Plugin Architecture**: Extend the dashboard with custom 3rd party widgets.

## 📂 Project Structure
- `PcStatsMonitor/`: Main WPF application project.
  - `Controls/`: Custom glassy UI components (Gauges, MessageBox, etc).
  - `Models/`: Data configurations and Metric definitions.
  - `Services/`: Hardware monitoring (LibreHardwareMonitor), Theming, and Plugin loading.
  - `ViewModels/`: UI logic following MVVM patterns.
- `PcStatsMonitor.PluginApi/`: The lightweight DLL developers use to build custom widgets.

## 🛠️ Requirements
- **OS**: Windows 11 (Supports Windows 10)
- **Runtime**: .NET 9.0 (Windows Desktop Runtime)
- **Permissions**: Requires Administrator rights for hardware sensor access (via WinRing0).

## 🔨 Build Instructions
1. Open `PcStatsMonitor.sln` in Visual Studio 2022+ or VS Code.
2. Build the solution (targets .NET 9.0).
3. Ensure `WinRing0x64.sys` is present in the output directory.

---
© 2026 BYLD PC Stats Monitor Project.
