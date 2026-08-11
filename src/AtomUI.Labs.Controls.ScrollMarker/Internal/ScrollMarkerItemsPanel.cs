using Avalonia.Controls;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal sealed class ScrollMarkerItemsPanel : VirtualizingStackPanel
{
    internal const double DefaultCacheLength = 0.5d;

    public ScrollMarkerItemsPanel()
    {
        CacheLength = DefaultCacheLength;
    }
}
