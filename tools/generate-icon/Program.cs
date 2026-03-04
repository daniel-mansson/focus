using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using System.Text;

[SupportedOSPlatform("windows")]
static class Program
{
    static void Main()
    {
        int[] sizes = { 16, 20, 24, 32 };

        var frames = sizes
            .Select(s => (PngData: DrawFocusIcon(s), Width: s, Height: s))
            .ToList();

        // Resolve output path: dotnet run is invoked from tools/generate-icon/
        // Environment.CurrentDirectory is the working directory at invocation time (tools/generate-icon/)
        // so ../../focus/focus.ico resolves to the repo-root focus/ directory.
        string outputPath = Path.GetFullPath(
            Path.Combine(Environment.CurrentDirectory, "..", "..", "focus", "focus.ico"));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var file = File.Open(outputPath, FileMode.Create, FileAccess.Write);
        WriteIco(file, frames);

        Console.WriteLine($"Generated focus.ico with {frames.Count} frames ({string.Join(", ", sizes.Select(s => $"{s}px"))})");
        Console.WriteLine($"Output: {outputPath}");
    }

    [SupportedOSPlatform("windows")]
    static byte[] DrawFocusIcon(int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);

        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

        // Design parameters scaled to 'size'
        int margin  = Math.Max(1, size / 8);   // outer margin
        int armLen  = Math.Max(2, size / 4);   // bracket arm length
        int thick   = Math.Max(1, size / 12);  // line thickness (1px at 16, 2px at 24+)
        int dotSize = Math.Max(1, size / 10);  // center dot half-width

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
        int cx = size / 2;
        int cy = size / 2;
        using var brush = new SolidBrush(Color.White);
        g.FillRectangle(brush, cx - dotSize, cy - dotSize, dotSize * 2 + 1, dotSize * 2 + 1);

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    static void WriteIco(Stream output, IReadOnlyList<(byte[] PngData, int Width, int Height)> frames)
    {
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

        // ICO header (6 bytes)
        writer.Write((ushort)0);              // reserved
        writer.Write((ushort)1);              // type: ICO
        writer.Write((ushort)frames.Count);  // number of frames

        // Directory entries (16 bytes per frame)
        long dataOffset = 6 + 16 * frames.Count;
        foreach (var (png, w, h) in frames)
        {
            writer.Write((byte)(w >= 256 ? 0 : w));   // width (0 means 256)
            writer.Write((byte)(h >= 256 ? 0 : h));   // height (0 means 256)
            writer.Write((byte)0);                     // color count (0 = more than 256)
            writer.Write((byte)0);                     // reserved
            writer.Write((ushort)1);                   // color planes
            writer.Write((ushort)32);                  // bits per pixel
            writer.Write((uint)png.Length);            // data size in bytes
            writer.Write((uint)dataOffset);            // file offset to image data
            dataOffset += png.Length;
        }

        // Frame data (raw PNG bytes)
        foreach (var (png, _, _) in frames)
            writer.Write(png);
    }
}
