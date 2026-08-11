using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal sealed class ScrollMarkerNavigatorPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return default;
        }

        var orientation = GetOrientation();
        var previous = Children.Count > 0 ? Children[0] : null;
        var viewport = Children.Count > 1 ? Children[1] : null;
        var next = Children.Count > 2 ? Children[2] : null;

        previous?.Measure(availableSize);
        next?.Measure(availableSize);

        var previousExtent = GetMainExtent(previous?.DesiredSize ?? default, orientation);
        var nextExtent = GetMainExtent(next?.DesiredSize ?? default, orientation);
        var availableMain = GetMainExtent(availableSize, orientation);
        var viewportMain = double.IsFinite(availableMain)
            ? Math.Max(0d, availableMain - previousExtent - nextExtent)
            : double.PositiveInfinity;
        var viewportAvailable = orientation == Orientation.Vertical
            ? new Size(availableSize.Width, viewportMain)
            : new Size(viewportMain, availableSize.Height);
        viewport?.Measure(viewportAvailable);

        var viewportDesired = viewport?.DesiredSize ?? default;
        if (orientation == Orientation.Vertical)
        {
            return new Size(
                Math.Max(viewportDesired.Width, Math.Max(
                    previous?.DesiredSize.Width ?? 0d,
                    next?.DesiredSize.Width ?? 0d)),
                previousExtent + viewportDesired.Height + nextExtent);
        }

        return new Size(
            previousExtent + viewportDesired.Width + nextExtent,
            Math.Max(viewportDesired.Height, Math.Max(
                previous?.DesiredSize.Height ?? 0d,
                next?.DesiredSize.Height ?? 0d)));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var orientation = GetOrientation();
        var previous = Children.Count > 0 ? Children[0] : null;
        var viewport = Children.Count > 1 ? Children[1] : null;
        var next = Children.Count > 2 ? Children[2] : null;
        var finalMain = GetMainExtent(finalSize, orientation);
        var previousExtent = Math.Min(
            finalMain,
            GetMainExtent(previous?.DesiredSize ?? default, orientation));
        var nextExtent = Math.Min(
            Math.Max(0d, finalMain - previousExtent),
            GetMainExtent(next?.DesiredSize ?? default, orientation));
        var viewportExtent = Math.Max(0d, finalMain - previousExtent - nextExtent);

        if (orientation == Orientation.Vertical)
        {
            previous?.Arrange(new Rect(0d, 0d, finalSize.Width, previousExtent));
            viewport?.Arrange(new Rect(0d, previousExtent, finalSize.Width, viewportExtent));
            next?.Arrange(new Rect(
                0d,
                previousExtent + viewportExtent,
                finalSize.Width,
                nextExtent));
        }
        else
        {
            previous?.Arrange(new Rect(0d, 0d, previousExtent, finalSize.Height));
            viewport?.Arrange(new Rect(previousExtent, 0d, viewportExtent, finalSize.Height));
            next?.Arrange(new Rect(
                previousExtent + viewportExtent,
                0d,
                nextExtent,
                finalSize.Height));
        }

        return finalSize;
    }

    private Orientation GetOrientation()
    {
        return TemplatedParent is ScrollMarkerNavigator navigator
            ? navigator.Orientation
            : Orientation.Vertical;
    }

    private static double GetMainExtent(Size size, Orientation orientation)
    {
        return orientation == Orientation.Vertical ? size.Height : size.Width;
    }
}
