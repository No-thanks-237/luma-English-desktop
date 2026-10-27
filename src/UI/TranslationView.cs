using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Luma.Core;
using Luma.Services;

namespace Luma.UI;

internal sealed class TranslationView : UserControl
{
    private readonly Controller controller;
    private readonly TextBox original = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, FontSize = 14 };
    private readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, FontSize = 14, Background = Theme.B("#F2F5F8FF") };
    private readonly TextBlock status = Theme.T("", 11, Theme.Muted);
    private readonly StackPanel historyItems = new();
    private readonly Grid editors = new();
    private readonly Border sourceCard;
    private readonly Border outputCard;
    private readonly Button translate;
    private readonly Button cancel;
    private CancellationTokenSource? request;
    public bool Busy => request != null;
    public TranslationView(Controller controller, Action openSettings)
    {
        this.controller = controller;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var content = new StackPanel(); content.Children.Add(Theme.T("CAPTURE & UNDERSTAND", 10, Theme.Accent, true)); Theme.Space(content, 8); content.Children.Add(Theme.T("读懂整个句子。", 27, Theme.Ink, true)); Theme.Space(content, 6); content.Children.Add(Theme.T("框选、检查原文，再开始翻译。让理解跟上阅读。", 12, Theme.Muted)); Theme.Space(content, 20);
        var capture = Theme.Button("框选屏幕文字", () => controller.BeginCapture(), false, "\uE7A8"); capture.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(Theme.Row(capture, Theme.Pill(controller.Store.Settings.CaptureHotkey, false))); Theme.Space(content, 18);
        Border Editor(string title, TextBox box, UIElement right)
        {
            var layout = new Grid { Height = 282 }; layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
            var label = Theme.T(title, 13, Theme.Ink, true); label.VerticalAlignment = VerticalAlignment.Center; var header = Theme.Row(label, right); header.Margin = new Thickness(0, 0, 0, 12); layout.Children.Add(header); Grid.SetRow(box, 1); layout.Children.Add(box); return Theme.Card(layout, new Thickness(17));
        }
        sourceCard = Editor("原文", original, Theme.Pill("可编辑", false)); outputCard = Editor("译文", output, Theme.IconButton("\uE8C8", "复制译文", () => { if (output.Text.Length > 0) controller.Copy(output.Text); }));
        editors.ColumnDefinitions.Add(new ColumnDefinition()); editors.ColumnDefinitions.Add(new ColumnDefinition()); editors.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); editors.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); editors.Children.Add(sourceCard); editors.Children.Add(outputCard); content.Children.Add(editors);
        SizeChanged += (_, _) => ArrangeEditors();
        System.Windows.Automation.AutomationProperties.SetName(original, "待翻译原文"); System.Windows.Automation.AutomationProperties.SetName(output, "翻译结果");
        var history = new Expander { Header = Theme.T("最近翻译 · 保存在本机", 12, Theme.Muted), Content = historyItems, Margin = new Thickness(0, 19, 0, 5) }; content.Children.Add(history); RebuildHistory(); root.Children.Add(Theme.Scroll(content));
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) }; var actions = new WrapPanel(); translate = Theme.Button("开始翻译", () => _ = Translate(), true, "\uE8C1"); actions.Children.Add(translate); cancel = Theme.Button("取消", Cancel); cancel.Visibility = Visibility.Collapsed; cancel.Margin = new Thickness(9, 0, 0, 0); actions.Children.Add(cancel); var configure = Theme.Button("接口设置", openSettings, false, "\uE713"); configure.Margin = new Thickness(9, 0, 0, 0); actions.Children.Add(configure); footer.Children.Add(actions); status.Margin = new Thickness(0, 9, 0, 0); footer.Children.Add(status); Grid.SetRow(footer, 1); root.Children.Add(footer); Content = root;
        status.Text = controller.Ai.Configured ? "已配置 " + controller.Store.Settings.Provider + " · 点击翻译后发送原文。" : "填写接口密钥后即可翻译。屏幕识别始终在本地进行。";
    }
    private void ArrangeEditors()
    {
        var wide = ActualWidth >= 700;
        editors.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(outputCard, wide ? 1 : 0); Grid.SetRow(outputCard, wide ? 0 : 1); sourceCard.Margin = wide ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 12); outputCard.Margin = wide ? new Thickness(8, 0, 0, 0) : new Thickness(0);
    }
    public void SetCaptured(string text, string message) { original.Text = text; output.Text = ""; status.Text = message; }
    public void Cancel() => request?.Cancel();
    private async Task Translate()
    {
        if (Busy) return; var text = original.Text.Trim();
        if (text.Length == 0) { status.Text = "先框选屏幕文字，或输入需要翻译的原文。"; return; }
        if (text.Length > 12000) { status.Text = "每次最多翻译 12,000 个字符，请分段处理。"; return; }
        if (!controller.Ai.Configured) { status.Text = "请先在接口设置中填写 API 密钥。"; return; }
        request = new CancellationTokenSource(); translate.IsEnabled = false; cancel.Visibility = Visibility.Visible; original.IsReadOnly = true; status.Text = "正在翻译…可随时取消。";
        var provider = controller.Store.Settings.Provider;
        try { var result = await controller.Ai.Translate(text, request.Token); request.Token.ThrowIfCancellationRequested(); output.Text = result; controller.Store.AddTranslation(new TranslationRecord { Original = text, Translated = result, Provider = provider }); status.Text = "翻译完成 · " + provider + " · " + controller.Store.Settings.TargetLanguage; RebuildHistory(); }
        catch (Exception ex) { status.Text = AiService.Friendly(ex); }
        finally { request.Dispose(); request = null; translate.IsEnabled = true; cancel.Visibility = Visibility.Collapsed; original.IsReadOnly = false; }
    }
    private void RebuildHistory()
    {
        historyItems.Children.Clear();
        if (controller.Store.Translations.Count == 0) { historyItems.Children.Add(Theme.T("完成一次翻译后，最近记录会出现在这里。", 11, Theme.Muted)); return; }
        foreach (var record in controller.Store.Translations.Take(5))
        {
            var label = record.Original.Length > 70 ? record.Original[..70] + "…" : record.Original; var button = Theme.Button(label, () => { if (Busy) return; original.Text = record.Original; output.Text = record.Translated; status.Text = "历史记录 · " + record.Provider + " · " + record.At.ToString("MM/dd HH:mm"); }); button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 7, 0, 0); historyItems.Children.Add(button);
        }
    }
}
