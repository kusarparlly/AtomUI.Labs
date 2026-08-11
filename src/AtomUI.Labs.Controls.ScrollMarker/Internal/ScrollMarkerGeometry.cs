using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal static class ScrollMarkerGeometry
{
    internal static bool TryGetLayoutStart(
        Control element,
        Control ancestor,
        Orientation orientation,
        out double start)
    {
        start = 0d;
        Visual? current = element;
        while (current is not null && !ReferenceEquals(current, ancestor))
        {
            var bounds = current.Bounds;
            start += orientation == Orientation.Vertical ? bounds.Y : bounds.X;
            current = current.GetVisualParent();
        }

        if (!ReferenceEquals(current, ancestor) || !double.IsFinite(start))
        {
            start = 0d;
            return false;
        }

        return true;
    }
}
