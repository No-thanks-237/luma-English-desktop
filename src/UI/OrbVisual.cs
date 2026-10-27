using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Luma.UI;

internal sealed class OrbVisual : Grid
{
    private readonly Ellipse shine;
    private readonly Ellipse status;
    private readonly ScaleTransform iconScale = new();
    public OrbVisual()
    {
        Width = 72; Height = 72; Clip = new EllipseGeometry(new Point(36, 36), 36, 36);
        Children.Add(new Ellipse
        {
            Fill = new RadialGradientBrush(new GradientStopCollection {
                new((Color)ColorConverter.ConvertFromString("#B8FFFFFF"),0),new((Color)ColorConverter.ConvertFromString("#70E8F0FF"),.45),new((Color)ColorConverter.ConvertFromString("#467B9AE2"),.82),new((Color)ColorConverter.ConvertFromString("#B6F4FAFF"),1)
            })
            { GradientOrigin = new Point(.25, .18), Center = new Point(.45, .35), RadiusX = .77, RadiusY = .77 }
        });
        Children.Add(new Ellipse { Margin = new Thickness(1), StrokeThickness = 1.3, Stroke = new LinearGradientBrush(new GradientStopCollection { new(Colors.White, 0), new((Color)ColorConverter.ConvertFromString("#28FFFFFF"), .5), new((Color)ColorConverter.ConvertFromString("#DDFFFFFF"), 1) }, 75) });
        Children.Add(new Ellipse { Margin = new Thickness(7), Stroke = Theme.B("#55FFFFFF"), StrokeThickness = .8 });
        shine = new Ellipse { Width = 42, Height = 18, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 6, 0, 0), Fill = new LinearGradientBrush(Colors.White, Colors.Transparent, 90), Opacity = .7, RenderTransform = new RotateTransform(-25, 21, 9), Effect = new BlurEffect { Radius = 3 } };
        Children.Add(shine);
        var lens = Theme.I("\uE721", 24, Theme.B("#355D9D")); lens.HorizontalAlignment = HorizontalAlignment.Center; lens.RenderTransform = iconScale; lens.RenderTransformOrigin = new Point(.5, .5); Children.Add(lens);
        var caustic = new Path { Data = Geometry.Parse("M 12,50 C 21,67 48,70 62,49"), Stroke = Theme.B("#BEFFFFFF"), StrokeThickness = 1.4, IsHitTestVisible = false };
        Children.Add(caustic);
        status = new Ellipse { Width = 8, Height = 8, Fill = Theme.Accent, Stroke = Brushes.White, StrokeThickness = 1.7, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 12) }; Children.Add(status);
        MouseEnter += (_, _) => { iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, Theme.AnimateTo(1.1)); iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, Theme.AnimateTo(1.1)); };
        MouseLeave += (_, _) => { iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, Theme.AnimateTo(1)); iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, Theme.AnimateTo(1)); };
        Loaded += (_, _) => Refresh(true); Unloaded += (_, _) => shine.BeginAnimation(OpacityProperty, null);
    }
    public void Refresh(bool enabled)
    {
        status.Fill = enabled ? Theme.Accent : Theme.B("#9AA4B6");
        shine.BeginAnimation(OpacityProperty, null);
        if (Theme.MotionEnabled) shine.BeginAnimation(OpacityProperty, new DoubleAnimation(.38, .82, TimeSpan.FromSeconds(3.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
}
