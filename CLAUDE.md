# CLAUDE.md

This file provides guidance to Claude Code when working with this repository.

## Project Overview

**BYLD Core (PC Stats Monitor)** — a premium hardware monitoring, fan control, and RGB lighting management dashboard.

- **Framework:** .NET 10.0 (WPF / Windows)
- **Architecture:** MVVM (CommunityToolkit.Mvvm) with Microsoft.Extensions.DependencyInjection and Microsoft.Extensions.Hosting

## Tech Stack & Key Libraries

- **UI:** WPF with heavily customized, glassmorphic XAML templates
- **Hardware metrics:** LibreHardwareMonitorLib (CPU/GPU temps, loads, RAM, etc.)
- **RGB control:** OpenRGB.NET (communicates with a bundled OpenRGB SDK Server)
- **Logging:** Serilog (file and console sinks)
- **Installer:** Inno Setup (`PcStatsMonitor_Installer.iss`)

## Project Structure

- `Controls/` — custom WPF user controls (fan control, RGB control views)
- `ViewModels/` — MVVM view models
- `Models/` — data models
- `Services/` — core services (`HardwareControlService`, `PawnIoDriverService`, `MouseHookService`, etc.)
- `Converters/` — WPF value converters
- `PcStatsMonitor.PluginApi/` — plugin API project
- `Tools/LicenseGenerator/` — license key generator utility
- `Assets/` — images, icons, and other resources

## Strict Coding Rules (MUST FOLLOW)

1. **Method complexity:** Maintain a strict maximum of 2 levels of indentation per method. If a 3rd level is required, instantly break the logic out into a well-named helper method.
2. **Dependency injection:** Strictly adhere to the Dependency Inversion Principle. Always use constructor injection. Keep business logic and core domains completely isolated from external frameworks.
3. **Concurrency:** Enforce `async/await` best practices down the entire call stack. Never use `async void` except for WPF UI event handlers. No synchronous blocking calls (`.Result` or `.Wait()`).
4. **Code quality:** Adhere to DRY and KISS principles. Do not touch or refactor legacy code unless absolutely necessary to fulfill the prompt. Add XML documentation (`///`) for all interfaces, models, entities, DTOs, and public methods.

## UI & Aesthetic Guidelines

- **Strict theme:** Enforce the "dark, jelly-like shiny layout" design language.
- **Visuals:** Heavy glassmorphism — `#0CFFFFFF` or `#0AFFFFFF` backgrounds, `BorderBrush` `#1AFFFFFF` or `#22FFFFFF`, dark themes, subtle blurs, and `BrandBlue` (`#3b82f6`) or `YellowAccent` (`#F5C518`) glowing accents for a premium look.
- **Rule:** NO generic, default flat UI elements (like standard grey buttons or comboboxes). Every control must be custom-templated.

## Build, Security & Deployment Workflows

- **Privileges:** The app requires Administrator rights to install/verify the hardware sensor driver and to read CPU MSRs and the motherboard Super I/O.
- **Driver setup:** Handled by `Services/PawnIoDriverService.cs`. LibreHardwareMonitor reads CPU temperature/clock and the motherboard Super I/O through **PawnIO** — a Microsoft-signed, sandboxed driver (it replaced the blocklisted WinRing0 in LHM 0.9.5-pre454). On first run the app silently installs the bundled `PawnIO_setup.exe`; if it is missing it points the user to https://pawnio.eu/. The official `PawnIO_setup.exe` must be placed at the repo root before publishing (it is git-ignored, not committed).
- **Signing / build process:**
  1. `dotnet publish` to the `publish` folder.
  2. Run `Sign-Installer.ps1` to self-sign all unsigned `.dll` and `.exe` files to bypass Smart App Control.
  3. Compile `PcStatsMonitor_Installer.iss` in Inno Setup.
  4. Run `Sign-Installer.ps1` again to sign the final `Setup.exe` located in the `InstallerOutput` folder.
