using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Luma.Core;

namespace Luma.UI;

internal sealed class MainWindow : Window
{
    private readonly Controller controller;
    private readonly ContentControl page = new();
    private readonly Dictionary<string, Button> nav = new();
    private readonly TextBlock stateLabel = Theme.T("", 11, Theme.Accent, true);
    private readonly TextBlock notice = Theme.T("", 11, Theme.Muted);
    private readonly Border noticeSurface;
    private readonly TextBlock pageLabel = Theme.T("随处查词", 13, Theme.Muted);
    private TranslationView? translations;
    private SettingsView? settings;
    private bool closing;
    private string current = "home";
    public MainWindow(Controller controller)
    {
        this.controller = controller;
        Title = "Luma 拾词 · 桌面英语助手";
        var work = SystemParameters.WorkArea; Width = Math.Min(1120, work.Width - 48); Height = Math.Min(790, work.Height - 48); MinWidth = Math.Min(840, work.Width - 32); MinHeight = Math.Min(560, work.Height - 32);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "luma.png")));
        Native.Glass(this);
        WindowChrome.GetWindowChrome(this).CaptionHeight = 59;
        var shell = new Grid { Background = new LinearGradientBrush(new GradientStopCollection { new((Color)ColorConverter.ConvertFromString("#F4F8FAFF"), 0), new((Color)ColorConverter.ConvertFromString("#EDEFF3FA"), 1) }, 115) };
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) }); shell.RowDefinitions.Add(new RowDefinition());
        var top = new Grid { Margin = new Thickness(26, 0, 16, 0), Background = Brushes.Transparent };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var logo = Theme.T("luma", 23, Theme.Ink, true); logo.FontFamily = Theme.Serif; brand.Children.Add(logo);
        var separator = Theme.T("/", 14, Theme.B("#B7C0D2")); separator.Margin = new Thickness(15, 0, 15, 0); brand.Children.Add(separator); brand.Children.Add(pageLabel); top.Children.Add(brand);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        WindowChrome.SetIsHitTestVisibleInChrome(controls, true);
        controls.Children.Add(Theme.IconButton("\uE921", "最小化", () => WindowState = WindowState.Minimized)); controls.Children.Add(Theme.IconButton("\uE922", "最大化 / 还原", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized)); controls.Children.Add(Theme.IconButton("\uE8BB", "收起到托盘", Hide)); top.Children.Add(controls);
        shell.Children.Add(top);
        var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) }); layout.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(layout, 1); shell.Children.Add(layout);
        var rail = new Grid { Margin = new Thickness(15, 8, 0, 18) }; rail.RowDefinitions.Add(new RowDefinition()); rail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var links = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        void AddNav(string key, string title, string glyph)
        {
            var button = Theme.Button(title, () => Navigate(key), false, glyph); button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Padding = new Thickness(17, 14, 12, 14); button.Margin = new Thickness(0, 0, 0, 8); button.BorderThickness = new Thickness(0); nav[key] = button; links.Children.Add(button);
        }
        AddNav("home", "随处查词", "\uE721"); AddNav("translate", "句子翻译", "\uE8C1"); AddNav("words", "我的单词本", "\uE82D"); Theme.Space(links, 20); AddNav("settings", "偏好设置", "\uE713"); rail.Children.Add(links);
        var bottom = new StackPanel { Margin = new Thickness(7, 18, 7, 8) };
        var statusButton = Theme.Button("取词状态", () => controller.ToggleEnabled()); statusButton.Content = stateLabel; statusButton.Padding = new Thickness(10, 10, 10, 10); bottom.Children.Add(statusButton); Theme.Space(bottom, 14); bottom.Children.Add(Theme.T("LUMA  0.1.4\n一个词，一点新发现。", 10, Theme.Muted)); Grid.SetRow(bottom, 1); rail.Children.Add(bottom); layout.Children.Add(rail);
        var main = new Grid { Margin = new Thickness(25, 10, 26, 22) }; main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.Children.Add(page);
        noticeSurface = Theme.Card(notice, new Thickness(13, 9, 13, 9), Theme.B("#E7ECF2FF")); noticeSurface.Margin = new Thickness(0, 12, 0, 0); noticeSurface.Visibility = Visibility.Collapsed; Grid.SetRow(noticeSurface, 1); main.Children.Add(noticeSurface); Grid.SetColumn(main, 1); layout.Children.Add(main);
        Content = new Border { CornerRadius = new CornerRadius(24), BorderBrush = Theme.B("#F7FFFFFF"), BorderThickness = new Thickness(1), Child = shell };
        Closing += (_, e) => { if (!closing) { e.Cancel = true; Hide(); } }; Closed += (_, _) => translations?.Cancel(); Navigate("home");
    }
    public void QuitClose() { closing = true; Close(); }
    public void SetNotice(string text) { notice.Text = text; noticeSurface.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible; }
    public void UpdateStatus() { stateLabel.Text = controller.Store.Settings.Enabled ? "●  取词已开启" : "Ⅱ  取词已暂停"; stateLabel.Foreground = controller.Store.Settings.Enabled ? Theme.Accent : Theme.Muted; }
    public void Navigate(string target)
    {
        current = target; pageLabel.Text = target switch { "translate" => "句子翻译", "words" => "我的单词本", "settings" => "偏好设置", _ => "随处查词" };
        foreach (var (key, button) in nav) button.Background = key == target ? Theme.B("#DDE6EDFF") : Brushes.Transparent;
        page.Content = target switch
        {
            "translate" => translations ??= new TranslationView(controller, () => Navigate("settings")),
            "settings" => settings ??= new SettingsView(controller, this),
            "words" => WordsPage(),
            _ => HomePage()
        };
        if (page.Content is FrameworkElement element) Theme.Enter(element); UpdateStatus();
    }
    public bool TranslationBusy => translations?.Busy == true;
    public void SetCapturedText(string text, string status) { if (current != "translate") Navigate("translate"); translations!.SetCaptured(text, status); }
    internal FrameworkElement CurrentPage => (FrameworkElement)page.Content;
    private static StackPanel PageTitle(string tag, string title, string subtitle)
    {
        var panel = new StackPanel(); panel.Children.Add(Theme.T(tag, 10, Theme.Accent, true)); Theme.Space(panel, 8); panel.Children.Add(Theme.T(title, 27, Theme.Ink, true)); Theme.Space(panel, 6); panel.Children.Add(Theme.T(subtitle, 12, Theme.Muted)); Theme.Space(panel, 23); return panel;
    }
    private UIElement HomePage()
    {
        var hint=controller.Store.Settings.Trigger switch {"Hover"=>"鼠标停在单词上，释义与构词信息就在身边。","Hotkey"=>"按 "+controller.Store.Settings.LookupHotkey+" 查询鼠标指向的单词。",_=>"按住 Alt 指向单词，释义与构词信息就在身边。"};
        var panel = PageTitle("A LITTLE CURIOSITY, EVERY DAY", "阅读时，轻轻拾起一个词。", hint);
        var searchPanel = new StackPanel(); var searchHeading = Theme.Row(Theme.T("今天想读懂哪个词？", 16, Theme.Ink, true), Theme.Pill("离线可用")); searchPanel.Children.Add(searchHeading); Theme.Space(searchPanel, 17);
        var input = new TextBox { MinHeight = 50, FontSize = 15, ToolTip = "输入英文单词，按 Enter 查询" }; System.Windows.Automation.AutomationProperties.SetName(input, "输入英文单词");
        var placeholder = Theme.T("输入单词，例如 serendipity", 13, Theme.Muted); placeholder.Margin = new Thickness(15, 0, 0, 0); placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.IsHitTestVisible = false;
        var box = new Grid(); box.Children.Add(input); box.Children.Add(placeholder); input.TextChanged += (_, _) => placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        void Lookup() { if (input.Text.Trim().Length > 0) controller.ManualLookup(input.Text); }
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Lookup(); e.Handled = true; } };
        var go = Theme.Button("查词", Lookup, true, "\uE721"); go.Margin = new Thickness(10, 0, 0, 0); searchPanel.Children.Add(Theme.Row(box, go)); Theme.Space(searchPanel, 14);
        var suggestions = new WrapPanel(); suggestions.Children.Add(Theme.T("试试看  ", 11, Theme.Muted));
        foreach (var word in new[] { "philosophy", "serendipity", "unbelievable" }) { var chip = Theme.Button(word, () => controller.ManualLookup(word)); chip.Padding = new Thickness(10, 5, 10, 5); chip.Margin = new Thickness(0, 0, 7, 7); suggestions.Children.Add(chip); }
        searchPanel.Children.Add(suggestions);
        panel.Children.Add(Theme.Card(searchPanel, new Thickness(23), Theme.B("#EFFFFFFF"))); Theme.Space(panel, 18);
        var features = new Grid(); features.ColumnDefinitions.Add(new ColumnDefinition()); features.ColumnDefinitions.Add(new ColumnDefinition());
        Border Feature(string glyph, string title, string desc, string key, Action action)
        {
            var content = new StackPanel(); var icon = Theme.Card(Theme.I(glyph, 19, Theme.Accent), new Thickness(11), Theme.B("#E8EEFF")); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.CornerRadius = new CornerRadius(13); content.Children.Add(icon); Theme.Space(content, 15); content.Children.Add(Theme.T(title, 17, Theme.Ink, true)); Theme.Space(content, 6); content.Children.Add(Theme.T(desc, 12, Theme.Muted)); Theme.Space(content, 17);
            var button = Theme.Button(key, action, false, "\uE72A"); button.HorizontalAlignment = HorizontalAlignment.Left; button.Padding = new Thickness(11, 7, 11, 7); content.Children.Add(button); return Theme.Card(content, new Thickness(22));
        }
        var capture = Feature("\uE7A8", "把整个句子读懂", "框选屏幕上的英文，检查原文后再翻译。", controller.Store.Settings.CaptureHotkey, () => controller.BeginCapture()); capture.Margin = new Thickness(0, 0, 9, 0); features.Children.Add(capture);
        var words = Feature("\uE82D", "把遇见的词留下", "收藏释义、写下自己的记忆点，让下一次更熟悉。", "打开单词本", () => Navigate("words")); words.Margin = new Thickness(9, 0, 0, 0); Grid.SetColumn(words, 1); features.Children.Add(words); panel.Children.Add(features); Theme.Space(panel, 21);
        var stats = new WrapPanel();
        foreach (var label in new[] { "770,611 个离线词条", controller.Dictionary.Roots.CoveredWords.ToString("N0") + " 个词关联词根", "在线词源无需密钥" }) { var text = Theme.T(label, 11, Theme.Muted); text.Margin = new Thickness(0, 0, 24, 9); stats.Children.Add(text); }
        panel.Children.Add(stats);
        return Theme.Scroll(panel);
    }
    private UIElement WordsPage()
    {
        var panel = PageTitle("YOUR WORD COLLECTION", "把好奇，留在这里。", "查词时点一下收藏。释义、笔记和学习时间都保存在这台电脑上。");
        var count = Theme.T("", 12, Theme.Muted); var export = Theme.Button("导出 CSV", ExportWords, false, "\uE896"); panel.Children.Add(Theme.Row(count, export)); Theme.Space(panel, 15);
        panel.Children.Add(Theme.T("搜索单词或中文释义", 12, Theme.Muted)); Theme.Space(panel, 8);
        var filter = new TextBox { ToolTip = "搜索单词或中文释义", MinHeight = 44 }; System.Windows.Automation.AutomationProperties.SetName(filter, "搜索单词本"); panel.Children.Add(filter); Theme.Space(panel, 16); var list = new StackPanel(); panel.Children.Add(list);
        void Populate()
        {
            list.Children.Clear(); var query = filter.Text.Trim(); var words = controller.Store.Words.Where(x => x.Word.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Translation.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList(); count.Text = $"收藏 {controller.Store.Words.Count} 个词 · 当前显示 {words.Count} 个";
            if (words.Count == 0) { var empty = new StackPanel { Margin = new Thickness(5, 25, 5, 25) }; var icon = Theme.I("\uE82D", 32, Theme.B("#ACB9E8")); icon.Margin = new Thickness(0, 0, 0, 18); empty.Children.Add(icon); empty.Children.Add(Theme.T(query.Length == 0 ? "下一次遇见，就会更熟悉。" : "没有匹配的单词。", 19, Theme.Ink, true)); Theme.Space(empty, 8); empty.Children.Add(Theme.T(query.Length == 0 ? "在词卡底部点击收藏，慢慢建立自己的词库。" : "换一个拼写或中文关键词试试。", 12, Theme.Muted)); list.Children.Add(Theme.Card(empty)); return; }
            foreach (var saved in words)
            {
                var body = new StackPanel(); var name = Theme.T(saved.Word, 25); name.FontFamily = Theme.Serif; var tools = new StackPanel { Orientation = Orientation.Horizontal };
                tools.Children.Add(Theme.IconButton("\uE721", "查看 " + saved.Word, () => controller.ManualLookup(saved.Word))); tools.Children.Add(Theme.IconButton("\uE767", "朗读 " + saved.Word, () => controller.Speak(saved.Word))); tools.Children.Add(Theme.IconButton("\uE74D", "移出单词本", () => { controller.Store.Words.Remove(saved); controller.Store.SaveWords(); Populate(); })); body.Children.Add(Theme.Row(name, tools));
                body.Children.Add(Theme.T(saved.Translation.Split('\n')[0], 12, Theme.Muted)); Theme.Space(body, 12); var note = new TextBox { Text = saved.Note, ToolTip = "写下自己的记忆点，离开输入框时保存", FontSize = 12, Padding = new Thickness(11, 9, 11, 9) }; System.Windows.Automation.AutomationProperties.SetName(note, saved.Word + " 的学习笔记"); note.TextChanged += (_, _) => saved.Note = note.Text; note.LostKeyboardFocus += (_, _) => controller.Store.SaveWords(); note.Unloaded += (_, _) => controller.Store.SaveWords(); body.Children.Add(note); Theme.Space(body, 9); body.Children.Add(Theme.T(saved.SavedAt.ToString("yyyy.MM.dd  HH:mm"), 10, Theme.Muted)); var card = Theme.Card(body, new Thickness(20)); card.Margin = new Thickness(0, 0, 0, 12); list.Children.Add(card);
            }
        }
        filter.TextChanged += (_, _) => Populate(); Populate(); return Theme.Scroll(panel);
    }
    private void ExportWords()
    {
        if (controller.Store.Words.Count == 0) { SetNotice("收藏单词后即可导出。"); return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "Luma-words-" + DateTime.Now.ToString("yyyyMMdd") + ".csv", Filter = "CSV 表格|*.csv", DefaultExt = ".csv" }; if (dialog.ShowDialog(this) != true) return;
        try { static string Csv(string text) => "\"" + (text.Length > 0 && "=+-@\t\r".Contains(text[0]) ? "'" : "") + text.Replace("\"", "\"\"") + "\""; var lines = new List<string> { "word,translation,note,saved_at" }; lines.AddRange(controller.Store.Words.Select(x => string.Join(",", Csv(x.Word), Csv(x.Translation), Csv(x.Note), Csv(x.SavedAt.ToString("O"))))); File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true)); SetNotice("单词本已导出。"); } catch (Exception ex) { SetNotice("导出失败：" + ex.Message); }
    }
}
