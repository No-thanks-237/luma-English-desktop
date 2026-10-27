using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Luma.Core;
using Luma.Services;

namespace Luma.UI;

internal sealed class SettingsView : UserControl
{
    private readonly Controller controller;
    private readonly MainWindow owner;
    private readonly Settings draft;
    private readonly ContentControl body = new();
    private readonly Dictionary<string, FrameworkElement> pages = new();
    private readonly Dictionary<string, Button> tabs = new();
    private readonly TextBlock feedback = Theme.T("修改后点击保存，即刻生效。", 11, Theme.Muted);
    private readonly Button test;
    private readonly ComboBox trigger, provider, language;
    private readonly Slider dwell;
    private readonly TextBox url, model, captureKey, lookupKey, pauseKey;
    private readonly PasswordBox key;
    public SettingsView(Controller controller, MainWindow owner)
    {
        this.controller = controller; this.owner = owner; draft = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(controller.Store.Settings))!;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new StackPanel(); title.Children.Add(Theme.T("MAKE IT YOURS", 10, Theme.Accent, true)); Theme.Space(title, 8); title.Children.Add(Theme.T("刚好适合你的习惯。", 27, Theme.Ink, true)); Theme.Space(title, 6); title.Children.Add(Theme.T("少一点打扰，多一点顺手。", 12, Theme.Muted)); title.Margin = new Thickness(0, 0, 0, 20); root.Children.Add(title);
        var tabBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) }; Grid.SetRow(tabBar, 1); root.Children.Add(tabBar); Grid.SetRow(body, 2); root.Children.Add(body);
        foreach (var item in new[] { ("general", "交互与显示"), ("api", "翻译接口"), ("keys", "快捷键"), ("about", "数据与关于") })
        {
            var button = Theme.Button(item.Item2, () => Select(item.Item1)); button.Padding = new Thickness(12, 8, 12, 8); button.Margin = new Thickness(0, 0, 7, 7); button.BorderThickness = new Thickness(0); tabs[item.Item1] = button; tabBar.Children.Add(button);
        }
        var general = new StackPanel();
        var closeBehavior=new ComboBox{ItemsSource=new[]{"在后台继续运行（系统托盘）","完全退出应用"},SelectedIndex=draft.CloseToTray?0:1};
        closeBehavior.SelectionChanged+=(_,_)=>draft.CloseToTray=closeBehavior.SelectedIndex==0;
        Field(general,"关闭主窗口时",closeBehavior,"仅影响右上角关闭按钮与 Alt+F4；「退出应用」始终完全退出。");
        trigger = new ComboBox { ItemsSource = new[] { "按住 Alt + 鼠标停留", "鼠标停留自动查词", "仅使用查词快捷键" }, SelectedIndex = draft.Trigger == "Alt" ? 0 : draft.Trigger == "Hover" ? 1 : 2 };
        Field(general, "取词方式", trigger, "默认按住 Alt 取词，减少阅读时的误触。");
        var delay = Theme.T(draft.DwellMs + " ms", 12, Theme.Accent, true); dwell = new Slider { Minimum = 200, Maximum = 1200, TickFrequency = 50, IsSnapToTickEnabled = true, Value = draft.DwellMs, Margin = new Thickness(0, 8, 17, 0) }; dwell.ValueChanged += (_, _) => delay.Text = ((int)dwell.Value) + " ms";
        general.Children.Add(Theme.T("鼠标停留时长", 12, Theme.Ink, true)); general.Children.Add(Theme.Row(dwell, delay)); Theme.Space(general, 20);
        Toggle(general, "桌面玻璃悬浮球", "单击展开三个按钮，拖动到屏幕边缘可吸附。", draft.ShowBubble, v => draft.ShowBubble = v);
        Toggle(general, "贴边自动隐藏", "吸附后闲置会缩到屏幕边缘，鼠标靠近时唤出。", draft.BubbleAutoHide, v => draft.BubbleAutoHide = v);
        Toggle(general, "图片取词", "文字无法直接读取时，使用本地英文 OCR。", draft.OcrFallback, v => draft.OcrFallback = v);
        Toggle(general, "登录时启动", "启动后收起到托盘。", draft.StartAtLogin, v => draft.StartAtLogin = v);
        Toggle(general, "减少动态效果", "关闭界面切换、按钮和玻璃高光动效。", draft.ReduceMotion, v => draft.ReduceMotion = v);
        general.Children.Add(Theme.Rule()); general.Children.Add(Theme.T("词卡展示的信息", 15, Theme.Ink, true)); Theme.Space(general, 13);
        var options = new UniformGrid { Columns = 2 };
        void Option(string label, bool value, Action<bool> changed) { var row = new StackPanel { Margin = new Thickness(0, 0, 17, 0) }; Toggle(row, label, "", value, changed); options.Children.Add(row); }
        Option("音标", draft.ShowPhonetic, v => draft.ShowPhonetic = v); Option("中文释义", draft.ShowTranslation, v => draft.ShowTranslation = v); Option("英文释义", draft.ShowDefinition, v => draft.ShowDefinition = v); Option("词根与词源", draft.ShowRoots, v => draft.ShowRoots = v); Option("双语例句", draft.ShowExamples, v => draft.ShowExamples = v); Option("搭配与用法", draft.ShowUsage, v => draft.ShowUsage = v); general.Children.Add(options); pages["general"] = Theme.Scroll(general);
        var api = new StackPanel(); api.Children.Add(Info("按需连接，密钥留在这台电脑。", "句子翻译与 AI 补充使用你配置的接口。离线查词、词根和维基词典词源无需 API 密钥。")); Theme.Space(api, 17);
        provider = new ComboBox { ItemsSource = new[] { "DeepSeek", "OpenAI 兼容接口" }, SelectedIndex = draft.Provider == "DeepSeek" ? 0 : 1 }; Field(api, "服务类型", provider);
        url = new TextBox { Text = draft.BaseUrl }; Field(api, "Base URL", url, "支持基础地址 /v1 或完整的 /chat/completions 地址。"); model = new TextBox { Text = draft.Model }; Field(api, "模型名称", model);
        key = new PasswordBox { Password = Store.Unprotect(draft.EncryptedApiKey) }; System.Windows.Automation.AutomationProperties.SetName(key, "API 密钥"); Field(api, "API 密钥", key, "使用 Windows 用户级加密保存。清空并保存即可移除。");
        language = new ComboBox { ItemsSource = new[] { "简体中文", "繁體中文", "English", "日本語" }, SelectedItem = draft.TargetLanguage }; if (language.SelectedIndex < 0) language.SelectedIndex = 0; Field(api, "翻译目标语言", language);
        provider.SelectionChanged += (_, _) => { if (provider.SelectedIndex == 0) { url.Text = "https://api.deepseek.com"; model.Text = "deepseek-flash"; } }; pages["api"] = Theme.Scroll(api);
        var keys = new StackPanel(); keys.Children.Add(Info("把常用动作留给快捷键。", "使用 Ctrl、Alt、Shift、Win 与一个主键组合。保存时检查冲突，失败后保留原配置。")); Theme.Space(keys, 22);
        captureKey = new TextBox { Text = draft.CaptureHotkey }; lookupKey = new TextBox { Text = draft.LookupHotkey }; pauseKey = new TextBox { Text = draft.PauseHotkey }; Field(keys, "框选翻译", captureKey); Field(keys, "立即查询鼠标处单词", lookupKey); Field(keys, "暂停 / 恢复取词", pauseKey); pages["keys"] = Theme.Scroll(keys);
        var about = new StackPanel(); about.Children.Add(Info("Luma 拾词 0.1.5", "让阅读自然继续。为桌面英语学习做一个轻巧、安静的小助手。")); Theme.Space(about, 18);about.Children.Add(new UpdateView(controller,owner));Theme.Space(about,18);
        about.Children.Add(Theme.T("770,611 个离线词条\n" + controller.Dictionary.Roots.RootCount + " 条词根记录 · 关联 " + controller.Dictionary.Roots.CoveredWords.ToString("N0") + " 个单词\n维基词典词源：查询后缓存，可离线复用", 13)); about.Children.Add(Theme.Rule()); about.Children.Add(Theme.T("词典与词根：ECDICT · MIT\n在线词源：Wiktionary · CC BY-SA 4.0\n图片识别：Tesseract · Apache 2.0", 12, Theme.Muted)); Theme.Space(about, 18);
        var data = Theme.Button("打开本地数据文件夹", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(controller.Store.Root) { UseShellExecute = true }), false, "\uE8B7"); data.HorizontalAlignment = HorizontalAlignment.Left; about.Children.Add(data); Theme.Space(about, 15); about.Children.Add(Theme.T("支持英文取词与英文屏幕识别。Google / Bing 翻译接口尚未接入。", 11, Theme.Muted)); pages["about"] = Theme.Scroll(about);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; test = Theme.Button("测试连接", () => _ = TestConnection()); test.Margin = new Thickness(0, 0, 9, 0); actions.Children.Add(test); actions.Children.Add(Theme.Button("保存设置", () => Save(), true, "\uE73E"));
        feedback.Margin = new Thickness(0, 0, 15, 0); feedback.VerticalAlignment = VerticalAlignment.Center; var footer = Theme.Row(feedback, actions); footer.Margin = new Thickness(0, 17, 0, 0); Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root; Select("general");
    }
    private void Select(string name)
    {
        foreach (var (id, button) in tabs) button.Background = id == name ? Theme.B("#DFE7EEFF") : Brushes.Transparent;
        body.Content = pages[name]; test.Visibility = name == "api" ? Visibility.Visible : Visibility.Collapsed; Theme.Enter(pages[name]);
    }
    internal void SelectTabForAudit(string name) => Select(name);
    private static Border Info(string title, string message) { var p = new StackPanel(); p.Children.Add(Theme.T(title, 14, Theme.Ink, true)); Theme.Space(p, 7); p.Children.Add(Theme.T(message, 12, Theme.Muted)); return Theme.Card(p, new Thickness(18), Theme.B("#CDEDF2FF")); }
    private static void Field(StackPanel panel, string label, Control control, string hint = "")
    {
        panel.Children.Add(Theme.T(label, 12, Theme.Ink, true)); Theme.Space(panel, 8); System.Windows.Automation.AutomationProperties.SetName(control, label); panel.Children.Add(control); if (hint.Length > 0) { Theme.Space(panel, 6); panel.Children.Add(Theme.T(hint, 11, Theme.Muted)); }
        Theme.Space(panel, 20);
    }
    private static void Toggle(StackPanel panel, string title, string subtitle, bool value, Action<bool> change)
    {
        var text = new StackPanel { Margin = new Thickness(0, 0, 14, 0) }; text.Children.Add(Theme.T(title, 12, Theme.Ink, true)); if (subtitle.Length > 0) text.Children.Add(Theme.T(subtitle, 11, Theme.Muted)); var toggle = Theme.Toggle(value); System.Windows.Automation.AutomationProperties.SetName(toggle, title); toggle.Checked += (_, _) => change(true); toggle.Unchecked += (_, _) => change(false); var row = Theme.Row(text, toggle); row.Margin = new Thickness(0, 0, 0, 19); panel.Children.Add(row);
    }
    private bool Save()
    {
        try
        {
            if (owner.TranslationBusy) throw new InvalidOperationException("请先等待或取消当前翻译。");
            draft.Trigger = trigger.SelectedIndex == 0 ? "Alt" : trigger.SelectedIndex == 1 ? "Hover" : "Hotkey"; draft.DwellMs = (int)dwell.Value; draft.Provider = provider.SelectedIndex == 0 ? "DeepSeek" : "OpenAI 兼容接口"; draft.BaseUrl = url.Text.Trim(); draft.Model = model.Text.Trim(); draft.EncryptedApiKey = Store.Protect(key.Password.Trim()); draft.TargetLanguage = language.SelectedItem?.ToString() ?? "简体中文";
            draft.CaptureHotkey = captureKey.Text.Trim(); draft.LookupHotkey = lookupKey.Text.Trim(); draft.PauseHotkey = pauseKey.Text.Trim(); draft.BubbleX = controller.Store.Settings.BubbleX; draft.BubbleY = controller.Store.Settings.BubbleY;draft.BubblePositionSaved=controller.Store.Settings.BubblePositionSaved; draft.Enabled = controller.Store.Settings.Enabled;
            AiService.Endpoint(draft.BaseUrl); if (string.IsNullOrWhiteSpace(draft.Model)) throw new InvalidOperationException("请填写模型名称。"); controller.ApplySettings(draft); feedback.Text = "已保存，即刻生效。"; feedback.Foreground = Theme.Accent; return true;
        }
        catch (Exception ex) { feedback.Text = ex.Message; feedback.Foreground = Theme.B("#AC5863"); return false; }
    }
    private async Task TestConnection()
    {
        if (!Save()) return; test.IsEnabled = false; feedback.Text = "正在测试连接…"; using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await controller.Ai.Test(ct.Token); feedback.Text = "连接成功，可以开始翻译。"; feedback.Foreground = Theme.Accent; } catch (Exception ex) { feedback.Text = AiService.Friendly(ex); feedback.Foreground = Theme.B("#AC5863"); } finally { test.IsEnabled = true; }
    }
}
