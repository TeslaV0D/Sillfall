# ADR-0001: Tech Stack

- Status: accepted
- Date: 2026-11-11
- Milestone: M0

## Context

Sillfall renders animated weather above open windows. The expensive part is GPU-bound; CPU simulation handles at most 20,000 particles. Development is largely done by a coding agent that often runs in a Linux container without a Windows desktop.

## Decision

Option A from PLAN.md, section 1: **C# on .NET 10, Direct3D 11 and DirectComposition via Vortice.Windows, Win32 interop via CsWin32, UI with WinUI 3.**

Rejected:

- **C++20 with C++/WinRT:** Smallest footprint, but slower iteration, and core logic would be hard to test on Linux.
- **Rust with windows-rs:** Safe and fast, but WinUI integration is painful, and DirectComposition transparency with wgpu is fiddly.

## M0 specifics

These items refine the plan or deviate slightly.

| Topic | Decision | Rationale |
|---|---|---|
| Target framework for Windows projects | `net10.0-windows10.0.26100.0`, minimum version 10.0.19041 (instead of `net10.0-windows`) | `net10.0-windows` implies Windows 7. Analyzer CA1416 then rejects APIs like `GetThreadDpiAwarenessContext` immediately. With minimum version 19041 (first version with `WDA_EXCLUDEFROMCAPTURE`) it flags exactly the APIs that need a version check. |
| Windows App SDK | 2.6.5 | Current stable line. Project file follows the official template `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`. |
| Platforms | x64 and ARM64, no x86 | As in the plan (CI builds x64 and ARM64). |
| Solution format | `Sillfall.slnx` | Standard format of the .NET 10 SDK. `Sillfall.CrossPlatform.slnf` filters out the WinUI app for Linux. |
| Test runner | xUnit v3 on Microsoft.Testing.Platform | xUnit v3 builds on this under .NET 10; the old VSTest path is no longer supported. |
| Assertions | AwesomeAssertions instead of FluentAssertions | Licensing; see ADR-0002. |
| `Vortice.D3DCompiler` | Not referenced yet | Shaders are compiled at build time per the plan. The package is added only if runtime compilation is needed. |
| Tray icon (`H.NotifyIcon.WinUI`) | Not referenced yet | Decided with the minimal tray (MVP cut). |
| App start in CI | `--smoke-test=<file>` | The app starts on the Windows runner, checks DPI awareness and the Direct3D device, then exits. Does not replace manual testing, but catches startup failures. |

## Consequences

- Roughly 60% of the code (simulation, geometry, weather mapping, settings) is buildable and testable on Linux. Platform, Render, and Audio also compile on Linux (`EnableWindowsTargeting`) but cannot run there.
- The WinUI app builds only on Windows. The `windows` CI job is the gate for that.
- Somewhat more RAM than C++ (~80–120 MB) and GC discipline in the frame loop (rule 3 in CLAUDE.md).
