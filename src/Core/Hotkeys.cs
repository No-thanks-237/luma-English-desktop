using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;

namespace Luma.Core;

internal sealed class Hotkeys : IDisposable
{
    private readonly HwndSource source;
    private List<string> current = new();
    public event Action<int>? Pressed;
    public void ListenEscape(bool enabled)
    {
        Native.UnregisterHotKey(source.Handle, 104);
        if (enabled) Native.RegisterHotKey(source.Handle, 104, 0x4000, 0x1B);
    }
    public Hotkeys()
    {
        source = new HwndSource(new HwndSourceParameters("Luma.Hotkeys") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) });
        source.AddHook(Hook);
    }
    public static (uint mods, uint key) Parse(string value)
    {
        var tokens = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint mods = 0x4000;
        Key key = Key.None;
        foreach (var token in tokens)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= 2; break;
                case "alt": mods |= 1; break;
                case "shift": mods |= 4; break;
                case "win": mods |= 8; break;
                default:
                    if (key != Key.None) throw new InvalidOperationException("快捷键只应包含一个主键。");
                    try { key = (Key)new KeyConverter().ConvertFromString(token)!; }
                    catch { throw new InvalidOperationException("无法识别快捷键：" + value + "。示例：Ctrl+Alt+T"); }
                    break;
            }
        }
        if ((mods & 15) == 0 || key == Key.None || KeyInterop.VirtualKeyFromKey(key) == 0 || key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)
            throw new InvalidOperationException("快捷键需包含 Ctrl / Alt / Shift / Win 和一个主键。");
        return (mods, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
    public void Register(Settings settings)
    {
        var values = new List<string> { settings.CaptureHotkey, settings.LookupHotkey, settings.PauseHotkey };
        var parsed = values.Select(Parse).ToList();
        if (parsed.Distinct().Count() != 3) throw new InvalidOperationException("三个快捷键需要各不相同。");
        var old = current.ToList();
        for (var i = 0; i < 3; i++) Native.UnregisterHotKey(source.Handle, 101 + i);
        try
        {
            for (var i = 0; i < 3; i++)
                if (!Native.RegisterHotKey(source.Handle, 101 + i, parsed[i].mods, parsed[i].key)) throw new InvalidOperationException("快捷键 " + values[i] + " 已被其他软件占用，请更换。");
            current = values;
        }
        catch
        {
            for (var i = 0; i < 3; i++) Native.UnregisterHotKey(source.Handle, 101 + i);
            for (var i = 0; i < old.Count; i++) { var k = Parse(old[i]); Native.RegisterHotKey(source.Handle, 101 + i, k.mods, k.key); }
            throw;
        }
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312) { Pressed?.Invoke(wParam.ToInt32() - 101); handled = true; }
        return IntPtr.Zero;
    }
    public void Dispose() { for (var i = 0; i < 4; i++) Native.UnregisterHotKey(source.Handle, 101 + i); source.Dispose(); }
}
