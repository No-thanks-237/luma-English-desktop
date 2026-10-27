using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Luma.Core;
using Luma.Services;

namespace Luma.UI;

internal sealed class WordWindow : Window
{
    private readonly Controller controller;
    private readonly WordEntry entry;
    private readonly string source;
    private readonly Grid surface = new();
    private readonly StackPanel body = new();
    private readonly StackPanel tabStrip = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock message = Theme.T("", 11, Theme.Muted);
    private readonly TextBlock provenance = Theme.T("", 10, Theme.Muted);
    private readonly Button pin;
    private readonly Button save;
    private readonly Button explain;
    private CancellationTokenSource? request;
    private string requestKind = "";
    private string selected = "meaning";
    public bool Pinned { get; private set; }
    public bool Busy => request != null;
    internal void SelectTabForAudit(string tab) { selected = tab; RenderBody(); }
    public void SetPinned() { Pinned = true; UpdateChrome(); }
    public WordWindow(Controller controller, WordEntry entry, string source, System.Drawing.Point? point = null)
    {
        this.controller = controller; this.entry = entry; this.source = source;
        Title = "Luma · " + entry.Word; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        FitToScreen(point ?? System.Drawing.Point.Empty); Native.Glass(this, true);
        surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); surface.RowDefinitions.Add(new RowDefinition()); surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(23, 17, 23, 12) };
        var brand = Theme.T("LUMA  /  拾词", 10, Theme.Muted, true); brand.VerticalAlignment = VerticalAlignment.Center; var controls = new StackPanel { Orientation = Orientation.Horizontal };
        pin = Theme.IconButton("\uE718", "固定 / 取消固定", () => { Pinned = !Pinned; UpdateChrome(); }); controls.Children.Add(pin); controls.Children.Add(Theme.IconButton("\uE8BB", "关闭词卡", Close)); header.Children.Add(Theme.Row(brand, controls)); Theme.Space(header, 9);
        var word = Theme.T(entry.Word, Math.Max(23, 34 - Math.Max(0, entry.Word.Length - 15) * .6)); word.FontFamily = Theme.Serif; word.LineHeight = word.FontSize * 1.3; word.ToolTip = entry.Word; word.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(Theme.Row(word, Theme.IconButton("\uE767", "朗读单词", () => { var error = controller.Speak(entry.Word); if (error != null) SetMessage(error); })));
        if (controller.Store.Settings.ShowPhonetic && entry.Phonetic.Length > 0) header.Children.Add(Theme.T("/" + entry.Phonetic.Trim('/') + "/", 13, Theme.Muted));
        if (!entry.Word.Equals(entry.QueriedAs, StringComparison.OrdinalIgnoreCase)) header.Children.Add(Theme.T(entry.QueriedAs + " → " + entry.Word + "（" + entry.Resolution + "）", 11, Theme.Muted));
        surface.Children.Add(header); tabStrip.Margin = new Thickness(23, 0, 23, 0); Grid.SetRow(tabStrip, 1); surface.Children.Add(tabStrip);
        var scroll = Theme.Scroll(body); scroll.Margin = new Thickness(23, 15, 16, 13); Grid.SetRow(scroll, 2); surface.Children.Add(scroll);
        var footer = new StackPanel { Margin = new Thickness(21, 12, 21, 16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        save = Theme.Button("收藏", () => { controller.Store.ToggleWord(entry); UpdateChrome(); }, false, "\uE710"); save.Padding = new Thickness(13, 9, 13, 9); actions.Children.Add(save);
        var copy = Theme.IconButton("\uE8C8", "复制单词", () => { controller.Copy(entry.Word); SetMessage("单词已复制。"); }); copy.Margin = new Thickness(6, 0, 0, 0); actions.Children.Add(copy);
        explain = Theme.Button("AI 补充", () => _ = Explain(), true, "\uE734"); explain.Padding = new Thickness(12, 9, 12, 9); footer.Children.Add(Theme.Row(actions, explain));
        message.Margin = new Thickness(0, 9, 0, 0); message.Visibility = Visibility.Collapsed; footer.Children.Add(message); provenance.Margin = new Thickness(0, 9, 0, 0); footer.Children.Add(provenance);
        var footerSurface = new Border { Background = Theme.B("#64FFFFFF"), BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer }; Grid.SetRow(footerSurface, 3); surface.Children.Add(footerSurface);
        var glass = Theme.Card(surface, new Thickness(0), Theme.B("#EEF7F9FE")); glass.CornerRadius = new CornerRadius(23); Content = glass;
        Loaded += (_, _) => { FitToScreen(point ?? System.Drawing.Point.Empty); Theme.Enter(surface); }; Closed += (_, _) => { request?.Cancel(); controller.WordClosed(this); }; RenderBody(); UpdateChrome();
    }
    public void FitToScreen(System.Drawing.Point point)
    {
        var area = System.Windows.Forms.Screen.FromPoint(point).WorkingArea;
        var own = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var dpi = Native.GetDpiForWindow(own != IntPtr.Zero ? own : Native.WindowFromPoint(new Native.POINT { X = point.X, Y = point.Y })); var scale = Math.Max(1, dpi / 96.0);
        Width = Math.Min(420, (area.Width - 32) / scale); Height = Math.Min(620, (area.Height - 32) / scale); MaxHeight = (area.Height - 24) / scale;
    }
    private void SetMessage(string text) { message.Text = text; message.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible; }
    private void UpdateChrome()
    {
        pin.Background = Pinned ? Theme.B("#DFE7EEFF") : Brushes.Transparent;
        var saved = controller.Store.IsSaved(entry.Word); save.Content = Theme.T(saved ? "已收藏" : "收藏单词", 12, saved ? Theme.Accent : Theme.Ink, true);
        explain.IsEnabled = request == null; explain.Content = Theme.T(requestKind == "AI" ? "生成中…" : "AI 补充", 12, Brushes.White, true);
        provenance.Text = (Pinned ? "已固定 · " : "") + source + " · " + (entry.Found ? "ECDICT" : "未收录") + " · Esc 关闭";
    }
    private void RenderBody()
    {
        var settings = controller.Store.Settings;
        if (selected == "roots" && !settings.ShowRoots) selected = "meaning";
        body.Children.Clear(); tabStrip.Children.Clear();
        foreach (var item in new[] { ("meaning", "释义"), ("roots", "构词"), ("examples", "例句") })
        {
            var button = Theme.Button(item.Item2, () => { selected = item.Item1; RenderBody(); Theme.Enter(body); }); button.Padding = new Thickness(18, 8, 18, 8); button.BorderThickness = new Thickness(0); button.Margin = new Thickness(0, 0, 6, 0); button.Background = selected == item.Item1 ? Theme.B("#E1E9F9FF") : Brushes.Transparent;
            if (item.Item1 == "roots" && !settings.ShowRoots) { button.IsEnabled = false; button.ToolTip = "在偏好设置中打开词根与词源显示。"; }
            tabStrip.Children.Add(button);
        }
        if (selected == "roots") RenderRoots(); else if (selected == "examples") RenderExamples(); else RenderMeaning();
        UpdateChrome();
    }
    private void RenderMeaning()
    {
        var settings = controller.Store.Settings;
        if (settings.ShowTranslation || !entry.Found) ReadOnlyText(body, entry.Translation.Length > 0 ? entry.Translation : "此词条暂未提供中文释义。", 15);
        else body.Children.Add(Theme.T("中文释义已在设置中隐藏。", 12, Theme.Muted));
        if (settings.ShowDefinition && entry.Definition.Length > 0) Section(body, "英文释义", entry.Definition);
        if (settings.ShowUsage && entry.Details?.Usage is { Length: > 0 } usage) Section(body, "搭配与用法", usage);
        if (entry.Details != null && settings.ShowUsage) { Theme.Space(body, 13); body.Children.Add(Theme.T(entry.Details.Source, 10, Theme.Muted)); }
        if (!settings.ShowUsage || entry.Details == null) { Theme.Space(body, 18); body.Children.Add(Theme.T("构词与词源在“构词”页签。更多搭配和例句可按需补充。", 12, Theme.Muted)); }
    }
    private void RenderExamples()
    {
        if (!controller.Store.Settings.ShowExamples) { body.Children.Add(Theme.T("例句显示已关闭，可在偏好设置中打开。", 13, Theme.Muted)); return; }
        if (entry.Details?.Examples.Count > 0)
        {
            foreach (var example in entry.Details.Examples)
            {
                var p = new StackPanel(); p.Children.Add(Theme.T(example.English, 14)); Theme.Space(p, 8); p.Children.Add(Theme.T(example.Chinese, 12, Theme.Muted)); var card = Theme.Card(p, new Thickness(15)); card.Margin = new Thickness(0, 0, 0, 12); body.Children.Add(card);
            }
            body.Children.Add(Theme.T(entry.Details.Source, 10, Theme.Muted));
        }
        else { body.Children.Add(Theme.T("还没有这个词的例句。", 17, Theme.Ink, true)); Theme.Space(body, 9); body.Children.Add(Theme.T("点击下方 AI 补充，可生成双语例句与搭配。已有的离线释义和词根不需要密钥。", 12, Theme.Muted)); }
    }
    private void RenderRoots()
    {
        var online = Theme.Button(requestKind == "词源" ? "查询中…" : entry.RootInfo.Online == null ? "在线词源" : "刷新词源", () => _ = FetchEtymology(), false, "\uE774"); online.Padding = new Thickness(10, 7, 10, 7); online.IsEnabled = request == null;
        var rootHeading = Theme.T("离线构词线索", 12, Theme.Accent, true); rootHeading.VerticalAlignment = VerticalAlignment.Center; body.Children.Add(Theme.Row(rootHeading, online)); Theme.Space(body, 10);
        if (entry.RootInfo.Hints.Count > 0)
        {
            foreach (var hint in entry.RootInfo.Hints)
            {
                var p = new StackPanel(); var title = Theme.T(hint.Form, 15, Theme.Ink, true); title.Margin = new Thickness(0, 0, 8, 0); p.Children.Add(Theme.Row(title, Theme.Pill(RootService.KindLabel(hint.Kind), false)));
                var chinese = RootService.ChineseGloss(hint.Meaning); Theme.Space(p, 7); if (chinese.Length > 0) p.Children.Add(Theme.T(chinese, 13)); p.Children.Add(Theme.T(hint.Meaning, 12, Theme.Muted));
                if (hint.Origin.Length > 0) { Theme.Space(p, 6); p.Children.Add(Theme.T(RootService.OriginLabel(hint.Origin), 10, Theme.Muted)); }
                var related = string.Join(" · ", hint.Examples.Where(x => !x.Equals(entry.Word, StringComparison.OrdinalIgnoreCase)).Take(3)); if (related.Length > 0) p.Children.Add(Theme.T("同组：" + related, 10, Theme.Muted));
                var card = Theme.Card(p, new Thickness(14)); card.Margin = new Thickness(0, 0, 0, 10); body.Children.Add(card);
            }
            body.Children.Add(Theme.T("按词典例词关联，并非完整拆分。", 10, Theme.Muted));
        }
        else body.Children.Add(Theme.T("离线词根库暂未关联此词，可在线补充词源。", 12, Theme.Muted));
        Theme.Space(body, 9); var sourceButton = Theme.Button("ECDICT 数据来源", () => OpenReference(RootService.SourceUrl)); sourceButton.HorizontalAlignment = HorizontalAlignment.Left; sourceButton.Padding = new Thickness(10, 6, 10, 6); body.Children.Add(sourceButton); body.Children.Add(Theme.Rule());
        body.Children.Add(Theme.T("维基词典词源", 12, Theme.Accent, true)); Theme.Space(body, 10);
        if (entry.RootInfo.Online is { } cached)
        {
            ReadOnlyText(body, cached.Text, 13); Theme.Space(body, 10); body.Children.Add(Theme.T("缓存于 " + cached.FetchedAt.ToString("yyyy.MM.dd") + " · " + cached.License, 10, Theme.Muted));
            var links = new WrapPanel(); var original = Theme.Button("查看原文与作者", () => OpenReference(cached.SourceUrl)); original.Padding = new Thickness(9, 6, 9, 6); original.Margin = new Thickness(0, 8, 7, 0); links.Children.Add(original); var license = Theme.Button("共享许可", () => OpenReference("https://creativecommons.org/licenses/by-sa/4.0/")); license.Padding = new Thickness(9, 6, 9, 6); license.Margin = new Thickness(0, 8, 0, 0); links.Children.Add(license); body.Children.Add(links);
        }
        else body.Children.Add(Theme.T("直接查询英语词源原文，无需 API 密钥。查过的结果会缓存。", 12, Theme.Muted));
        if (entry.Details?.Roots is { Length: > 0 } extra && (entry.Details.Source.StartsWith("AI") || entry.RootInfo.Hints.Count == 0 && entry.RootInfo.Online == null)) Section(body, entry.Details.Source.StartsWith("AI") ? "AI 补充 · 供参考" : "内置词源笔记", extra);
    }
    private static void Section(StackPanel panel, string heading, string text) { panel.Children.Add(Theme.Rule()); panel.Children.Add(Theme.T(heading, 12, Theme.Accent, true)); Theme.Space(panel, 9); ReadOnlyText(panel, text, 13); }
    private static void ReadOnlyText(StackPanel panel, string text, double size)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = size, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Top, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; panel.Children.Add(box);
    }
    private void OpenReference(string value)
    {
        try { if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host is not ("en.wiktionary.org" or "github.com" or "creativecommons.org")) return; System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { SetMessage("无法打开浏览器，请稍后重试。"); }
    }
    private async Task FetchEtymology()
    {
        if (Busy) return; request = new CancellationTokenSource(); requestKind = "词源"; Pinned = true; SetMessage("正在查询维基词典，无需 API 密钥…"); RenderBody();
        try { entry.RootInfo.Online = await controller.Etymologies.Fetch(entry.Word, request.Token); SetMessage("词源已缓存，可离线查看。"); }
        catch (Exception ex) { SetMessage(EtymologyService.Friendly(ex)); }
        finally { request.Dispose(); request = null; requestKind = ""; if (IsVisible) RenderBody(); }
    }
    private async Task Explain()
    {
        if (Busy) return; if (!controller.Ai.Configured) { SetMessage("在偏好设置中配置接口，即可补充双语例句。离线词根和在线词源无需密钥。"); return; }
        request = new CancellationTokenSource(); requestKind = "AI"; Pinned = true; SetMessage("正在补充学习信息…"); RenderBody();
        try { entry.Details = await controller.Ai.Explain(entry.Word, request.Token); SetMessage("学习信息已缓存。"); }
        catch (Exception ex) { SetMessage(AiService.Friendly(ex)); }
        finally { request.Dispose(); request = null; requestKind = ""; if (IsVisible) RenderBody(); }
    }
}
