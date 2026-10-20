using Windows.Foundation;
namespace Shift.App.Services;

/// <summary>Wraps real controls without horizontal clipping at narrow widths or large text sizes.</summary>
public sealed class FlowPanel : Panel
{
    public double Spacing { get; set; } = 8;
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 10000 : availableSize.Width;
        double x = 0, y = 0, rowHeight = 0, maxWidth = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                maxWidth = Math.Max(maxWidth, x - Spacing);
                y += rowHeight + Spacing;
                x = 0;
                rowHeight = 0;
            }
            x += size.Width + Spacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return new Size(Math.Min(width, Math.Max(maxWidth, Math.Max(0, x - Spacing))), y + rowHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            var width = Math.Min(size.Width, finalSize.Width);
            if (x > 0 && x + width > finalSize.Width)
            {
                y += rowHeight + Spacing;
                x = 0;
                rowHeight = 0;
            }
            child.Arrange(new Rect(x, y, width, size.Height));
            x += width + Spacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return finalSize;
    }
}
