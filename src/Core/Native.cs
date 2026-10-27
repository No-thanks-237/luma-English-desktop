using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Luma.Core;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    internal static int HitTest(Window window,Point position)
    {
        var p=window.PointToScreen(position);var packed=((long)(int)p.Y<<16)|((long)(int)p.X&0xffff);
        return (int)SendMessage(new WindowInteropHelper(window).Handle,0x84,IntPtr.Zero,new IntPtr(packed));
    }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);
    [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int State, Flags, Color, Animation; }
    [StructLayout(LayoutKind.Sequential)] private struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }
    public static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public static bool IsOwnWindow(POINT p)
    {
        GetWindowThreadProcessId(WindowFromPoint(p), out var pid);
        return pid == (uint)Environment.ProcessId;
    }
    public static void Glass(Window window, bool noActivate = false)
    {
        Luma.UI.Theme.Prepare(window);
        window.WindowStyle = WindowStyle.None;
        window.Background = Brushes.White;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(window.ResizeMode == ResizeMode.NoResize ? 0 : 6), GlassFrameThickness = new Thickness(-1), CornerRadius = new CornerRadius(24), UseAeroCaptionButtons = false });
        window.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(window).Handle;
            if (noActivate)
            {
                var old = GetWindowLongPtr(h, -20).ToInt64();
                SetWindowLongPtr(h, -20, new IntPtr(old | 0x08000000 | 0x80));
            }
            var source = HwndSource.FromHwnd(h);
            if (source?.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.White;
            var round = 2; DwmSetWindowAttribute(h, 33, ref round, 4);
            var backdrop = 3;
            if (DwmSetWindowAttribute(h, 38, ref backdrop, 4) != 0)
            {
                var policy = new AccentPolicy { State = 4, Flags = 2, Color = unchecked((int)0xC0EDF3F1) };
                var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
                try { Marshal.StructureToPtr(policy, ptr, false); var data = new CompositionData { Attribute = 19, Data = ptr, Size = Marshal.SizeOf<AccentPolicy>() }; SetWindowCompositionAttribute(h, ref data); }
                finally { Marshal.FreeHGlobal(ptr); }
            }
        };
    }
    public static void TransparentOverlay(Window window)
    {
        Luma.UI.Theme.Prepare(window); window.WindowStyle = WindowStyle.None; window.AllowsTransparency = true; window.Background = Brushes.Transparent;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle; var style = GetWindowLongPtr(handle, -20).ToInt64(); SetWindowLongPtr(handle, -20, new IntPtr(style | 0x08000000 | 0x80));
        };
    }
    public static void Circle(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        GetWindowRect(handle, out var bounds);
        var region = CreateEllipticRgn(0, 0, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        if (SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
    }
    public static void ClipToScreen(Window window,System.Drawing.Rectangle screen)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;GetWindowRect(handle,out var r);
        var bounds=System.Drawing.Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);var visible=System.Drawing.Rectangle.Intersect(bounds,screen);
        var region=visible.IsEmpty?CreateRectRgn(0,0,0,0):CreateRectRgn(visible.Left-r.Left,visible.Top-r.Top,visible.Right-r.Left,visible.Bottom-r.Top);
        if(SetWindowRgn(handle,region,true)==0)DeleteObject(region);
    }
    public static void ClearClip(Window window){var handle=new WindowInteropHelper(window).Handle;if(handle!=IntPtr.Zero)SetWindowRgn(handle,IntPtr.Zero,true);}
    public static void PlaceBeside(Window window, Window anchor)
    {
        var anchorHandle = new WindowInteropHelper(anchor).Handle;
        var handle = new WindowInteropHelper(window).Handle;
        GetWindowRect(anchorHandle, out var a); GetWindowRect(handle, out var r);
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(a.Left, a.Top)).WorkingArea;
        var width = r.Right - r.Left; var height = r.Bottom - r.Top;
        var x = a.Left - width - 15;
        if (x < area.Left + 12) x = a.Right + 15;
        x = Math.Clamp(x, area.Left + 12, Math.Max(area.Left + 12, area.Right - width - 12));
        var y = Math.Clamp(a.Top + (a.Bottom - a.Top - height) / 2, area.Top + 12, Math.Max(area.Top + 12, area.Bottom - height - 12));
        SetWindowPos(handle, new IntPtr(-1), x, y, 0, 0, 0x0015);
    }
    public static void PlaceNear(Window w, System.Drawing.Point p)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(p).WorkingArea;
        var h = new WindowInteropHelper(w).Handle;
        var m = PresentationSource.FromVisual(w)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var width = (int)Math.Ceiling(w.ActualWidth * m.M11); var height = (int)Math.Ceiling(w.ActualHeight * m.M22);
        var x = p.X + 24; var y = p.Y + 26;
        if (x + width > screen.Right - 12) x = p.X - width - 20;
        if (y + height > screen.Bottom - 12) y = screen.Bottom - height - 12;
        x = Math.Max(screen.Left + 12, Math.Min(x, screen.Right - width - 12));
        y = Math.Max(screen.Top + 12, y);
        SetWindowPos(h, new IntPtr(-1), x, y, 0, 0, 0x0015);
    }
    public static bool ContainsPointer(Window w, POINT p, int padding = 0)
    {
        if (!w.IsVisible) return false;
        GetWindowRect(new WindowInteropHelper(w).Handle, out var r);
        return p.X >= r.Left - padding && p.X <= r.Right + padding && p.Y >= r.Top - padding && p.Y <= r.Bottom + padding;
    }
}
