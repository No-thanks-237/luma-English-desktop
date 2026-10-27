using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Luma.Core;
using Luma.Services;
using Luma.UI;

namespace Luma;

internal sealed class Controller : IDisposable
{
    public Store Store { get; }
    public DictionaryService Dictionary { get; }
    public AiService Ai { get; }
    public EtymologyService Etymologies { get; }
    private readonly CaptureService capture = new();
    private readonly Hotkeys hotkeys = new();
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly MainWindow main;
    private BubbleWindow? bubble;
    private bool bubbleExpanded;
    private WordWindow? word;
    private SelectionWindow? selection;
    private SpeechSynthesizer? speaker;
    private Native.POINT lastPoint;
    private DateTime settled = DateTime.UtcNow;
    private DateTime keepUntil = DateTime.MinValue;
    private bool attempted;
    private bool reading;
    private bool explicitReading;
    private CancellationTokenSource? wordRequest;
    private CancellationTokenSource? ocrRequest;
    private bool quitting;
    private IntPtr captureForeground;
    private bool preparingCapture;
    public Controller(bool quiet, string? dataRoot = null)
    {
        Store = new Store(dataRoot); Theme.MotionEnabled = !Store.Settings.ReduceMotion && SystemParameters.ClientAreaAnimation; Dictionary = new DictionaryService(Store); Ai = new AiService(Store); Etymologies = new EtymologyService(Store); main = new MainWindow(this); Application.Current.MainWindow = main;
        tray = new System.Windows.Forms.NotifyIcon { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "luma.ico")), Text = "Luma 拾词 · Alt 指向取词", Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开 Luma 拾词", null, (_, _) => ShowMain()); menu.Items.Add("框选翻译", null, (_, _) => BeginCapture());
        menu.Items.Add("暂停 / 恢复取词", null, (_, _) => ToggleEnabled()); menu.Items.Add("偏好设置", null, (_, _) => ShowMain("settings")); menu.Items.Add(new System.Windows.Forms.ToolStripSeparator()); menu.Items.Add("退出 Luma", null, (_, _) => Quit()); tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => ShowMain();
        try { hotkeys.Register(Store.Settings); } catch (Exception ex) { main.SetNotice(ex.Message); quiet = false; }
        hotkeys.Pressed += id =>
        {
            switch (id) { case 0: BeginCapture(); break; case 1: if (selection == null && Store.Settings.Enabled) { Native.GetCursorPos(out var p); _ = LookupAt(p, true); } break; case 2: ToggleEnabled(); break; case 3: word?.Close();bubble?.CloseMenu(); break; }
        };
        poll.Tick += (_, _) => Tick(); poll.Start(); RefreshBubble(); main.UpdateStatus();
        if (Store.LoadWarning != null) main.SetNotice(Store.LoadWarning);
        if (!quiet) main.Show();
    }
    public void ShowMain(string? target = null)
    {
        if (quitting) return;
        if (target != null) main.Navigate(target);
        main.Show(); if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal; main.Activate();
    }
    public void ToggleEnabled()
    {
        Store.Settings.Enabled = !Store.Settings.Enabled; Store.SaveSettings(); main.UpdateStatus();
        tray.Text = Store.Settings.Enabled ? "Luma 拾词 · 取词已开启" : "Luma 拾词 · 取词已暂停";
        bubble?.RefreshState();
        if (!Store.Settings.Enabled) { wordRequest?.Cancel(); if (word is { Pinned: false }) word.Close(); }
    }
    public void ApplySettings(Settings settings)
    {
        if (word is { Busy: true }) throw new InvalidOperationException("请等待词卡的信息查询完成，再保存设置。");
        // Register first: invalid or occupied shortcuts must not replace working settings.
        hotkeys.Register(settings);
        try
        {
            if (settings.StartAtLogin != Store.Settings.StartAtLogin)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true) ?? throw new InvalidOperationException("无法打开 Windows 登录启动配置。");
                if (settings.StartAtLogin) key.SetValue("Luma", '"' + Environment.ProcessPath + '"' + " --quiet"); else key.DeleteValue("Luma", false);
            }
            Store.ReplaceSettings(settings);
            Theme.MotionEnabled = !settings.ReduceMotion && SystemParameters.ClientAreaAnimation;
        }
        catch { hotkeys.Register(Store.Settings); throw; }
        wordRequest?.Cancel(); if (word is { Busy: false }) word.Close(); attempted = false; RefreshBubble(); main.UpdateStatus();
    }
    private void RefreshBubble()
    {
        if (Store.Settings.ShowBubble) { if (bubble == null) { bubble = new BubbleWindow(this); bubble.Show(); } bubble.RefreshState(); }
        else { bubble?.Close(); bubble = null; }
    }
    private void Tick()
    {
        if (quitting || selection != null) return;
        Native.GetCursorPos(out var p); var now = DateTime.UtcNow;
        var moved = Math.Abs(p.X - lastPoint.X) + Math.Abs(p.Y - lastPoint.Y) > 5;
        if (moved) { lastPoint = p; settled = now; attempted = false; wordRequest?.Cancel(); }
        var alt = Native.Down(0x12) && !Native.Down(0x11) && !Native.Down(0x10);
        if (word != null)
        {
            if (Native.Down(0x1B)) { word.Close(); attempted = true; return; }
            if (Native.ContainsPointer(word, p, 12) || alt) keepUntil = now.AddMilliseconds(1000);
            else if (!word.Pinned && now > keepUntil) { word.Close(); }
            if (word is { Pinned: true } || word != null && Native.ContainsPointer(word, p, 12)) return;
        }
        if (!Store.Settings.Enabled || Store.Settings.Trigger == "Hotkey") return;
        var active = Store.Settings.Trigger == "Hover" || alt;
        if (!active) { if (!explicitReading) wordRequest?.Cancel(); attempted = false; settled = now; return; }
        if (Native.Down(0x01) || Native.Down(0x02) || Native.IsOwnWindow(p)) return;
        if (attempted || reading || (now - settled).TotalMilliseconds < Store.Settings.DwellMs) return;
        attempted = true; _ = LookupAt(p, false);
    }
    private async Task LookupAt(Native.POINT p, bool explicitRequest)
    {
        if (reading || selection != null) return;
        if (word is { Pinned: true }) { if (explicitRequest) main.SetNotice("当前查词卡已固定，请先关闭或取消固定。"); return; }
        word?.Close(); reading = true; explicitReading = explicitRequest; attempted = true; wordRequest = new CancellationTokenSource(); var ct = wordRequest.Token;
        try
        {
            var result = await capture.WordAt(p, Store.Settings.OcrFallback, ct); ct.ThrowIfCancellationRequested();
            if (result == null) { if (explicitRequest) { main.SetNotice("没有识别到鼠标指向的英文单词。可尝试框选，或手动输入。"); ShowMain(); } return; }
            var entry = Dictionary.Lookup(result.Text); ct.ThrowIfCancellationRequested();
            if (!entry.Found && result.Source == "本地 OCR") entry = Dictionary.CorrectOcr(result.Text) ?? entry;
            if (result.Confidence < 45 && !entry.Found) { if (explicitRequest) { main.SetNotice("识别结果置信度较低，且未在词典中找到。请放大文字后重试，或框选校正。"); ShowMain(); } return; }
            ShowWord(entry, result.Source, new System.Drawing.Point(p.X, p.Y), explicitRequest);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { main.SetNotice("取词暂不可用：" + ex.Message); if (explicitRequest) ShowMain(); }
        finally { wordRequest?.Dispose(); wordRequest = null; reading = false; explicitReading = false; }
    }
    public void ManualLookup(string text)
    {
        var normalized = DictionaryService.Normalize(text);
        if (!DictionaryService.IsWord(normalized)) { main.SetNotice("请输入一个英文单词，可含连字符或撇号。整句请使用句子翻译。"); return; }
        try { Native.GetCursorPos(out var p); ShowWord(Dictionary.Lookup(normalized), "手动查词", new System.Drawing.Point(p.X, p.Y), true); }
        catch (Exception ex) { main.SetNotice("词典读取失败：" + ex.Message); }
    }
    private void ShowWord(WordEntry entry, string source, System.Drawing.Point point, bool pin = false)
    {
        word?.Close(); word = new WordWindow(this, entry, source, point);
        if (pin) word.SetPinned();
        var placed = word; word.DpiChanged += (_, _) => placed.Dispatcher.BeginInvoke(() => { if (placed.IsVisible) { placed.FitToScreen(point); Native.PlaceNear(placed, point); } });
        word.SizeChanged += (_, _) => { if (placed.IsVisible) Native.PlaceNear(placed, point); };
        word.Show(); hotkeys.ListenEscape(true); word.UpdateLayout(); Native.PlaceNear(word, point); keepUntil = DateTime.UtcNow.AddMilliseconds(pin ? 3000 : 1200);
    }
    public void WordClosed(WordWindow closed) { if (word == closed) { word = null; hotkeys.ListenEscape(bubbleExpanded); } }
    internal void BubbleExpansionChanged(bool expanded){bubbleExpanded=expanded;hotkeys.ListenEscape(expanded||word is {IsVisible:true});}
    public void Copy(string text)
    {
        try { System.Windows.Clipboard.SetText(text); main.SetNotice("已复制到剪贴板。"); }
        catch { main.SetNotice("剪贴板正被其他软件占用，请稍后重试。"); }
    }
    public string? Speak(string text)
    {
        try
        {
            speaker ??= new SpeechSynthesizer();
            var hasEnglish = false;
            foreach (var voice in speaker.GetInstalledVoices()) if (voice.Enabled && voice.VoiceInfo.Culture.TwoLetterISOLanguageName == "en") { speaker.SelectVoice(voice.VoiceInfo.Name); hasEnglish = true; break; }
            if (!hasEnglish) throw new InvalidOperationException("未安装英语语音。");
            speaker.SpeakAsyncCancelAll(); speaker.Rate = -1; speaker.SpeakAsync(text);
            return null;
        }
        catch { const string error = "没有可用的英语语音，请在 Windows 中添加后重试。"; main.SetNotice(error); return error; }
    }
    public async void BeginCapture()
    {
        if (selection != null || preparingCapture) return;
        if (main.TranslationBusy) { main.SetNotice("请等待当前翻译完成或先取消，再开始新的框选。"); ShowMain("translate"); return; }
        if (ocrRequest != null) { main.SetNotice("正在识别上一段文字，请稍候。"); return; }
        captureForeground = Native.GetForegroundWindow();
        preparingCapture = true;
        var wasVisible = main.IsVisible;
        wordRequest?.Cancel(); word?.Close(); main.Hide(); bubble?.Hide();
        try
        {
            // Let our windows leave the compositor before freezing the screen.
            await Task.Delay(180);
            selection = new SelectionWindow(); selection.Selected += bitmap =>
            {
                selection = null; if (Store.Settings.ShowBubble) bubble?.Show();
                if (bitmap == null) { if (wasVisible) { main.ShowActivated = false; main.Show(); main.ShowActivated = true; } Native.SetForegroundWindow(captureForeground); return; }
                _ = ProcessSelection(bitmap);
            }; selection.Show(); selection.Activate();
        }
        catch (Exception ex) { selection = null; if (Store.Settings.ShowBubble) bubble?.Show(); ShowMain("translate"); main.SetNotice("无法框选：" + ex.Message); }
        finally { preparingCapture = false; }
    }
    private async Task ProcessSelection(Bitmap bitmap)
    {
        ShowMain("translate"); main.SetCapturedText("", "正在本地识别英文…"); ocrRequest = new CancellationTokenSource();
        try
        {
            var text = await capture.Recognize(bitmap, ocrRequest.Token);
            main.SetCapturedText(text, text.Length > 0 ? "本地 OCR 已完成。请检查原文，再点击翻译。" : "未识别到英文。可重新框选、放大文字，或直接输入。");
        }
        catch (Exception ex) { main.SetCapturedText("", "识别失败：" + AiService.Friendly(ex)); }
        finally { bitmap.Dispose(); ocrRequest.Dispose(); ocrRequest = null; }
    }
    public void Quit()
    {
        if (quitting) return; quitting = true; Store.SaveWords(); Dispose(); main.QuitClose(); Application.Current.Shutdown();
    }
    public void Dispose()
    {
        poll.Stop(); wordRequest?.Cancel(); ocrRequest?.Cancel(); word?.Close(); selection?.Close(); bubble?.Close(); hotkeys.Dispose(); tray.Visible = false; tray.Dispose(); speaker?.Dispose(); capture.Dispose();
    }
}
