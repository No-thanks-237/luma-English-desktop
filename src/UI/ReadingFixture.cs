using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Luma.Services;

namespace Luma.UI;

internal static class ReadingFixture
{
    public static Window Create()
    {
        var w = new Window { Title = "Luma · 取词验收页", Width = 850, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Theme.B("#F3F4EC") };
        var p = new StackPanel { Margin = new Thickness(45) }; p.Children.Add(Theme.T("取词验收页", 22, Theme.Ink, true)); Theme.Space(p, 12);
        p.Children.Add(Theme.T("文字直取测试：把鼠标放在 philosophy 上，使用查词快捷键。", 12, Theme.Muted)); Theme.Space(p, 10);
        var text = new TextBox { Text = "philosophy", FontSize = 34, IsReadOnly = true, FontFamily = Theme.Serif, BorderThickness = new Thickness(0) }; p.Children.Add(text); Theme.Space(p, 30);
        p.Children.Add(Theme.T("图片 OCR 测试：下面的英文已栅格化，可取词，也可框选。", 12, Theme.Muted)); Theme.Space(p, 12);
        using var bitmap = new Bitmap(1200, 210); using (var g = Graphics.FromImage(bitmap)) { g.Clear(System.Drawing.Color.White); using var f = new Font("Georgia", 34); g.DrawString("Serendipity makes life beautiful.", f, System.Drawing.Brushes.Black, 20, 65); }
        p.Children.Add(new System.Windows.Controls.Image { Source = SelectionWindow.ImageSource(bitmap), Stretch = Stretch.Uniform, MaxHeight = 160 }); Theme.Space(p, 15);
        p.Children.Add(Theme.T("本页是验收工具，可关闭，不影响助手。", 11, Theme.Muted)); w.Content = p; return w;
    }
}
