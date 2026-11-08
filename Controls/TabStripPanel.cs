using System.Windows;
using System.Windows.Controls;

namespace Waypoint.Controls;

/// <summary>Lays tabs out in a row, sharing the available width between them like a browser.</summary>
public sealed class TabStripPanel : Panel
{
    public double MaxItemWidth { get; set; } = 220;
    public double MinItemWidth { get; set; } = 80;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = ItemWidth(availableSize.Width);
        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(width, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        double total = width * InternalChildren.Count;
        return new Size(double.IsInfinity(availableSize.Width) ? total : Math.Min(total, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = ItemWidth(finalSize.Width);
        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        return finalSize;
    }

    double ItemWidth(double available)
    {
        int count = InternalChildren.Count;
        if (count == 0)
            return 0;
        if (double.IsInfinity(available))
            return MaxItemWidth;
        return Math.Max(MinItemWidth, Math.Min(MaxItemWidth, available / count));
    }
}
