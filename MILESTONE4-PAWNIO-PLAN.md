# Milestone 4 — PawnIO Integration Plan

**Status:** planned · **Scope:** driver layer swap + internal refactor · **UI/UX impact:** none

## Why

Every unresolved hardware issue from Milestone 3 traces to a single root cause:
`WinRing0x64.sys` is on Microsoft's Vulnerable Driver Blocklist (CVE-2020-14979)
and is silently blocked on Windows 11 22H2+. No application-side workaround can
reach the motherboard Super I/O chip or the CPU MSRs while that driver is blocked.

Confirmed symptoms on the client machine (Win 11 26200, ASRock B650I Lightning
WiFi, Ryzen 7 7700X, Radeon RX 9070):

- CPU temperature (Tctl/Tdie) reads `0` — the app falls back to an iGPU SoC proxy
- Motherboard reports **0 sub-hardware** — case fans and the CPU fan header are invisible
- Fan-control writes fall back to the GPU, whose rejected driver calls throttle the PC

[PawnIO](https://pawnio.eu/) is the modern replacement: a Microsoft-signed,
sandboxed driver that runs signed modules and exposes them over an ioctl
interface. It is **not** blocklisted, so it loads under HVCI / Smart App Control.
LibreHardwareMonitor has already merged the swap
([PR #1857](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/pull/1857)),
and Fan Control (the client's reference application) migrated to it in V238.

**Licensing:** free, GPL v2, with an explicit exception for programs that
communicate with PawnIO *solely through the device IO control interface* — which
is how LibreHardwareMonitor uses it. BYLD Core therefore remains closed-source;
the only obligation is to ship PawnIO's licence/source notice for the bundled
driver and modules. Commercial licensing is available from `admin@namazso.eu` if
ever required.

## Guiding constraints

1. **Zero UI/UX change.** No XAML visual trees, styles, colours, layouts, labels
   or tab structures are altered. The user sees the same application — the
   hardware features simply begin working.
2. **PawnIO replaces WinRing0** as the hardware-access layer.
3. **Refactor to international standards** — SOLID, constructor injection,
   async down the call stack, XML documentation on public members, and the
   project's strict 2-level indentation rule — with proper code segregation.

---

## Implementation note (2026-07-21) — the library was already on PawnIO

Diagnosing Joe's `hardware (7)` logs against his LibreHardwareMonitor screenshot
revealed the real state: our pinned `LibreHardwareMonitorLib 0.9.7-pre686` **already
uses PawnIO internally** — the WinRing0→PawnIO swap landed upstream in
`0.9.5-pre454` (commit `eb5e1a2`), well before build 686
([LHM issue #2088](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues/2088)).
So the app was installing WinRing0 (via `KernelDriverService`) that the library no
longer calls, while **PawnIO — which it does call — was never installed** on the
client machine. His logs even show WinRing0 loading and opening successfully, yet
CPU temp still reads 0: proof the library ignores it.

That makes Milestone 4 an **install-the-right-driver + remove-dead-code** change,
not a hardware-layer rewrite. No library upgrade was required; no LHM API churn.
Delivered: `KernelDriverService` deleted, `PawnIoDriverService` added (detect →
silent-install bundled `PawnIO_setup.exe` → verify), all call sites rewired, the
installer/csproj/docs updated, and the Windows Defender exclusion dropped (PawnIO
is signed, so it is unnecessary).

## Phase 1 — Driver layer swap

| File | Change |
| --- | --- |
| `Services/KernelDriverService.cs` (26 KB) | **Removed.** WinRing0 SCM install, err-183 conflict recovery, foreign-service reclaim, Defender exclusion and device probing all go away |
| **New** `Services/Drivers/PawnIoDriverService.cs` | Detect/install PawnIO, load required modules, verify availability |
| `App.xaml.cs` | Startup `EnsureInstalled` + `VerifyRing0WithProbe` → PawnIO availability check |
| `PcStatsMonitor.csproj` | Upgrade `LibreHardwareMonitorLib` `0.9.7-pre686` → PawnIO-based build |
| `PcStatsMonitor_Installer.iss` | Stop shipping `WinRing0x64.sys`; bundle and register PawnIO; drop the `sc stop` / `sc delete` uninstall steps |

Files currently referencing WinRing0/Ring0 that need review:
`KernelDriverService.cs`, `HardwareControlService.cs`, `HardwareMonitorService.cs`,
`WmiSensorService.cs`, `FanControlViewModel.cs`, `SettingsWindow.xaml.cs`,
`App.xaml.cs`, `PcStatsMonitor_Installer.iss`.

## Phase 2 — What starts working

- **CPU temperature** — real Tctl/Tdie instead of an estimate
- **Case fans and CPU fan** — detected *and* controllable via the motherboard Super I/O
- **Motherboard VRM / chipset temperatures and voltages** — real values
- **Throttling resolved at source** — a proper motherboard fan path replaces
  repeatedly poking a GPU that rejects the writes
- **No Windows Security workarounds** — PawnIO is Windows-trusted, so the
  "disable Smart App Control" instructions disappear

## Phase 3 — What else moves off the old flow

### Can be replaced (workarounds that become unnecessary)

| Current workaround | Replaced by |
| --- | --- |
| CPU temp via iGPU SoC proxy (`_cachedIgpuSocTemp` in `HardwareMonitorService`, `CpuProxySource` in `FanControlViewModel`) | Real CPU MSR temperature |
| CPU clock via PerformanceCounter / WMI | Real MSR clock |
| "GPU temp as VRM proxy" | Real motherboard temperature |
| "GPU fan as board-fan fallback" | Real chassis fan RPM |
| `Services/WmiSensorService.cs` — the whole WMI thermal fallback stack | Demoted to a last-resort path for machines without PawnIO, or removed |

### Stays on the current flow (unchanged)

- **RGB via OpenRGB** — a separate stack that works today. OpenRGB has its own
  PawnIO merge request upstream; adopting it is an optional future item.
- **GPU sensors and GPU fan** — AMD ADL through LibreHardwareMonitor. The RX 9070
  fan-write limitation is vendor/library-side and is **not** affected by PawnIO.
- **Storage, memory and network sensors** — standard LibreHardwareMonitor APIs,
  no ring-0 access required.
- **Weather, clock, themes, screen rotation, licensing, installer signing** — untouched.

## Phase 4 — Refactor and segregation (internal only)

| Current | Problem | Split into |
| --- | --- | --- |
| `SettingsWindow.xaml.cs` — 1,413 lines | God-class | Per-tab partials/handlers (Appearance, Colors, Layout, Clock, Weather, Fans, RGB, Registration) |
| `HardwareControlService.cs` — 29 KB | LHM lifecycle + fan control + OpenRGB client + write queue + dedup in one type | `HardwareSensorProvider`, `FanControlService`, `RgbService`, `RgbWriteQueue` |
| `HardwareMonitorService.cs` — 32 KB | Polling loop + metric mapping + WMI fallbacks + disk mapping mixed | Polling loop vs. `CpuReader` / `GpuReader` / `StorageReader` / `NetworkReader` |
| `FanControlViewModel.cs` — 38 KB | View-model + curve engine + persistence | Extract `FanCurveEngine`; keep persistence separate |
| `RgbControlViewModel.cs` — 29 KB | View-model + persistence + snapshot logic | Extract persistence and snapshot services |
| `Models/ThemeConfig.cs` — 22 KB | Config model + brushes + palette logic + registry reads | Extract brush/resolution helpers |

Target layout: `Services/Hardware/`, `Services/Drivers/`, `Services/Rgb/`,
`Core/` (domain models), with constructor injection throughout and interfaces at
the boundaries.

**Explicitly untouched:** every `.xaml` file's visual tree, all styling and
theming, and all user-facing behaviour.

## Phase 5 — Validation

- Verify on the client configuration (Win 11 26200, B650I, RX 9070): real CPU
  temperature, Super I/O detection, case-fan RPM **and** control, throttling gone
- Full regression: RGB, themes, 7" rotation, installer and uninstaller
- Retain the Milestone 3 safety nets — fan-unresponsive auto-disable and the
  "Force fan control" override — as fallbacks

## Risks

1. ~~**Super I/O module availability** for the ASRock B650I's sensor chip.~~
   **Checked 2026-07-20 — cleared.** See "Super I/O verification" below.
2. **LibreHardwareMonitor upgrade** may introduce API changes requiring compile
   fixes across the sensor code.
3. **PawnIO install dependency** in the installer (signed, but requires admin).
4. **RX 9070 fan writes** remain vendor-dependent — already communicated to the client.

---

## Super I/O verification (checked 2026-07-20)

**Chip:** the ASRock B650I Lightning WiFi uses a **Nuvoton NCT6799D** — the
standard sensor controller across ASRock's B650 line. HWiNFO's changelog records
enhanced sensor monitoring added for this exact board.

**PawnIO does not ship per-chip Super I/O modules, and does not need to.**
[`LpcIO.p`](https://github.com/namazso/PawnIO.Modules) is a *generic* LPC
port-access module:

| ioctl | Purpose |
| --- | --- |
| `ioctl_select_slot` | Select chip slot — `0x2e/0x2f` or `0x4e/0x4f` |
| `ioctl_find_bars` | Discover the chip's I/O ranges from its own configuration |
| `ioctl_superio_inb` / `inw` / `outb` | Super I/O register read/write |
| `ioctl_pio_inb` / `outb` | Raw port access within validated ranges |

It validates chip presence by reading register `0x20` (rejecting `0x00`/`0xFF`)
and acquires the standard `Access_ISABUS.HTP.Method` mutex, so it coexists
correctly with other monitoring tools. This is precisely the primitive WinRing0
provided — sandboxed and Microsoft-signed instead of blocklisted.

Chip-specific knowledge stays where it already lives: LibreHardwareMonitor's
[`Nct677X.cs`](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LibreHardwareMonitorLib/Hardware/Motherboard/Lpc/Nct677X.cs)
carries the NCT6799D under chip ID `0xD802`. **Split:** PawnIO supplies port
access, LHM already knows how to drive the chip.

**Real-world confirmation on the same chip:**
[LHM issue #1993](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues/1993)
covers an ASRock X870E Nova — a modern ASRock AM5 board with the same NCT6799D —
running the current PawnIO-based LibreHardwareMonitor. There the chip **is**
detected, fans enumerate with live RPM (Fan #5 at 7,541 RPM), and fan-curve
control works. The only defect reported is one niche PCH temperature sensor not
being exposed.

**Remaining caveats:**

- **Chip ID collision** —
  [issue #2005](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues/2005):
  NCT6796D-S and NCT6799D share base chip ID `0xD8`, distinguished only by
  revision. Both are handled by the same driver family, so misidentification is
  cosmetic (sensor labelling) rather than functional.
- **The chip on the client machine is inferred, not measured** — it cannot be
  read while WinRing0 is blocked.

**Pre-work validation (zero cost, no code):** have the client run the stock
**LibreHardwareMonitor release**, which already ships the PawnIO path, and
screenshot the Motherboard node. Case fans appearing there with RPM settles this
empirically on the target hardware before the refactor begins.

## References

- PawnIO — https://pawnio.eu/ · https://github.com/namazso/PawnIO
- PawnIO modules — https://github.com/namazso/PawnIO.Modules
- LibreHardwareMonitor PR #1857 (WinRing0 → PawnIO) — https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/pull/1857
