# Stack Research

**Domain:** Windows CLI tool with Win32 API interop (window management / directional focus navigation)
**Researched:** 2026-02-26
**Confidence:** HIGH

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET 8 LTS | 8.0 (LTS) | Runtime and SDK | LTS with security support through Nov 2026. Best P/Invoke story in .NET, strong AOT support, fast startup. Project constraint already established. |
| C# 12 | Ships with .NET 8 SDK | Language | Latest stable language features with .NET 8. Primary pattern matching, records, and `partial` methods needed for source generators all GA. |
| `System.CommandLine` | 2.0.3 | CLI argument parsing, help text, exit codes | Stable (non-preview) as of Feb 2026. Trim-friendly and AOT-capable by design. Used by the .NET CLI itself, PowerShell, and Azure SDK. Zero ceremony for simple `direction` argument + flag overrides. |
| `Microsoft.Windows.CsWin32` | 0.3.269 | Win32 P/Invoke source generation | Microsoft-maintained source generator that produces correct, AOT-compatible P/Invoke declarations for EnumWindows, GetWindowRect, DwmGetWindowAttribute, SetForegroundWindow, keybd_event. Eliminates hand-written P/Invoke error surface. Replaces the deprecated `dotnet/pinvoke` NuGet assemblies. |
| `System.Text.Json` | Built into .NET 8 | JSON config file read/write | Ships with .NET 8, no extra dependency. AOT-compatible with source generation. Substantially faster than Newtonsoft.Json. Sufficient for simple config object (strategy, wrap, exclude list). |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Microsoft.Windows.SDK.Win32Metadata` | Latest (transitive via CsWin32) | Win32 API metadata feed for CsWin32 | Pulled automatically as CsWin32 dependency; never reference directly |
| `System.Text.Json` source gen context | Built-in code pattern | AOT-safe JSON serialization | Required if publishing as Native AOT — add `[JsonSerializable]` context class over config POCO |

**No other library dependencies are needed.** The project constraint explicitly calls for minimal dependencies (Win32 API via P/Invoke only, no third-party native dependencies). All required capabilities — window enumeration, positioning, focus switching — come from Windows system DLLs accessed through CsWin32-generated code.

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| .NET 8 SDK | Build, publish, test | Install via `winget install Microsoft.DotNet.SDK.8` — needed for `PublishAot` to work, requires MSVC toolchain (Visual Studio Desktop C++ workload) |
| Visual Studio 2022 / VS Code + C# DevKit | IDE | VS 2022 required for Native AOT publish on Windows (needs Desktop C++ workload); VS Code works for dev-time. AOT publish must run in full VS/CLI environment. |
| `dotnet publish -r win-x64 -c Release` | Produce final exe | Native AOT produces ~2–5 MB single-file exe with ~5–10ms startup vs ~150–200ms for JIT. Standard `dotnet publish --self-contained` produces ~50 MB+ self-contained but avoids AOT complexity. |

## Installation

```bash
# Create new console project targeting .NET 8
dotnet new console -n windowfocusnavigation --framework net8.0

# Core CLI parsing
dotnet add package System.CommandLine --version 2.0.3

# Win32 source generator (design-time / build-time only, no runtime assembly shipped)
dotnet add package Microsoft.Windows.CsWin32 --version 0.3.269

# System.Text.Json is already in-box for .NET 8 — no add needed
```

```xml
<!-- In .csproj — enable AOT-friendly settings -->
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <RootNamespace>WindowFocusNavigation</RootNamespace>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>  <!-- Required by CsWin32 generated code -->
  <!-- Optional: enable AOT publish path -->
  <!-- <PublishAot>true</PublishAot> -->
</PropertyGroup>

<!-- CsWin32 is a build-time analyzer only — mark accordingly -->
<ItemGroup>
  <PackageReference Include="Microsoft.Windows.CsWin32" Version="0.3.269">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  <PackageReference Include="System.CommandLine" Version="2.0.3" />
</ItemGroup>
```

```xml
<!-- NativeMethods.txt — tells CsWin32 which Win32 APIs to generate -->
<!-- Create this file at project root -->
EnumWindows
EnumChildWindows
GetWindowRect
GetClientRect
IsWindowVisible
IsIconic
GetForegroundWindow
SetForegroundWindow
keybd_event
GetWindowThreadProcessId
GetWindowLong
GetWindowText
GetWindowTextLength
DwmGetWindowAttribute
GetSystemMetrics
MonitorFromWindow
GetMonitorInfo
```

```json
// NativeMethods.json — CsWin32 configuration for AOT mode
{
  "allowMarshaling": false,
  "$schema": "https://aka.ms/CsWin32.schema.json"
}
```

### AOT publish command (optional, for sub-10ms startup)

```bash
# Requires VS 2022 Desktop C++ workload installed
dotnet publish -r win-x64 -c Release -p:PublishAot=true
```

### Standard self-contained publish (simpler, ~50ms startup)

```bash
dotnet publish -r win-x64 -c Release --self-contained true -p:PublishSingleFile=true
```

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| `System.CommandLine 2.0.3` | `Cocona`, `Spectre.Console.Cli`, `CommandLineParser` | Cocona/Spectre for richer DX on complex CLI apps; not needed here — this tool has one argument and 3-4 flags. System.CommandLine is Microsoft's official choice and is already AOT-compatible. |
| `CsWin32` source generator | Hand-written `[LibraryImport]` P/Invokes | Hand-writing is appropriate for 1-2 simple functions. This project needs 12+ Win32 functions including complex structs (RECT, MONITORINFO) — CsWin32 eliminates all struct mapping errors and keeps types correct. |
| `CsWin32` source generator | `dotnet/pinvoke` NuGet packages | `dotnet/pinvoke` is deprecated as of 2023 in favor of CsWin32. The project notice explicitly directs users to CsWin32. Never use for new projects. |
| `System.Text.Json` | `Newtonsoft.Json` | Use Newtonsoft if you need dynamic JSON (JObject), complex polymorphism, or `$type` handling. Simple config POCO with 4 fields needs none of that. System.Text.Json is AOT-safe, in-box, and 2-3x faster. |
| Native AOT publish | Self-contained publish | Self-contained is simpler (no MSVC toolchain requirement, no trim warnings to fix). Use Native AOT only if <100ms hotkey startup is not being met by self-contained. Start with self-contained; AOT is an optimization path. |
| .NET 8 LTS | .NET 9 or .NET 10 preview | .NET 9 is current (Nov 2024), but the project constraint specifies .NET 8 LTS. .NET 8 LTS is the correct choice for long-term stability. Can upgrade to .NET 10 LTS later. |

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `[DllImport]` for new P/Invoke declarations | Deprecated in favor of `[LibraryImport]` for .NET 7+. `DllImport` uses runtime IL stub generation which is incompatible with Native AOT and slower due to no inlining. SYSLIB1054 analyzer warns on each use. | `[LibraryImport]` if writing P/Invokes manually, or CsWin32 to generate them |
| `dotnet/pinvoke` NuGet packages (`PInvoke.User32`, etc.) | Officially deprecated in 2023. Project README directs all users to migrate to CsWin32. Will not receive Win32 API coverage updates. | `Microsoft.Windows.CsWin32` |
| `Newtonsoft.Json` | Adds a third-party dependency for a task in-box .NET 8 handles. Not AOT-compatible without significant extra work. 2-3x slower than System.Text.Json for simple reads. | `System.Text.Json` with source generation |
| `StringBuilder` in P/Invoke string parameters | Creates 4 allocations per call (documented in Microsoft interop best practices). Particularly harmful in window enumeration loops touching 20-100 windows. | `char[]` from `ArrayPool<char>`, or let CsWin32 handle it |
| `IntPtr` for HWND/HINSTANCE/HANDLE | `IntPtr` loses type safety — a HWND and a HICON are both IntPtr but not interchangeable. Hard to track passing wrong handle type. | `HWND`, `HICON`, etc. as generated by CsWin32 (strongly-typed wrappers) |
| `Marshal.SizeOf<T>()` for blittable structs | Reflection-based, not AOT-safe, slower. | `sizeof(T)` in unsafe context for blittable structs (RECT is blittable) |
| `Microsoft.Win32.Registry` for config | Registry is harder to edit, version, or back up than a JSON file. Adds friction for users wanting to inspect/share their config. | JSON config file in `%APPDATA%\windowfocusnavigation\config.json` |
| Background service / IHost / Generic Host | `Microsoft.Extensions.Hosting` adds 5-15 MB and 50-100ms startup overhead for dependency injection wiring. This is a stateless CLI tool called per-hotkey — no DI container is needed or appropriate. | Direct instantiation, static methods, or manual constructor injection |

## Stack Patterns by Variant

**If targeting Native AOT for sub-10ms startup:**
- Add `<CsWin32RunAsBuildTask>true</CsWin32RunAsBuildTask>` to csproj
- Add `"allowMarshaling": false` to `NativeMethods.json`
- Add `[JsonSerializable(typeof(AppConfig))]` JsonSerializerContext for System.Text.Json
- Add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (required by CsWin32 unsafe overloads)
- Requires Visual Studio 2022 + Desktop C++ workload on build machine
- Run `dotnet publish -r win-x64 -c Release -p:PublishAot=true`

**If targeting standard self-contained (simpler build, ~50ms startup — well within the 100ms target):**
- Skip CsWin32RunAsBuildTask and allowMarshaling settings
- Run `dotnet publish -r win-x64 -c Release --self-contained true -p:PublishSingleFile=true`
- Produces a single .exe that requires no .NET runtime installed
- Startup is ~40-80ms, comfortably under the 100ms hotkey budget
- **Recommended starting point** — optimize to AOT only if profiling shows startup is too slow

**If starting with framework-dependent (for development iteration speed):**
- Run `dotnet run -- left` during development
- No publish step needed
- Assumes .NET 8 runtime is installed (developer machine always has it)
- JIT startup ~150-200ms — fine for testing, exceeds production budget

## Version Compatibility

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| `System.CommandLine 2.0.3` | .NET 8.0, .NET Standard 2.0 | Stable release Feb 2026. AOT-capable. Backwards compatible from beta5 with migration guide needed if coming from 2.0.0-beta4 or earlier. |
| `Microsoft.Windows.CsWin32 0.3.269` | .NET 8+, C# 12, Roslyn source generators | Released Jan 2026. Requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`. AOT mode requires `CsWin32RunAsBuildTask=true` and `allowMarshaling: false`. |
| `System.Text.Json` (in-box) | .NET 8.0 | No additional package needed. Source generation for AOT requires `[JsonSerializable]` + `JsonSerializerContext` subclass. |
| .NET 8 SDK | Windows x64, Arm64 | Native AOT targets win-x64 and win-arm64. Win32 window APIs are x64/x86 compatible; no ARM-specific considerations for user32/dwmapi. |

## Sources

- [System.CommandLine overview — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/commandline/) — current status, AOT compatibility, NuGet package details (updated 2025-10-21)
- [Microsoft.Windows.CsWin32 GitHub](https://github.com/microsoft/CsWin32) — version 0.3.269, AOT support, NativeMethods.json config
- [CsWin32 AOT support discussion #1169](https://github.com/microsoft/CsWin32/discussions/1169) — AOT configuration requirements, `CsWin32RunAsBuildTask`, `allowMarshaling` — MEDIUM confidence (GitHub discussion, not official docs)
- [Native AOT deployment overview — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) — startup time benchmarks, P/Invoke compatibility, platform support (updated 2026-01-08)
- [Native interoperability best practices — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices) — LibraryImport vs DllImport, SafeHandle, blittable types (updated 2026-01-08)
- [P/Invoke source generation — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation) — LibraryImport source generator details
- [NuGet: System.CommandLine 2.0.3](https://www.nuget.org/packages/System.CommandLine) — stable release 2026-02-10, 70M total downloads
- [NuGet: Microsoft.Windows.CsWin32 0.3.269](https://www.nuget.org/packages/Microsoft.Windows.CsWin32) — released 2026-01-16, stable

---
*Stack research for: Windows directional window focus navigation CLI tool*
*Researched: 2026-02-26*
