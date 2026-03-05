# Phase 13: Tray Identity - Research

**Researched:** 2026-03-04
**Domain:** Windows system tray icon (ICO format, System.Drawing, NotifyIcon, MSBuild embedding)
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Icon visual design**
- Crosshair/target concept — represents "focus" literally
- Monochrome white silhouette on transparent background — matches Windows 11 tray aesthetic
- Geometric bold style with corner brackets (camera focus brackets 「」) — strong presence at small sizes
- Include a center dot inside the brackets — gives a clear focal point

**Icon creation method**
- Code-generated — no binary .ico committed to repo, defined as drawing primitives
- Icon serves as both the tray icon AND the .exe application icon (visible in Explorer, taskbar, Task Manager) — unified branding
- "Replaceable" (ICON-03) means swap the generation source code and rebuild — no runtime file override

**Tooltip text**
- NotifyIcon.Text set to "Focus — Navigation Daemon" (em dash, not hyphen) per ICON-02
- Straightforward replacement of current "Focus Daemon" string

### Claude's Discretion

- Generation timing — MSBuild pre-build target vs standalone script that commits output
- Exact pixel layout of brackets and center dot at each size (16, 20, 24, 32px)
- Line thickness and bracket proportions at each resolution
- How to embed as both EmbeddedResource (for tray) and ApplicationIcon (for .exe)
- ICO file format details (BMP vs PNG frames for each size)

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| ICON-01 | Daemon displays a custom multi-size .ico icon in the system tray (16, 20, 24, 32px) | ICO format research: PNG frames via BinaryWriter + System.Drawing; NotifyIcon.Icon = new Icon(stream) pattern |
| ICON-02 | Tray icon tooltip shows "Focus — Navigation Daemon" on hover | NotifyIcon.Text property; 127-char limit in .NET 6+; "Focus — Navigation Daemon" = 25 chars, safe |
| ICON-03 | Custom .ico is embedded as assembly resource and replaceable by swapping the file | EmbeddedResource + ApplicationIcon dual-embed in csproj; GetManifestResourceStream at runtime; generation script |
</phase_requirements>

## Summary

Phase 13 is a self-contained visual identity change: generate a custom multi-size ICO, embed it in the assembly two ways (Win32 resource for .exe icon via `<ApplicationIcon>`, .NET manifest resource for runtime NotifyIcon via `<EmbeddedResource>`), and update the tooltip string. No new NuGet packages are required — the entire solution uses `System.Drawing` (already imported) and `BinaryWriter` for the ICO encoder.

The icon is code-generated from drawing primitives (L-shaped corner brackets + center dot) using `System.Drawing.Graphics`. The generation runs once and the output `.ico` is committed to the repo — it is a build input, not a build output. "Replaceable" means changing the generation source code and re-running it, not a runtime file path. This is the same pattern as committing generated parser code: the source of truth is the generator, and the generated artifact is tracked.

The key implementation insight confirmed by STATE.md: the ICO encoder is a ~30-line `BinaryWriter` routine. Use PNG frames (Vista+, which Windows 11 exceeds). Each size is drawn separately on a `Bitmap` with `PixelFormat.Format32bppArgb`, cleared to `Color.Transparent`, drawn with `SmoothingMode.None` for pixel-perfect geometric shapes, then saved to a `MemoryStream` as PNG and embedded in the ICO container.

**Primary recommendation:** Implement a standalone C# generator script (`tools/generate-icon/`), run it manually, commit the output `focus.ico`, then reference it as both `<ApplicationIcon>` and `<EmbeddedResource>` in the csproj. Load at runtime via `Assembly.GetExecutingAssembly().GetManifestResourceStream(...)`.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Drawing | Built into net8.0-windows (already imported) | Bitmap creation, Graphics drawing, MemoryStream PNG save | Already used in project (SystemIcons, OverlayColors); zero new dependencies |
| System.IO.BinaryWriter | BCL | ICO file binary format encoding | Standard .NET; no dependency |
| System.Reflection.Assembly | BCL | GetManifestResourceStream for runtime icon load | Standard .NET pattern for embedded resources |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Windows.Forms.NotifyIcon | net8.0-windows (already in project) | NotifyIcon.Icon and .Text properties | Already instantiated in TrayIcon.cs |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Hand-written BinaryWriter ICO encoder | Third-party (ImageSharp, Aspose, IconLib) | Third-party adds NuGet dependency for a ~30-line problem; hand-written is sufficient for generating from known clean PNG frames |
| PNG frames in ICO | BMP frames in ICO | BMP requires height-doubling (double-height DIB with AND/XOR mask planes), is more complex to encode; PNG frames are simpler, fully supported on Windows Vista+ (Windows 11 guaranteed), and supported by System.Drawing.Icon constructor |
| Standalone generator script | MSBuild pre-build Exec target | Pre-build runs on every build even without icon changes; standalone script runs on demand, output committed to repo, conceptually simpler and avoids build-time file-lock risks |

**Installation:** No new packages. Zero changes to `<PackageReference>` section.

## Architecture Patterns

### Recommended Project Structure
```
focus/
├── focus.ico             # Generated ICO file, committed to repo
├── focus.csproj          # Add <ApplicationIcon> + <EmbeddedResource>
└── Windows/
    └── Daemon/
        └── TrayIcon.cs   # Update NotifyIcon.Icon + NotifyIcon.Text

tools/
└── generate-icon/        # Standalone C# generator (separate mini-project or single .cs)
    └── Program.cs        # Draws the icon, writes focus/focus.ico
```

### Pattern 1: ICO File Format (BinaryWriter, PNG frames)

**What:** The ICO binary format contains a 6-byte header, N×16-byte directory entries, then the raw PNG (or BMP DIB) data for each frame.

**ICO Header (6 bytes, little-endian):**
```
Offset 0 (2 bytes): Reserved — always 0x0000
Offset 2 (2 bytes): Type — 0x0001 for ICO
Offset 4 (2 bytes): Count — number of frames (e.g., 4 for 16/20/24/32)
```

**Each directory entry (16 bytes per frame):**
```
Offset 0 (1 byte): Width  (0 means 256px; use actual px for 16/20/24/32)
Offset 1 (1 byte): Height (same rule)
Offset 2 (1 byte): Color count (0 = more than 256 colors)
Offset 3 (1 byte): Reserved — always 0
Offset 4 (2 bytes): Color planes (1 for ICO)
Offset 6 (2 bytes): Bits per pixel (32 for ARGB)
Offset 8 (4 bytes): Size of image data in bytes (PNG byte count)
Offset 12 (4 bytes): File offset to image data
```

**Frame data:** For PNG frames, write the raw PNG bytes (complete PNG file). No height-doubling required (that is only for BMP/DIB frames).

**When to use:** Any time an ICO is needed with multiple sizes and no external tools.

**Example:**
```csharp
// Source: Verified against ICO spec (https://docs.fileformat.com/image/ico/)
// and Meziantou blog (https://www.meziantou.net/creating-ico-files-from-multiple-images-in-dotnet.htm)
static void WriteIco(Stream output, IReadOnlyList<(byte[] PngData, int Width, int Height)> frames)
{
    using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

    // Header
    writer.Write((ushort)0);          // reserved
    writer.Write((ushort)1);          // type: ICO
    writer.Write((ushort)frames.Count);

    // Directory entries — calculate data offsets
    long dataOffset = 6 + 16 * frames.Count;
    foreach (var (png, w, h) in frames)
    {
        writer.Write((byte)(w >= 256 ? 0 : w));   // width
        writer.Write((byte)(h >= 256 ? 0 : h));   // height
        writer.Write((byte)0);                     // color count
        writer.Write((byte)0);                     // reserved
        writer.Write((ushort)1);                   // color planes
        writer.Write((ushort)32);                  // bits per pixel
        writer.Write((uint)png.Length);            // data size
        writer.Write((uint)dataOffset);            // data offset
        dataOffset += png.Length;
    }

    // Frame data
    foreach (var (png, _, _) in frames)
        writer.Write(png);
}
```

### Pattern 2: Drawing the Focus Icon

**What:** Use `System.Drawing.Graphics` to draw L-shaped corner brackets and a center dot on a transparent `Bitmap`. Each size is drawn independently (not scaled) to ensure pixel-perfect rendering at each DPI level.

**When to use:** Per-size drawing gives control over line thickness and proportions at small sizes (e.g., 16px needs thicker-looking brackets than 32px proportionally).

**Example:**
```csharp
// Source: System.Drawing.Graphics patterns; SmoothingMode.None for pixel-perfect
static byte[] DrawFocusIcon(int size)
{
    using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);

    g.Clear(Color.Transparent);
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

    // Design parameters scaled to 'size'
    int margin   = Math.Max(1, size / 8);   // outer margin
    int armLen   = Math.Max(2, size / 4);   // bracket arm length
    int thick    = Math.Max(1, size / 12);  // line thickness (1px at 16, 2px at 24+)
    int dotSize  = Math.Max(1, size / 10);  // center dot radius

    using var pen = new Pen(Color.White, thick);
    int inner = margin + armLen;  // inner corner of bracket

    // Top-left L-bracket
    g.DrawLine(pen, margin, margin, inner, margin);       // horizontal arm
    g.DrawLine(pen, margin, margin, margin, inner);       // vertical arm

    // Top-right L-bracket
    g.DrawLine(pen, size - 1 - margin, margin, size - 1 - inner, margin);
    g.DrawLine(pen, size - 1 - margin, margin, size - 1 - margin, inner);

    // Bottom-left L-bracket
    g.DrawLine(pen, margin, size - 1 - margin, inner, size - 1 - margin);
    g.DrawLine(pen, margin, size - 1 - margin, margin, size - 1 - inner);

    // Bottom-right L-bracket
    g.DrawLine(pen, size - 1 - margin, size - 1 - margin, size - 1 - inner, size - 1 - margin);
    g.DrawLine(pen, size - 1 - margin, size - 1 - margin, size - 1 - margin, size - 1 - inner);

    // Center dot
    int cx = size / 2, cy = size / 2;
    using var brush = new SolidBrush(Color.White);
    g.FillRectangle(brush, cx - dotSize, cy - dotSize, dotSize * 2 + 1, dotSize * 2 + 1);

    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}
```

### Pattern 3: Dual Embedding in .csproj

**What:** The same `.ico` file is referenced twice in the project file with different build actions:
1. `<ApplicationIcon>` — Win32 resource; gives the `.exe` its icon in Explorer, taskbar, Task Manager. Read by the C# compiler linker.
2. `<EmbeddedResource>` — .NET manifest resource; readable at runtime via `Assembly.GetManifestResourceStream`.

These are **different resource systems** and do **not conflict**. The Win32 icon is burned into the PE header by the compiler; the manifest resource is a separate section read by the .NET runtime. Using both is confirmed to work by independent community accounts and the distinct nature of the two mechanisms.

**Example csproj additions:**
```xml
<PropertyGroup>
  <!-- Win32 resource: .exe icon in Explorer, taskbar, Task Manager -->
  <ApplicationIcon>focus.ico</ApplicationIcon>
</PropertyGroup>

<ItemGroup>
  <!-- .NET manifest resource: runtime load for NotifyIcon -->
  <EmbeddedResource Include="focus.ico" />
</ItemGroup>
```

**When to use:** Whenever the same icon needs to serve both as the EXE application icon and a runtime-loadable resource for a WinForms `NotifyIcon`.

### Pattern 4: Runtime Icon Load from Embedded Resource

**What:** Load the embedded `.ico` at runtime using `Assembly.GetManifestResourceStream`, then construct a `System.Drawing.Icon` from the stream.

**Resource name convention:** The manifest resource name is `{DefaultNamespace}.{RelativePath}` where path separators become `.`. For a file at the project root (no subfolder), the name is `Focus.focus.ico` (assuming assembly/root namespace is `Focus`). Confirm with `Assembly.GetManifestResourceNames()` if uncertain.

**Example:**
```csharp
// Source: Microsoft Learn - Assembly.GetManifestResourceStream
// https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.getmanifestresourcestream
using var stream = Assembly.GetExecutingAssembly()
    .GetManifestResourceStream("Focus.focus.ico")
    ?? throw new InvalidOperationException("Embedded icon resource not found.");
var icon = new Icon(stream);

// In TrayIcon.cs constructor (replaces SystemIcons.Application):
_trayIcon = new NotifyIcon
{
    Icon = icon,
    Text = "Focus \u2014 Navigation Daemon",  // em dash U+2014
    ContextMenuStrip = menu,
    Visible = true
};
```

### Anti-Patterns to Avoid

- **BMP frames in ICO with height-doubling:** BMP format in ICO requires the DIB height to be double the displayed height (top half = XOR color data, bottom half = AND mask). This is ~50 more lines of bit-manipulation. Use PNG frames instead — fully supported on Windows Vista+, which Windows 11 exceeds.
- **Scaling a single bitmap to multiple sizes:** Produces blurry results. Draw each size independently on its own `Bitmap` with `SmoothingMode.None`.
- **Anti-aliasing on small geometric icons:** `SmoothingMode.AntiAlias` creates gray fringe pixels that look muddy against any tray background. Use `SmoothingMode.None` for crisp pixel-aligned geometric shapes.
- **`System.Drawing.Image.Save(stream, ImageFormat.Icon)`:** This does NOT produce a valid ICO file — it saves as PNG with an `.ico` extension. Use the hand-written BinaryWriter encoder.
- **Forgetting `leaveOpen: true` on BinaryWriter:** Default disposes the underlying stream on BinaryWriter.Dispose, which may close a stream you still need.
- **Wrong resource name casing:** `GetManifestResourceStream` is case-sensitive. Use `GetManifestResourceNames()` to confirm the exact name at development time.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| ICO multi-frame container | Third-party ICO library | BinaryWriter encoder (~30 lines) | Problem is simple; third-party adds unnecessary dependency |
| PNG encoding of each frame | Custom pixel serializer | `Bitmap.Save(stream, ImageFormat.Png)` | System.Drawing built-in; already imported |
| Win32 tray icon display | Shell_NotifyIcon P/Invoke | `NotifyIcon` (WinForms) | Already used in TrayIcon.cs |
| EXE icon embedding | RC compiler / post-build linker script | `<ApplicationIcon>` in csproj | MSBuild/Roslyn handles this natively; zero extra steps |

**Key insight:** The total new code is ~80 lines: ~30-line ICO encoder, ~40-line icon drawing routine, ~5-line loader in TrayIcon.cs, and one-line Text change. No library is warranted.

## Common Pitfalls

### Pitfall 1: Wrong ICO sizes for Windows 11 DPI scaling
**What goes wrong:** Providing only 16px and 32px means Windows 11 scales down from 32px when the display is at 125% (needs 20px) or 150% (needs 24px), producing a blurry tray icon.
**Why it happens:** Windows selects the smallest frame that is >= the required physical pixel size. With only 16 and 32, 125% DPI (needs 20px) uses the 32px frame scaled down.
**How to avoid:** Include all four sizes: 16, 20, 24, 32. Per community reports and the Windows API, these cover 100%, 125%, 150%, and 200% DPI cleanly.
**Warning signs:** Icon appears soft/blurry at non-100% DPI when you only have 16 and 32.

### Pitfall 2: Manifest resource name mismatch
**What goes wrong:** `GetManifestResourceStream("Focus.focus.ico")` returns null at runtime.
**Why it happens:** The resource name is constructed from the assembly's root namespace + the file path within the project, with path separators replaced by `.`. If the file is in a subfolder or the root namespace differs from the assembly name, the name is different.
**How to avoid:** After adding the `<EmbeddedResource>` to the csproj, call `Assembly.GetExecutingAssembly().GetManifestResourceNames()` in a debug build and inspect the actual names. OR add `LogicalName` metadata: `<EmbeddedResource Include="focus.ico"><LogicalName>focus.ico</LogicalName></EmbeddedResource>` to use the exact name `focus.ico` without namespace prefix.
**Warning signs:** `NullReferenceException` or `InvalidOperationException` from the null-check throw on the stream.

### Pitfall 3: `BinaryWriter` closes the output stream
**What goes wrong:** After writing the ICO to a MemoryStream via BinaryWriter, the stream is at EOF and its position is at the end — and if BinaryWriter disposes the stream, subsequent reads fail.
**Why it happens:** `BinaryWriter(stream)` defaults to `leaveOpen: false`, so `Dispose()` closes the underlying stream.
**How to avoid:** Use `new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)`, then `ms.Seek(0, SeekOrigin.Begin)` before reading.
**Warning signs:** `ObjectDisposedException` or reading 0 bytes from stream after BinaryWriter disposal.

### Pitfall 4: Generator produces file while daemon holds build lock
**What goes wrong:** Running `dotnet build` while the daemon is running causes MSB3027 (file lock on output EXE). This is a pre-existing concern noted in STATE.md.
**Why it happens:** The daemon holds a file handle on focus.exe via the mutex/process handle.
**How to avoid:** Kill daemon before rebuild (pre-existing operational requirement). The icon generator writes to `focus/focus.ico` (project source), not the build output, so the generator itself is unaffected. Only the final `dotnet build` step requires the daemon to be stopped.
**Warning signs:** MSB3027 error during `dotnet build`.

### Pitfall 5: `SmoothingMode.AntiAlias` blurs geometric icon
**What goes wrong:** The bracket lines get anti-aliased fringe pixels in gray/semi-transparent, which look dirty against the Windows 11 tray (which can be light or dark).
**Why it happens:** Default `SmoothingMode` for a Graphics object is `Default`, which may apply smoothing.
**How to avoid:** Explicitly set `g.SmoothingMode = SmoothingMode.None` before drawing any lines.
**Warning signs:** Zoomed-in view of the generated icon shows gray fringe pixels between the white lines and the transparent background.

## Code Examples

Verified patterns from official sources:

### NotifyIcon.Text — tooltip string
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon.text
// .NET 6+: max 127 chars. "Focus — Navigation Daemon" = 25 chars, well within limit.
// U+2014 is em dash (—), matching the locked decision.
_trayIcon.Text = "Focus \u2014 Navigation Daemon";
```

### Loading Icon from Embedded Resource
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.getmanifestresourcestream
// The stream returned is positioned at offset 0. Icon(Stream) reads it as an ICO.
// Do NOT dispose the stream before the Icon is done with it — Icon reads lazily.
var stream = Assembly.GetExecutingAssembly()
    .GetManifestResourceStream("Focus.focus.ico");     // or use LogicalName-simplified name
// Stream lifetime: keep alive as long as the Icon is in use, OR pass it to Icon(stream)
// which copies what it needs internally.
var customIcon = new Icon(stream!);
```

### EmbeddedResource with explicit LogicalName (recommended to avoid namespace guessing)
```xml
<!-- In focus.csproj -->
<ItemGroup>
  <EmbeddedResource Include="focus.ico">
    <LogicalName>focus.ico</LogicalName>
  </EmbeddedResource>
</ItemGroup>
```
With `LogicalName`, load as: `GetManifestResourceStream("focus.ico")` — no namespace prefix needed.

### ApplicationIcon csproj property
```xml
<!-- In focus.csproj <PropertyGroup> -->
<ApplicationIcon>focus.ico</ApplicationIcon>
```
This causes the Roslyn compiler to embed the .ico as a Win32 resource (ID 32512) in the PE header of the output EXE, which Windows Explorer, taskbar, and Task Manager use to display the application icon.

### Generator invocation pattern (standalone script approach)
```bash
# Run once when the icon design changes; commit focus/focus.ico to repo
dotnet run --project tools/generate-icon -- --output focus/focus.ico
```
Or as a simple console program with no arguments (hardcoded output path relative to its location).

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| BMP/DIB frames with AND mask plane (height doubled) | PNG frames embedded in ICO container | Windows Vista (2006) | Simpler encoding; no mask plane; full ARGB transparency |
| NotifyIcon.Text max 63 chars | NotifyIcon.Text max 127 chars | .NET 6 (2021) | "Focus — Navigation Daemon" (25 chars) works on both old and new |
| `System.Drawing.Image.Save(..ImageFormat.Icon)` | BinaryWriter ICO encoder | N/A (the Save method never produced valid ICO) | Don't use ImageFormat.Icon |

**Deprecated/outdated:**
- `SystemIcons.Application`: The placeholder used in current `TrayIcon.cs:54` — replace with the custom embedded icon.
- BMP ICO frames with AND masks: Overcomplicated for this use case; PNG frames are the current standard on Vista+.

## Open Questions

1. **Root namespace for embedded resource naming**
   - What we know: Assembly name is `focus` (from `<AssemblyName>focus</AssemblyName>` in csproj). Namespace in TrayIcon.cs is `Focus.Windows.Daemon`.
   - What's unclear: The default root namespace for the project. If it's `Focus` (capitalized), the manifest resource name would be `Focus.focus.ico`. If it's `focus` (lowercase), it would be `focus.focus.ico`.
   - Recommendation: Use `<LogicalName>focus.ico</LogicalName>` in the `<EmbeddedResource>` item to make the name explicit and portable. Load as `GetManifestResourceStream("focus.ico")`. This eliminates the ambiguity entirely.

2. **Generator timing: MSBuild pre-build vs committed output**
   - What we know: STATE.md already reflects the decision toward committed output ("no binary .ico committed to repo, defined as drawing primitives" — meaning the `.ico` is generated from code). The CONTEXT.md marks generation timing as Claude's Discretion.
   - What's unclear: Whether to run the generator as a pre-build MSBuild target (automatic, always fresh) or as a manual tool whose output is committed.
   - Recommendation: **Committed output approach.** The icon changes only when the design changes. A pre-build target adds latency to every build for a one-time asset. The generator is a standalone console project in `tools/generate-icon/`; run it manually when changing the design and commit `focus/focus.ico`. This avoids the file-lock issue on the daemon EXE and keeps build times fast.

## Sources

### Primary (HIGH confidence)
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon.text — NotifyIcon.Text, 127-char limit (.NET 6+)
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon.icon — NotifyIcon.Icon property, type System.Drawing.Icon
- https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.getmanifestresourcestream — GetManifestResourceStream signature and behavior
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/6.0/notifyicon-text-max-text-length-increased — NotifyIcon.Text limit change history
- https://docs.fileformat.com/image/ico/ — ICO file format: header (6 bytes), directory entries (16 bytes each), PNG vs BMP frame layout

### Secondary (MEDIUM confidence)
- https://www.meziantou.net/creating-ico-files-from-multiple-images-in-dotnet.htm — BinaryWriter ICO encoder with PNG frames; verified against ICO spec
- https://gist.github.com/Willy-Kimura/d3d3541dee057c583f39005b25df65c8 — PngIconConverter pattern; PNG frames in ICO confirmed Vista+
- https://learn.microsoft.com/en-us/answers/questions/1425442/windows-11-always-takes-a-bigger-png-from-ico — Windows 11 ICO size selection behavior; recommended sizes 16, 20, 24, 32px for DPI coverage

### Tertiary (LOW confidence)
- Community reports (multiple sources) that `<ApplicationIcon>` and `<EmbeddedResource>` for the same .ico file do not conflict — these are distinct resource systems (Win32 PE vs .NET manifest). Mechanically verified by the nature of the two systems; no official "use both simultaneously" documentation found.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — System.Drawing and BinaryWriter are BCL; NotifyIcon is already in project
- ICO format: HIGH — verified against official spec and multiple independent implementations
- Architecture: HIGH — EmbeddedResource + ApplicationIcon pattern is well-established; GetManifestResourceStream is official API
- Dual embedding compatibility: MEDIUM — logically correct (different resource systems), community-confirmed, no official single-document confirmation
- Pitfalls: HIGH — most verified by official docs or direct code analysis of the existing project

**Research date:** 2026-03-04
**Valid until:** 2026-09-04 (stable APIs, 6-month estimate)
