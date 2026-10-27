using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Luma.UI;

internal static class Theme
{
    public static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    public static readonly Brush Ink = B("#252C3D"), Muted = B("#647086"), Accent = B("#526EE5"), Line = B("#E4E8F0"), Paper = B("#F2F5F8FC");
    public static readonly FontFamily Sans = new("Microsoft YaHei"), Serif = new("Georgia");
    public static bool MotionEnabled { get; set; } = SystemParameters.ClientAreaAnimation;
    public static void Install(Application app) => app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Luma;component/UI/Styles.xaml", UriKind.Relative) });
    public static void Prepare(Window window)
    {
        window.UseLayoutRounding = true; window.SnapsToDevicePixels = true; window.FontFamily = Sans; window.Foreground = Ink;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display); TextOptions.SetTextRenderingMode(window, TextRenderingMode.Grayscale);
    }
    public static TextBlock T(string text, double size = 13, Brush? color = null, bool bold = false)
    {
        var actualSize = Math.Max(12, size);
        var t = new TextBlock { Text = text, FontSize = actualSize, Foreground = color ?? Ink, FontFamily = Sans, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, LineHeight = actualSize * 1.55 };
        TextOptions.SetTextFormattingMode(t, TextFormattingMode.Display); TextOptions.SetTextRenderingMode(t, TextRenderingMode.Grayscale); return t;
    }
    public static TextBlock I(string glyph, double size = 17, Brush? color = null) => new() { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, Foreground = color ?? Ink, VerticalAlignment = VerticalAlignment.Center };
    public static Button Button(string text, Action action, bool primary = false, string? glyph = null)
    {
        var button = new Button { Background = primary ? Accent : B("#EFFFFFFF"), Foreground = primary ? Brushes.White : Ink, BorderBrush = primary ? B("#748CEE") : B("#DFE5EF") };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph != null) { var icon = I(glyph, 15, primary ? Brushes.White : Accent); icon.Margin = new Thickness(0, 0, 9, 0); row.Children.Add(icon); }
        row.Children.Add(T(text, 12, primary ? Brushes.White : Ink, true)); button.Content = row;
        AutomationProperties.SetName(button, text); button.Click += (_, _) => action(); Hover(button); return button;
    }
    public static Button IconButton(string glyph, string tooltip, Action action)
    {
        var button = new Button { Content = I(glyph, 15), ToolTip = tooltip, Padding = new Thickness(9), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, MinWidth = 34, MinHeight = 34 };
        AutomationProperties.SetName(button, tooltip); button.Click += (_, _) => action(); Hover(button); return button;
    }
    public static Border Card(UIElement child, Thickness? padding = null, Brush? fill = null) => new()
    {
        Child = child,
        Padding = padding ?? new Thickness(22),
        CornerRadius = new CornerRadius(20),
        Background = fill ?? B("#DFFFFFFF"),
        BorderBrush = new LinearGradientBrush(new GradientStopCollection { new(Colors.White, 0), new((Color)ColorConverter.ConvertFromString("#CFDCE3EF"), .65), new(Colors.White, 1) }, 80),
        BorderThickness = new Thickness(1)
    };
    public static Border Pill(string label, bool green = true) => new() { Child = T(label, 11, green ? Accent : Muted, true), CornerRadius = new CornerRadius(20), Background = B(green ? "#E8EEFF" : "#EDF0F6"), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    public static Border Rule() => new() { Height = 1, Background = Line, Margin = new Thickness(0, 15, 0, 15) };
    public static ScrollViewer Scroll(UIElement child) => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly, Padding = new Thickness(0, 0, 8, 0) };
    public static void Space(StackPanel parent, double height = 12) => parent.Children.Add(new Border { Height = height });
    public static CheckBox Toggle(bool value)
    {
        var check = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center, Cursor = System.Windows.Input.Cursors.Hand, Width = 44, Height = 28, FocusVisualStyle = null };
        var template = new ControlTemplate(typeof(CheckBox)); var track = new FrameworkElementFactory(typeof(Border)); track.Name = "Track";
        track.SetValue(Border.WidthProperty, 40.0); track.SetValue(Border.HeightProperty, 24.0); track.SetValue(Border.CornerRadiusProperty, new CornerRadius(12)); track.SetValue(Border.BackgroundProperty, B("#CED5E2"));
        var dot = new FrameworkElementFactory(typeof(Ellipse)); dot.Name = "Dot"; dot.SetValue(FrameworkElement.WidthProperty, 18.0); dot.SetValue(FrameworkElement.HeightProperty, 18.0); dot.SetValue(Shape.FillProperty, Brushes.White); dot.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left); dot.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 0, 0, 0)); dot.SetValue(UIElement.RenderTransformProperty, new TranslateTransform()); track.AppendChild(dot); template.VisualTree = track;
        var state = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true }; state.Setters.Add(new Setter(Border.BackgroundProperty, Accent, "Track")); template.Triggers.Add(state); check.Template = template;
        void Update(bool animate) { check.ApplyTemplate(); if (template.FindName("Dot", check) is Ellipse ellipse) { var transform = ellipse.RenderTransform as TranslateTransform; if (transform == null || transform.IsFrozen) { transform = new TranslateTransform(); ellipse.RenderTransform = transform; } transform.BeginAnimation(TranslateTransform.XProperty, AnimateTo(check.IsChecked == true ? 16 : 0, animate ? 180 : 0)); } }
        check.Loaded += (_, _) => Update(false); check.Checked += (_, _) => Update(true); check.Unchecked += (_, _) => Update(true); return check;
    }
    public static Grid Row(UIElement left, UIElement right)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    public static DoubleAnimation AnimateTo(double to, int milliseconds = 200) => new(to, TimeSpan.FromMilliseconds(MotionEnabled ? milliseconds : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
    public static void Enter(FrameworkElement element, int delay = 0)
    {
        if (!MotionEnabled) return; var translate = new TranslateTransform(); element.RenderTransform = translate;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = TimeSpan.FromMilliseconds(delay) });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    public static void Hover(Button button)
    {
        var scale = new ScaleTransform(1, 1); button.RenderTransform = scale; button.RenderTransformOrigin = new Point(.5, .5);
        button.MouseEnter += (_, _) => { scale.BeginAnimation(ScaleTransform.ScaleXProperty, AnimateTo(1.025, 160)); scale.BeginAnimation(ScaleTransform.ScaleYProperty, AnimateTo(1.025, 160)); };
        button.MouseLeave += (_, _) => { scale.BeginAnimation(ScaleTransform.ScaleXProperty, AnimateTo(1, 180)); scale.BeginAnimation(ScaleTransform.ScaleYProperty, AnimateTo(1, 180)); };
    }
}
