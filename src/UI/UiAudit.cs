using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Luma.Core;

namespace Luma.UI;

internal static class UiAudit
{
    public static async Task Run(Controller controller, MainWindow main, string folder)
    {
        Directory.CreateDirectory(folder); var results = new List<object>(); var failures = 0; var priorMotion = Theme.MotionEnabled; Theme.MotionEnabled = false;
        void Check(string name, Action work) { try { work(); results.Add(new { name, passed = true }); } catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.Message }); } }
        main.WindowState=WindowState.Normal;main.UpdateLayout();
        Check("title blank area is native caption",()=>{if(Native.HitTest(main,new Point(400,28))!=2)throw new Exception("Blank title cannot drag");});
        Check("title brand is native caption",()=>{if(Native.HitTest(main,new Point(55,28))!=2)throw new Exception("Brand cannot drag");});
        Check("title buttons remain client controls",()=>{if(Native.HitTest(main,new Point(main.ActualWidth-42,28))!=1)throw new Exception("Title button intercepted by caption");});
        Check("main exposes explicit exit action",()=>{if(!Children(main).OfType<Button>().Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="退出应用"))throw new Exception("Missing exit button");});
        main.WindowState=WindowState.Maximized;await main.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);main.UpdateLayout();
        Check("maximized title supports native restore drag",()=>{if(Native.HitTest(main,new Point(400,28))!=2)throw new Exception("Maximized caption cannot drag");});
        Check("maximized title buttons remain clickable",()=>{if(Native.HitTest(main,new Point(main.ActualWidth-42,28))!=1)throw new Exception("Maximized title button intercepted");});
        main.WindowState=WindowState.Normal;
        foreach (var size in new[] { new Size(1120, 790), new Size(840, 560) })
        {
            main.WindowState = WindowState.Normal; main.Width = size.Width; main.Height = size.Height;
            foreach (var page in new[] { "home", "translate", "words", "settings" })
            {
                main.Navigate(page); await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); main.UpdateLayout();
                var name = page + "-" + (int)size.Width;
                Check(name + " footer bounds", () => CheckButtons(main));
                Check(name + " input height", () => CheckInputHeights(main));
                if (main.CurrentPage is SettingsView settings)
                {
                    foreach (var tab in new[] { "general", "api", "keys", "about" })
                    {
                        settings.SelectTabForAudit(tab); await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); main.UpdateLayout(); Check(name + "-" + tab, () => CheckButtons(main)); Check(name + "-" + tab + " input height", () => CheckInputHeights(main));
                        if (size.Width == 1120 && tab == "api") Save(main, "settings-api", folder, 1.5);
                        if(size.Width==1120&&tab=="general"){Check("close behavior selector exposes both choices",()=>{var combo=Children(settings).OfType<ComboBox>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="关闭主窗口时");if(combo.Items.Count!=2)throw new Exception("Missing close behavior choices");});Save(main,"settings-general",folder,1.5);}
                        if(size.Width==1120&&tab=="about"){Check("settings exposes check for updates",()=>{if(!Children(settings).OfType<Button>().Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="检查更新"))throw new Exception("Missing update action");});Save(main,"settings-about",folder,1.5);}
                    }
                }
                if (size.Width == 1120) Save(main, name, folder, 1.5);
            }
        }
        var capsule=new BubbleWindow(controller);capsule.Show();await main.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);capsule.UpdateLayout();var sphereHandle=new System.Windows.Interop.WindowInteropHelper(capsule).Handle;
        Check("orb window uses per-pixel alpha",()=>{if(!capsule.AllowsTransparency||capsule.Background is not SolidColorBrush brush||brush.Color.A!=0)throw new Exception("Opaque backing");});
        capsule.SetExpandedForAudit(true);await main.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);capsule.UpdateLayout();
        Check("capsule uses same native window",()=>{if(new System.Windows.Interop.WindowInteropHelper(capsule).Handle!=sphereHandle)throw new Exception("New popup created");});
        Check("capsule has three commands",()=>{if(capsule.CommandCount!=3)throw new Exception("Wrong command count");CheckButtons(capsule);});Save(capsule,"capsule-expanded",folder,1.5);
        capsule.ShowMenuForAudit();await main.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);var context=capsule.MenuForAudit;context.UpdateLayout();
        Check("right click menu contains requested actions",()=>{var names=context.Items.OfType<MenuItem>().Select(System.Windows.Automation.AutomationProperties.GetName).ToArray();if(!names.SequenceEqual(new[]{"打开应用","偏好设置","退出应用"}))throw new Exception("Wrong menu actions");});
        Check("right click menu items fit the menu",()=>{foreach(var item in context.Items.OfType<MenuItem>()){var p=item.TranslatePoint(new Point(),context);if(p.X<0||p.Y<0||p.X+item.ActualWidth>context.ActualWidth+1||p.Y+item.ActualHeight>context.ActualHeight+1)throw new Exception("Menu item outside border");}});
        SaveElement(context,"context-menu",folder,1.5);context.IsOpen=false;
        ((MenuItem)context.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check("context open application action",()=>{if(!main.IsVisible)throw new Exception("Application did not reopen");});
        ((MenuItem)context.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check("context preferences action",()=>{if(main.CurrentPage is not SettingsView)throw new Exception("Preferences did not open");});
        capsule.SetExpandedForAudit(false);capsule.UpdateLayout();Check("capsule collapses to orb",()=>{if(Math.Abs(capsule.VisualWidth-92)>1||capsule.IsExpanded)throw new Exception("Not collapsed");});
        foreach(var edge in new[]{DockEdge.Left,DockEdge.Right,DockEdge.Top,DockEdge.Bottom})
        {
            capsule.DockAndHideForAudit(edge);capsule.UpdateLayout();Check("idle hides docked orb "+edge,()=>{if(capsule.DockedEdge!=edge||!capsule.IsEdgeHidden)throw new Exception("Dock/hide failed");});
            capsule.RevealForAudit();capsule.UpdateLayout();Check("edge reveal restores orb "+edge,()=>{if(capsule.IsEdgeHidden||Math.Abs(capsule.VisualWidth-92)>1)throw new Exception("Reveal failed");});
            capsule.SetExpandedForAudit(true);capsule.UpdateLayout();Check("button highlights stay inside rounded capsule "+edge,()=>CheckCommandClip(capsule));var center=capsule.OrbScreenCenter;Native.GetWindowRect(sphereHandle,out var before);string? motionError=null;Theme.MotionEnabled=true;capsule.SetExpandedForAudit(false,true);
            for(var frame=0;frame<18;frame++)
            {
                await Task.Delay(16);capsule.UpdateLayout();Native.GetWindowRect(sphereHandle,out var now);var current=capsule.OrbScreenCenter;
                if(!before.Equals(now)||Math.Abs(current.X-center.X)>.5||Math.Abs(current.Y-center.Y)>.5)motionError="Native host or orb moved during collapse";
                if(frame is 0 or 4 or 8 or 17)Save(capsule,"collapse-"+edge+"-"+frame,folder,1);
            }
            Theme.MotionEnabled=false;
            Check("animated edge collapse keeps host and orb stationary "+edge,()=>{if(motionError!=null||capsule.IsAnimating||capsule.IsExpanded||Math.Abs(capsule.VisualWidth-92)>.01)throw new Exception(motionError??"Collapse incomplete");});
            await Task.Delay(32);var transparent=capsule.OrbScreenCenter;transparent.X+=edge==DockEdge.Right?-170:170;
            Check("collapsed transparent viewport passes pointer through "+edge,()=>{if(Native.WindowFromPoint(new Native.POINT{X=(int)transparent.X,Y=(int)transparent.Y})==sphereHandle)throw new Exception("Invisible viewport intercepts pointer");});
        }
        capsule.Close();
        foreach (var query in new[] { "philosophy", "pneumonoultramicroscopicsilicovolcanoconiosis" })
        {
            var word = new WordWindow(controller, controller.Dictionary.Lookup(query), "布局验收", new System.Drawing.Point(400, 400)); word.SetPinned(); word.Show();
            foreach (var tab in new[] { "meaning", "roots", "examples" })
            {
                word.SelectTabForAudit(tab); await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); word.UpdateLayout(); Check(query + "-" + tab + " fixed actions", () => CheckButtons(word));
                if (query == "philosophy") Save(word, "word-" + tab, folder, 1.5);
            }
            word.Close();
        }
        Check("sphere has transparent corners", () =>
        {
            var orb = new OrbVisual(); orb.Measure(new Size(72, 72)); orb.Arrange(new Rect(0, 0, 72, 72)); orb.UpdateLayout(); var bitmap = new RenderTargetBitmap(144, 144, 192, 192, PixelFormats.Pbgra32); bitmap.Render(orb); var pixels = new byte[144 * 144 * 4]; bitmap.CopyPixels(pixels, 144 * 4, 0); if (pixels[3] != 0 || pixels[(143 * 144 + 143) * 4 + 3] != 0) throw new Exception("Non-transparent rectangular corners"); if (pixels[(72 * 144 + 72) * 4 + 3] == 0) throw new Exception("Sphere center is empty"); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(folder, "glass-orb.png")); encoder.Save(file);
        });
        Theme.MotionEnabled = priorMotion; main.Width = 1120; main.Height = 790; main.Navigate("home");
        File.WriteAllText(Path.Combine(folder, "ui-test-results.json"), JsonSerializer.Serialize(new { at = DateTimeOffset.Now, passed = results.Count - failures, failed = failures, tests = results }, Store.Json));
    }
    private static void CheckButtons(Window window)
    {
        foreach (var button in Children(window).OfType<Button>().Where(b => b.IsVisible && b.ActualWidth > 0 && b.ActualHeight > 0))
        {
            if (HasScrollAncestor(button, window)) continue;
            var p = button.TranslatePoint(new Point(0, 0), window);
            if (p.X < -2 || p.Y < -2 || p.X + button.ActualWidth > window.ActualWidth + 2 || p.Y + button.ActualHeight > window.ActualHeight + 2) throw new Exception("Action outside window: " + System.Windows.Automation.AutomationProperties.GetName(button));
        }
    }
    private static void CheckInputHeights(Window window)
    {
        foreach (var input in Children(window).OfType<TextBox>().Where(x => x.IsVisible && !x.IsReadOnly && !x.AcceptsReturn && x.FontSize <= 15))
            if (input.ActualHeight > 54) throw new Exception("Single-line input has doubled padding: " + System.Windows.Automation.AutomationProperties.GetName(input));
    }
    private static bool HasScrollAncestor(DependencyObject item, Window stop)
    {
        for (var parent = VisualTreeHelper.GetParent(item); parent != null && parent != stop; parent = VisualTreeHelper.GetParent(parent)) if (parent is ScrollViewer) return true; return false;
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject item)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) { var child = VisualTreeHelper.GetChild(item, i); yield return child; foreach (var descendant in Children(child)) yield return descendant; }
    }
    private static void Save(Window window, string name, string folder, double scale)
        =>SaveElement(window,name,folder,scale);
    private static void SaveElement(FrameworkElement window,string name,string folder,double scale)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale), (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(folder, name + ".png")); encoder.Save(file);
    }
    private static void CheckCommandClip(BubbleWindow window)
    {
        var panel=window.CommandsForAudit;
        if(panel.Clip is not RectangleGeometry shape||shape.RadiusX!=36)throw new Exception("No capsule clip");
        var surfaces=Children(panel).OfType<Border>().Where(b=>b.Name=="Surface").ToList();
        try
        {
            foreach(var surface in surfaces)surface.Background=Theme.Accent;
            foreach(var decoration in window.DecorationsForAudit)decoration.Visibility=Visibility.Hidden;
            window.UpdateLayout();var width=(int)Math.Ceiling(window.ActualWidth);var height=(int)Math.Ceiling(window.ActualHeight);var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
            var offset=panel.TranslatePoint(new Point(),window);var bounds=shape.Rect;bounds.Offset(offset.X,offset.Y);bounds.Inflate(1,1);var allowed=new RectangleGeometry(bounds,37,37);
            for(var y=0;y<height;y++)for(var x=0;x<width;x++)if(pixels[(y*width+x)*4+3]>20&&!allowed.FillContains(new Point(x+.5,y+.5)))throw new Exception("Rendered highlight escaped capsule corner");
        }
        finally{foreach(var surface in surfaces)surface.ClearValue(Border.BackgroundProperty);foreach(var decoration in window.DecorationsForAudit)decoration.Visibility=Visibility.Visible;}
    }
}
