using System;
using System.Drawing;
using System.Linq;

namespace Luma.Core;

internal enum DockEdge { None, Left, Right, Top, Bottom }
internal readonly record struct DockPlacement(double X,double Y,DockEdge Edge);
internal readonly record struct CapsulePlacement(double X,double Y,double Width,bool OpensRight);

internal static class BubbleGeometry
{
    public const double CollapsedWidth=92;
    public const double ExpandedWidth=312;
    public const double Peek=14;
    public static DockPlacement Snap(Rectangle bounds,Rectangle area,double scale)
    {
        var inset=2*scale;
        var x=Math.Clamp((double)bounds.Left,area.Left-inset,Math.Max(area.Left-inset,area.Right-bounds.Width+inset));
        var y=Math.Clamp((double)bounds.Top,area.Top-inset,Math.Max(area.Top-inset,area.Bottom-bounds.Height+inset));
        var options=new[]{(DockEdge.Left,Math.Abs(x-(area.Left-inset))),(DockEdge.Right,Math.Abs(x+bounds.Width-(area.Right+inset))),(DockEdge.Top,Math.Abs(y-(area.Top-inset))),(DockEdge.Bottom,Math.Abs(y+bounds.Height-(area.Bottom+inset)))};
        var closest=options.OrderBy(p=>p.Item2).First();
        if(closest.Item2>34*scale)return new(x,y,DockEdge.None);
        return closest.Item1 switch {
            DockEdge.Left=>new(area.Left-inset,y,DockEdge.Left),DockEdge.Right=>new(area.Right-bounds.Width+inset,y,DockEdge.Right),
            DockEdge.Top=>new(x,area.Top-inset,DockEdge.Top),_=>new(x,area.Bottom-bounds.Height+inset,DockEdge.Bottom)
        };
    }
    public static (double X,double Y) Hidden(DockPlacement home,Rectangle area,double scale)
        =>home.Edge switch {
            DockEdge.Left=>(area.Left-(82-Peek)*scale,home.Y),DockEdge.Right=>(area.Right-(10+Peek)*scale,home.Y),
            DockEdge.Top=>(home.X,area.Top-(82-Peek)*scale),DockEdge.Bottom=>(home.X,area.Bottom-(10+Peek)*scale),_ =>(home.X,home.Y)
        };
    public static CapsulePlacement Expand(DockPlacement home,Rectangle area,double scale)
    {
        var width=Math.Min(ExpandedWidth,Math.Max(CollapsedWidth,(area.Width-16*scale)/scale));
        var right=home.X+width*scale<=area.Right+2*scale;
        var x=right?home.X:home.X-(width-CollapsedWidth)*scale;
        x=Math.Clamp(x,area.Left-2*scale,Math.Max(area.Left-2*scale,area.Right-width*scale+2*scale));
        return new(x,home.Y,width,right);
    }
}
