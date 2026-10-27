using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Luma.Core;

namespace Luma.UI;

/// <summary>The layered HWND keeps a fixed size. Only the glass surface and its clip animate.</summary>
internal sealed class BubbleWindow : Window
{
    private readonly Controller controller;
    private readonly Canvas root=new();
    private readonly OrbVisual orb=new();
    private readonly Border capsule;
    private readonly StackPanel commands=new(){Orientation=Orientation.Horizontal,Width=198,Height=68};
    private readonly DispatcherTimer idle=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private readonly DispatcherTimer motion=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private readonly Stopwatch clock=new();
    private DockPlacement home;
    private System.Drawing.Rectangle area,screenBounds;
    private double scale=1,anchorX,anchorY,visualWidth=92;
    private bool opensRight=true;
    private DateTime lastInteraction=DateTime.UtcNow;
    private bool initialized,pressed,dragging,expanded,hidden,animating,closed;
    private Point dragStart;
    private double dragX,dragY;
    private double fromX,fromY,toX,toY,fromWidth,toWidth,fromAlpha,toAlpha,fromCommands,toCommands;
    private int duration;
    private Action? completed;
    private double OrbOffset=>opensRight?0:BubbleGeometry.ExpandedWidth-92;
    private double SurfaceOffset=>opensRight?0:BubbleGeometry.ExpandedWidth-visualWidth;
    internal bool IsExpanded=>expanded;
    internal bool IsEdgeHidden=>hidden;
    internal bool IsAnimating=>animating;
    internal double VisualWidth=>visualWidth;
    internal int CommandCount=>commands.Children.Count;
    internal DockEdge DockedEdge=>home.Edge;
    internal Point OrbScreenCenter=>orb.PointToScreen(new Point(36,36));
    internal void SetExpandedForAudit(bool value,bool animate=false)=>SetExpanded(value,animate);
    internal void RevealForAudit()=>Reveal(false);
    internal void DockAndHideForAudit(DockEdge edge)
    {
        CollapseImmediate();RefreshArea();Native.GetCursorPos(out var pointer);var w=(int)(92*scale);var x=area.Left+area.Width/3;var y=area.Top+area.Height/3;
        if(Math.Abs(pointer.Y-y)<140*scale)y=area.Top+area.Height*2/3;if(Math.Abs(pointer.X-x)<140*scale)x=area.Left+area.Width*2/3;
        var bounds=edge switch {DockEdge.Left=>new System.Drawing.Rectangle(area.Left,y,w,w),DockEdge.Right=>new System.Drawing.Rectangle(area.Right-w,y,w,w),DockEdge.Top=>new System.Drawing.Rectangle(x,area.Top,w,w),_=>new System.Drawing.Rectangle(x,area.Bottom-w,w,w)};
        home=BubbleGeometry.Snap(bounds,area,scale);ConfigureDirection();Move(home.X,home.Y);lastInteraction=DateTime.UtcNow.AddSeconds(-3);Idle();
    }

    public BubbleWindow(Controller controller)
    {
        this.controller=controller;Title="Luma · 玻璃胶囊";Width=BubbleGeometry.ExpandedWidth;Height=92;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;WindowStartupLocation=WindowStartupLocation.Manual;
        Native.TransparentOverlay(this);Cursor=Cursors.Hand;
        capsule=new Border{Height=72,Width=72,CornerRadius=new CornerRadius(36),Opacity=0,Background=new LinearGradientBrush(new GradientStopCollection{new((Color)ColorConverter.ConvertFromString("#ECFFFFFF"),0),new((Color)ColorConverter.ConvertFromString("#D6EAF2FF"),.55),new((Color)ColorConverter.ConvertFromString("#BBC9DCFA"),1)},80),BorderThickness=new Thickness(1),BorderBrush=new LinearGradientBrush(Colors.White,(Color)ColorConverter.ConvertFromString("#8FB1C8ED"),90),Effect=new DropShadowEffect{BlurRadius=14,ShadowDepth=4,Opacity=.15,Color=Color.FromRgb(58,77,114)}};
        root.Children.Add(capsule);commands.Opacity=0;commands.Visibility=Visibility.Hidden;commands.IsHitTestVisible=false;
        AddCommand("查词","\uE721","打开查词面板",()=>controller.ShowMain("home"));
        AddCommand("翻译","\uE7A8","框选翻译 · "+controller.Store.Settings.CaptureHotkey,controller.BeginCapture);
        AddCommand("单词本","\uE82D","打开单词本",()=>controller.ShowMain("words"));root.Children.Add(commands);
        orb.Effect=new DropShadowEffect{BlurRadius=12,ShadowDepth=3,Opacity=.13,Color=Color.FromRgb(58,77,114)};root.Children.Add(orb);Content=root;
        AutomationProperties.SetName(orb,"Luma 悬浮球，单击展开三个按钮，拖动贴边");orb.ToolTip="单击展开胶囊 · 拖动贴边\n贴边闲置后隐藏，鼠标靠近唤出";
        var prefs=controller.Store.Settings;var hasPosition=prefs.BubblePositionSaved||(prefs.BubbleX>=0&&prefs.BubbleY>=0);
        Left=hasPosition?prefs.BubbleX:SystemParameters.WorkArea.Left+14;Top=hasPosition?prefs.BubbleY:SystemParameters.WorkArea.Bottom-190;
        ApplySurface();
        Loaded+=(_,_)=>{var bounds=HostBounds();anchorX=bounds.Left;anchorY=bounds.Top;initialized=true;RefreshArea();Snap(false,false);RefreshState();idle.Start();};
        DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(initialized&&!pressed){CollapseImmediate();RefreshArea();Snap(false,false);}});
        orb.MouseLeftButtonDown+=OnPress;MouseMove+=OnMove;MouseLeftButtonUp+=OnRelease;
        orb.MouseRightButtonUp+=(_,e)=>{ToggleMenu();e.Handled=true;};
        MouseEnter+=(_,_)=>{lastInteraction=DateTime.UtcNow;if(hidden)Reveal(true);};
        idle.Tick+=(_,_)=>Idle();motion.Tick+=(_,_)=>AnimateFrame();
        IsVisibleChanged+=(_,_)=>{if(!IsVisible&&initialized){CollapseImmediate();idle.Stop();}else if(IsVisible&&initialized){lastInteraction=DateTime.UtcNow;idle.Start();}};
        Closed+=(_,_)=>{closed=true;idle.Stop();motion.Stop();completed=null;controller.BubbleExpansionChanged(false);};
    }
    private void AddCommand(string label,string glyph,string tooltip,Action action)
    {
        var content=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};var icon=Theme.I(glyph,19,Theme.Accent);icon.HorizontalAlignment=HorizontalAlignment.Center;content.Children.Add(icon);var text=Theme.T(label,12,Theme.Ink,true);text.Margin=new Thickness(0,5,0,0);text.HorizontalAlignment=HorizontalAlignment.Center;content.Children.Add(text);
        var button=Theme.Button(label,()=>Perform(action));button.Content=content;button.Width=60;button.Height=60;button.Padding=new Thickness(3);button.Margin=new Thickness(3,0,3,0);button.Background=Brushes.Transparent;button.BorderBrush=Brushes.Transparent;button.ToolTip=tooltip;commands.Children.Add(button);
    }
    private void Perform(Action action){if(!animating)SetExpanded(false,true,action);}
    private void OnPress(object sender,MouseButtonEventArgs e)
    {
        if(hidden){Reveal(false);e.Handled=true;return;}
        lastInteraction=DateTime.UtcNow;dragStart=PointToScreen(e.GetPosition(this));dragX=anchorX;dragY=anchorY;pressed=true;dragging=false;CaptureMouse();e.Handled=true;
    }
    private void OnMove(object sender,MouseEventArgs e)
    {
        if(!pressed)return;var point=PointToScreen(e.GetPosition(this));var dx=point.X-dragStart.X;var dy=point.Y-dragStart.Y;
        if(!dragging&&Math.Abs(dx)+Math.Abs(dy)<5*scale)return;
        if(!dragging){CollapseImmediate();dragging=true;}
        Move(dragX+dx,dragY+dy);lastInteraction=DateTime.UtcNow;
    }
    private void OnRelease(object sender,MouseButtonEventArgs e)
    {
        if(!pressed)return;pressed=false;ReleaseMouseCapture();lastInteraction=DateTime.UtcNow;
        if(dragging){RefreshArea();Snap(true,true);}else ToggleMenu();e.Handled=true;
    }
    public void RefreshState(){orb.Refresh(controller.Store.Settings.Enabled);if(initialized&&!controller.Store.Settings.BubbleAutoHide&&hidden)Reveal(true);}
    public void ToggleMenu(){if(hidden)Reveal(false);SetExpanded(!expanded,true);}
    public void CloseMenu(){if(expanded)SetExpanded(false,true);}
    private void SetExpanded(bool value,bool animate,Action? after=null)
    {
        if(!initialized||closed){after?.Invoke();return;}
        if(value==expanded&&!animating){after?.Invoke();return;}
        lastInteraction=DateTime.UtcNow;if(hidden)Reveal(false);expanded=value;controller.BubbleExpansionChanged(value);commands.Visibility=Visibility.Visible;commands.IsHitTestVisible=value;
        if(value){RefreshArea();ConfigureDirection();StartMotion(home.X,home.Y,BubbleGeometry.Expand(home,area,scale).Width,1,1,animate?230:0,after);}
        else StartMotion(home.X,home.Y,92,0,0,animate?190:0,()=>{commands.Visibility=Visibility.Hidden;after?.Invoke();});
    }
    private void ConfigureDirection(){opensRight=BubbleGeometry.Expand(home,area,scale).OpensRight;ApplySurface();}
    private void CollapseImmediate()
    {
        if(!initialized)return;motion.Stop();animating=false;completed=null;expanded=false;hidden=false;visualWidth=92;capsule.Opacity=0;commands.Opacity=0;commands.Visibility=Visibility.Hidden;commands.IsHitTestVisible=false;controller.BubbleExpansionChanged(false);Move(home.X,home.Y);
    }
    private void RefreshArea(){scale=VisualTreeHelper.GetDpi(this).DpiScaleX;var screen=System.Windows.Forms.Screen.FromRectangle(OrbBounds());area=screen.WorkingArea;screenBounds=screen.Bounds;}
    private void Snap(bool animate,bool save)
    {
        home=BubbleGeometry.Snap(OrbBounds(),area,scale);ConfigureDirection();StartMotion(home.X,home.Y,92,0,0,animate?170:0,()=>{if(save)SavePosition();});
    }
    private void SavePosition(){controller.Store.Settings.BubbleX=Left+OrbOffset;controller.Store.Settings.BubbleY=Top;controller.Store.Settings.BubblePositionSaved=true;controller.Store.SaveSettings();}
    private void Idle()
    {
        if(!initialized||closed||!IsVisible||pressed||animating)return;
        Native.GetCursorPos(out var point);var bounds=VisualBounds();bounds.Inflate(3,3);var inside=bounds.Contains(point.X,point.Y)&&screenBounds.Contains(point.X,point.Y);
        if(hidden){if(inside)Reveal(true);return;}if(inside){lastInteraction=DateTime.UtcNow;return;}
        var elapsed=(DateTime.UtcNow-lastInteraction).TotalMilliseconds;
        if(expanded){if(elapsed>850||Native.Down(0x01))SetExpanded(false,true);return;}
        if(home.Edge!=DockEdge.None&&controller.Store.Settings.BubbleAutoHide&&elapsed>1400){var target=BubbleGeometry.Hidden(home,screenBounds,scale);hidden=true;StartMotion(target.X,target.Y,92,0,0,230);}
    }
    private void Reveal(bool animate){hidden=false;lastInteraction=DateTime.UtcNow;StartMotion(home.X,home.Y,92,0,0,animate?150:0);}
    private void StartMotion(double x,double y,double width,double alpha,double commandAlpha,int milliseconds,Action? after=null)
    {
        motion.Stop();fromX=anchorX;fromY=anchorY;toX=x;toY=y;fromWidth=visualWidth;toWidth=width;fromAlpha=capsule.Opacity;toAlpha=alpha;fromCommands=commands.Opacity;toCommands=commandAlpha;completed=after;duration=Theme.MotionEnabled?milliseconds:0;animating=true;
        if(duration<=0){FinishMotion();return;}clock.Restart();motion.Start();
    }
    private void AnimateFrame()
    {
        var progress=Math.Min(1,clock.Elapsed.TotalMilliseconds/duration);var eased=1-Math.Pow(1-progress,3);
        visualWidth=fromWidth+(toWidth-fromWidth)*eased;capsule.Opacity=fromAlpha+(toAlpha-fromAlpha)*eased;commands.Opacity=fromCommands+(toCommands-fromCommands)*eased;Move(fromX+(toX-fromX)*eased,fromY+(toY-fromY)*eased);
        if(progress>=1)FinishMotion();
    }
    private void FinishMotion(){motion.Stop();visualWidth=toWidth;capsule.Opacity=toAlpha;commands.Opacity=toCommands;Move(toX,toY);animating=false;var next=completed;completed=null;next?.Invoke();}
    private System.Drawing.Rectangle HostBounds(){var handle=new WindowInteropHelper(this).Handle;Native.GetWindowRect(handle,out var r);return System.Drawing.Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);}
    private System.Drawing.Rectangle OrbBounds()=>new((int)Math.Round(anchorX),(int)Math.Round(anchorY),(int)Math.Round(92*scale),(int)Math.Round(92*scale));
    private System.Drawing.Rectangle VisualBounds()=>new((int)Math.Round(anchorX+(SurfaceOffset-OrbOffset)*scale),(int)Math.Round(anchorY),(int)Math.Ceiling(visualWidth*scale),(int)Math.Round(92*scale));
    private void ApplySurface()
    {
        capsule.Width=Math.Max(72,visualWidth-20);Canvas.SetLeft(capsule,SurfaceOffset+10);Canvas.SetTop(capsule,10);
        Canvas.SetLeft(commands,opensRight?96:12);Canvas.SetTop(commands,12);Canvas.SetLeft(orb,OrbOffset+10);Canvas.SetTop(orb,10);
        var clip=new Rect(SurfaceOffset,0,visualWidth,92);
        if(initialized){var hostX=anchorX-OrbOffset*scale;clip.Intersect(new Rect((screenBounds.Left-hostX)/scale,(screenBounds.Top-anchorY)/scale,screenBounds.Width/scale,screenBounds.Height/scale));}
        root.Clip=new RectangleGeometry(clip);
    }
    private void Move(double x,double y)
    {
        anchorX=x;anchorY=y;ApplySurface();var handle=new WindowInteropHelper(this).Handle;if(handle==IntPtr.Zero)return;
        var targetX=(int)Math.Round(x-OrbOffset*scale);var targetY=(int)Math.Round(y);var bounds=HostBounds();
        // Layered-HWND resizes and redraw-causing window regions produce edge flashes.
        if(bounds.Left!=targetX||bounds.Top!=targetY)Native.SetWindowPos(handle,new IntPtr(-1),targetX,targetY,0,0,0x0015);
    }
}
