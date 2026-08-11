using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal sealed class ScrollMarkerViewPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return default;
        }

        var content = Children[0];
        var navigator = Children.Count > 1 ? Children[1] : null;
        var host = TemplatedParent as IScrollMarkerHost;
        var navigatorVisible = navigator is not null && host?.IsNavigatorVisible == true;

        if (!navigatorVisible)
        {
            navigator?.Measure(default);
            content.Measure(availableSize);
            return content.DesiredSize;
        }

        navigator!.Measure(availableSize);
        var navigatorWidth = Math.Max(navigator.DesiredSize.Width, navigator.MinWidth);
        var navigatorHeight = Math.Max(navigator.DesiredSize.Height, navigator.MinHeight);
        if (host!.NavigatorDisplayMode == ScrollMarkerDisplayMode.Overlay)
        {
            content.Measure(availableSize);
            return new Size(
                Math.Max(content.DesiredSize.Width, navigatorWidth),
                Math.Max(content.DesiredSize.Height, navigatorHeight));
        }

        var spacing = ScrollMarkerValueSanitizer.NonNegative(host.NavigatorSpacing);
        var contentAvailable = host.Orientation == Orientation.Vertical
            ? new Size(Math.Max(0d, availableSize.Width - navigatorWidth - spacing), availableSize.Height)
            : new Size(availableSize.Width, Math.Max(0d, availableSize.Height - navigatorHeight - spacing));
        content.Measure(contentAvailable);

        return host.Orientation == Orientation.Vertical
            ? new Size(content.DesiredSize.Width + spacing + navigatorWidth,
                Math.Max(content.DesiredSize.Height, navigatorHeight))
            : new Size(Math.Max(content.DesiredSize.Width, navigatorWidth),
                content.DesiredSize.Height + spacing + navigatorHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var content = Children[0];
        var navigator = Children.Count > 1 ? Children[1] : null;
        var host = TemplatedParent as IScrollMarkerHost;
        var navigatorVisible = navigator is not null && host?.IsNavigatorVisible == true;

        if (!navigatorVisible)
        {
            content.Arrange(new Rect(finalSize));
            navigator?.Arrange(default);
            return finalSize;
        }

        var spacing = host!.NavigatorDisplayMode == ScrollMarkerDisplayMode.Inline
            ? ScrollMarkerValueSanitizer.NonNegative(host.NavigatorSpacing)
            : 0d;
        var overlay = host.NavigatorDisplayMode == ScrollMarkerDisplayMode.Overlay;

        if (host.Orientation == Orientation.Vertical)
        {
            var navigatorWidth = Math.Min(
                Math.Max(navigator!.DesiredSize.Width, navigator.MinWidth),
                finalSize.Width);
            var contentWidth = overlay ? finalSize.Width : Math.Max(0d, finalSize.Width - navigatorWidth - spacing);
            var navigatorX = host.NavigatorPlacement == ScrollMarkerPlacement.Start
                ? 0d
                : finalSize.Width - navigatorWidth;
            var contentX = !overlay && host.NavigatorPlacement == ScrollMarkerPlacement.Start
                ? navigatorWidth + spacing
                : 0d;
            content.Arrange(new Rect(contentX, 0d, contentWidth, finalSize.Height));
            navigator.Arrange(new Rect(navigatorX, 0d, navigatorWidth, finalSize.Height));
        }
        else
        {
            var navigatorHeight = Math.Min(
                Math.Max(navigator!.DesiredSize.Height, navigator.MinHeight),
                finalSize.Height);
            var contentHeight = overlay ? finalSize.Height : Math.Max(0d, finalSize.Height - navigatorHeight - spacing);
            var navigatorY = host.NavigatorPlacement == ScrollMarkerPlacement.Start
                ? 0d
                : finalSize.Height - navigatorHeight;
            var contentY = !overlay && host.NavigatorPlacement == ScrollMarkerPlacement.Start
                ? navigatorHeight + spacing
                : 0d;
            content.Arrange(new Rect(0d, contentY, finalSize.Width, contentHeight));
            navigator.Arrange(new Rect(0d, navigatorY, finalSize.Width, navigatorHeight));
        }

        return finalSize;
    }
}
