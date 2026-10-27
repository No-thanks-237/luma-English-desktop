using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Luma.Core;
using Tesseract;

namespace Luma.Services;

public sealed class CaptureService : IDisposable
{
    private readonly BlockingCollection<(Native.POINT, TaskCompletionSource<CaptureResult?>)> automationQueue = new(1);
    private readonly SemaphoreSlim ocrGate = new(1, 1);
    private TesseractEngine? engine;
    private bool disposed;
    internal string? DiagnosticFolder { get; set; }
    public CaptureService()
    {
        var worker = new Thread(() =>
        {
            foreach (var (point, completion) in automationQueue.GetConsumingEnumerable())
            {
                try { completion.TrySetResult(AutomationWord(point)); }
                catch { completion.TrySetResult(null); }
            }
        })
        { IsBackground = true, Name = "Luma UI Automation" };
        worker.SetApartmentState(ApartmentState.MTA); worker.Start();
    }
    private static CaptureResult? AutomationWord(Native.POINT p)
    {
        var point = new System.Windows.Point(p.X, p.Y);
        var element = AutomationElement.FromPoint(point);
        for (var depth = 0; element != null && depth < 6; depth++, element = TreeWalker.ControlViewWalker.GetParent(element))
        {
            if (element.Current.IsPassword) return null;
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) continue;
            var range = ((TextPattern)pattern).RangeFromPoint(point);
            range.ExpandToEnclosingUnit(TextUnit.Word);
            var text = DictionaryService.Normalize(range.GetText(128));
            if (!DictionaryService.IsWord(text)) continue;
            foreach (var rect in range.GetBoundingRectangles())
            {
                // RangeFromPoint returns the nearest text, including on whitespace.
                if (rect.Contains(point)) return new CaptureResult(text, "文字直取", new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));
            }
        }
        return null;
    }
    internal async Task<CaptureResult?> WordAt(Native.POINT point, bool fallback, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<CaptureResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (automationQueue.TryAdd((point, completion)))
        {
            var winner = await Task.WhenAny(completion.Task, Task.Delay(300, ct));
            if (winner == completion.Task && await completion.Task is { } found) return found;
        }
        ct.ThrowIfCancellationRequested();
        if (!fallback) return null;
        var work = System.Windows.Forms.Screen.FromPoint(new Point(point.X, point.Y)).Bounds;
        var dpi = Math.Clamp(Native.GetDpiForWindow(Native.WindowFromPoint(point)) / 96f, 1, 3);
        var halfWidth = (int)(320 * dpi); var halfHeight = (int)(85 * dpi);
        var region = Rectangle.Intersect(work, new Rectangle(point.X - halfWidth, point.Y - halfHeight, halfWidth * 2, halfHeight * 2));
        using var bitmap = Screenshot(region);
        if (DiagnosticFolder != null) { Directory.CreateDirectory(DiagnosticFolder); bitmap.Save(Path.Combine(DiagnosticFolder, "crop.png"), System.Drawing.Imaging.ImageFormat.Png); File.WriteAllText(Path.Combine(DiagnosticFolder, "ocr-trace.txt"), $"Point {point.X},{point.Y}; region {region}\n"); }
        return await Task.Run(async () =>
        {
            await ocrGate.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                using var pix = ToPix(bitmap, out var scale);
                using var page = Engine.Process(pix, PageSegMode.SparseText);
                using var it = page.GetIterator();
                it.Begin();
                do
                {
                    if (!it.TryGetBoundingBox(PageIteratorLevel.Word, out var box)) continue;
                    var r = new Rectangle(region.X + (int)(box.X1 / scale), region.Y + (int)(box.Y1 / scale), (int)Math.Ceiling(box.Width / scale), (int)Math.Ceiling(box.Height / scale));
                    r.Inflate((int)Math.Ceiling(2 * dpi), (int)Math.Ceiling(4 * dpi));
                    if (DiagnosticFolder != null) File.AppendAllText(Path.Combine(DiagnosticFolder, "ocr-trace.txt"), $"{it.GetText(PageIteratorLevel.Word)} | {r} | conf {it.GetConfidence(PageIteratorLevel.Word)}\n");
                    if (!r.Contains(point.X, point.Y) || it.GetConfidence(PageIteratorLevel.Word) < 10) continue;
                    var text = DictionaryService.Normalize(it.GetText(PageIteratorLevel.Word) ?? "");
                    if (DictionaryService.IsWord(text)) return new CaptureResult(text, "本地 OCR", r, it.GetConfidence(PageIteratorLevel.Word));
                } while (it.Next(PageIteratorLevel.Word));
                return null;
            }
            finally { ocrGate.Release(); }
        }, ct);
    }
    private TesseractEngine Engine => engine ??= new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "Assets", "tessdata"), "eng", EngineMode.LstmOnly);
    public async Task<string> Recognize(Bitmap bitmap, CancellationToken ct)
    {
        await ocrGate.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var pix = ToPix(bitmap, out _);
                using var page = Engine.Process(pix, PageSegMode.Auto);
                return page.GetText().Trim();
            }, ct);
        }
        finally { ocrGate.Release(); }
    }
    public static Bitmap Screenshot(Rectangle r)
    {
        var bitmap = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
        try { using var g = Graphics.FromImage(bitmap); g.CopyFromScreen(r.Location, Point.Empty, r.Size, CopyPixelOperation.SourceCopy); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }
    private static Pix ToPix(Bitmap original, out float scale)
    {
        scale = Math.Min(2f, 4500f / Math.Max(original.Width, original.Height));
        using var scaled = new Bitmap((int)(original.Width * scale), (int)(original.Height * scale), PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.Clear(Color.White); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // Invert dark UIs before recognition, preserving anti-aliased letter edges.
            long luma = 0; int count = 0;
            for (var y = 0; y < original.Height; y += Math.Max(1, original.Height / 12))
                for (var x = 0; x < original.Width; x += Math.Max(1, original.Width / 24)) { var c = original.GetPixel(x, y); luma += c.R + c.G + c.B; count += 3; }
            using var attr = new ImageAttributes();
            if (luma / Math.Max(count, 1) < 105) attr.SetColorMatrix(new ColorMatrix(new[] { new float[] { -1, 0, 0, 0, 0 }, new float[] { 0, -1, 0, 0, 0 }, new float[] { 0, 0, -1, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 1, 1, 1, 0, 1 } }));
            g.DrawImage(original, new Rectangle(0, 0, scaled.Width, scaled.Height), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attr);
        }
        using var stream = new MemoryStream(); scaled.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return Pix.LoadFromMemory(stream.ToArray());
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        automationQueue.CompleteAdding();
        // Do not dispose a native OCR handle while an in-flight request is using it.
        if (ocrGate.Wait(0)) { engine?.Dispose(); ocrGate.Release(); }
    }
}
