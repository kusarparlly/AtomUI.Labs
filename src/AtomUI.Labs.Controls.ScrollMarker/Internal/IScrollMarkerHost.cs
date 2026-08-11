using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal interface IScrollMarkerHost
{
    Orientation Orientation { get; }

    ScrollMarkerPlacement NavigatorPlacement { get; }

    ScrollMarkerDisplayMode NavigatorDisplayMode { get; }

    double NavigatorSpacing { get; }

    Thickness NavigatorMargin { get; }

    IBrush? NavigatorBackground { get; }

    IBrush? NavigatorBorderBrush { get; }

    Thickness NavigatorBorderThickness { get; }

    CornerRadius NavigatorCornerRadius { get; }

    Thickness NavigatorPadding { get; }

    int MaxVisibleMarkerCount { get; }

    double MarkerSlotExtent { get; }

    ControlTheme? ItemContainerTheme { get; }

    double AnchorOffset { get; }

    bool IsNavigatorVisible { get; }
}

internal static class ScrollMarkerValueSanitizer
{
    internal const double DefaultNavigatorSpacing = 8d;
    internal const double DefaultMarkerSlotExtent = 24d;
    internal const double DefaultMarkerSize = 8d;

    internal static double NonNegative(double value, double fallback = 0d)
    {
        return double.IsFinite(value) && value >= 0d ? value : fallback;
    }

    internal static double Positive(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0d ? value : fallback;
    }

    internal static Thickness NonNegative(Thickness value)
    {
        return new Thickness(
            NonNegative(value.Left),
            NonNegative(value.Top),
            NonNegative(value.Right),
            NonNegative(value.Bottom));
    }

    internal static CornerRadius NonNegative(CornerRadius value)
    {
        return new CornerRadius(
            NonNegative(value.TopLeft),
            NonNegative(value.TopRight),
            NonNegative(value.BottomRight),
            NonNegative(value.BottomLeft));
    }
}
