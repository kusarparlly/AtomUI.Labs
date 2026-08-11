using Avalonia.Controls;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal sealed class ScrollMarkerTrackPanel : VirtualizingStackPanel
{
    internal const double DefaultCacheLength = 0.5d;

    public ScrollMarkerTrackPanel()
    {
        CacheLength = DefaultCacheLength;
        UseLayoutRounding = false;
    }
}
