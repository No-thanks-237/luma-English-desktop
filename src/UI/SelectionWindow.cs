using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Luma.Core;
using Luma.Services;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;

namespace Luma.UI;

internal sealed class SelectionWindow : Window
{
    private readonly System.Drawing.Rectangle bounds;
    private readonly Bitmap snapshot;
    private readonly Canvas canvas = new();
    private readonly Path shade = new() { Fill = Theme.B("#90151E38"), IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Rectangle frame = new() { Stroke = Theme.B("#D3E1FF"), StrokeThickness = 2, RadiusX = 4, RadiusY = 4, IsHitTestVisible = false };
    private readonly Border hint;
    private Native.POINT start;
    private bool selecting;
    private bool finished;
    public event Action<Bitmap?>? Selected;
    public SelectionWindow()
    {
        bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        if ((long)bounds.Width * bounds.Height > 65000000) throw new InvalidOperationException("屏幕总分辨率过大，请减少显示器后重试。");
        snapshot = CaptureService.Screenshot(bounds);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true; Topmost = true; Title = "Luma · 框选翻译"; Cursor = Cursors.Cross; Background = System.Windows.Media.Brushes.Black;
        var image = new System.Windows.Controls.Image { Source = ImageSource(snapshot), Stretch = Stretch.Fill, IsHitTestVisible = false };
        var root = new Grid(); root.Children.Add(image); root.Children.Add(canvas); Content = root;
        canvas.Children.Add(shade); canvas.Children.Add(frame); frame.Visibility = Visibility.Collapsed;
        hint = Theme.Card(Theme.T("拖动框选英文  ·  松开确认  ·  Esc 取消", 13, System.Windows.Media.Brushes.White, true), new Thickness(22, 12, 22, 12), Theme.B("#E72D3B5F")); canvas.Children.Add(hint);
        SourceInitialized += (_, _) => Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0040);
        Loaded += (_, _) => { UpdateShade(new Rect()); Canvas.SetLeft(hint, Math.Max(20, (ActualWidth - 360) / 2)); Canvas.SetTop(hint, 35); Activate(); Focus(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Finish(null); e.Handled = true; } };
        MouseLeftButtonDown += (_, e) => { start = EventPoint(e); selecting = true; CaptureMouse(); e.Handled = true; };
        MouseMove += (_, e) => { if (!selecting) return; var end = EventPoint(e); var rect = UiRect(start, end); frame.Visibility = Visibility.Visible; Canvas.SetLeft(frame, rect.Left); Canvas.SetTop(frame, rect.Top); frame.Width = rect.Width; frame.Height = rect.Height; UpdateShade(rect); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!selecting) return; selecting = false; var end = EventPoint(e); ReleaseMouseCapture();
            var selected = System.Drawing.Rectangle.Intersect(bounds, System.Drawing.Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));
            if (selected.Width < 8 || selected.Height < 8) { Finish(null); return; }
            var local = new System.Drawing.Rectangle(selected.X - bounds.X, selected.Y - bounds.Y, selected.Width, selected.Height);
            Finish(snapshot.Clone(local, System.Drawing.Imaging.PixelFormat.Format24bppRgb)); e.Handled = true;
        };
        Closed += (_, _) => { snapshot.Dispose(); if (!finished) { finished = true; Selected?.Invoke(null); } };
    }
    private Native.POINT EventPoint(MouseEventArgs e)
    {
        var p = PointToScreen(e.GetPosition(this)); return new Native.POINT { X = (int)Math.Round(p.X), Y = (int)Math.Round(p.Y) };
    }
    private Rect UiRect(Native.POINT a, Native.POINT b)
    {
        var t = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var p1 = t.Transform(new Point(a.X - bounds.X, a.Y - bounds.Y)); var p2 = t.Transform(new Point(b.X - bounds.X, b.Y - bounds.Y)); return new Rect(p1, p2);
    }
    private void UpdateShade(Rect selected)
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd }; group.Children.Add(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight))); if (!selected.IsEmpty) group.Children.Add(new RectangleGeometry(selected)); shade.Data = group;
    }
    private void Finish(Bitmap? bitmap) { if (finished) { bitmap?.Dispose(); return; } finished = true; Close(); Selected?.Invoke(bitmap); }
    public static BitmapSource ImageSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); stream.Position = 0;
        var source = new BitmapImage(); source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.StreamSource = stream; source.EndInit(); source.Freeze(); return source;
    }
}
