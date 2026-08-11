using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace AtomUI.Labs.Controls.ScrollMarker;

public partial class ScrollMarkerView
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<ScrollMarkerView, Orientation>(nameof(Orientation), Orientation.Vertical);

    public static readonly StyledProperty<ScrollMarkerPlacement> NavigatorPlacementProperty =
        AvaloniaProperty.Register<ScrollMarkerView, ScrollMarkerPlacement>(
            nameof(NavigatorPlacement),
            ScrollMarkerPlacement.End);

    public static readonly StyledProperty<ScrollMarkerDisplayMode> NavigatorDisplayModeProperty =
        AvaloniaProperty.Register<ScrollMarkerView, ScrollMarkerDisplayMode>(
            nameof(NavigatorDisplayMode),
            ScrollMarkerDisplayMode.Inline);

    public static readonly StyledProperty<double> NavigatorSpacingProperty =
        AvaloniaProperty.Register<ScrollMarkerView, double>(
            nameof(NavigatorSpacing),
            ScrollMarkerValueSanitizer.DefaultNavigatorSpacing);

    public static readonly StyledProperty<Thickness> NavigatorMarginProperty =
        AvaloniaProperty.Register<ScrollMarkerView, Thickness>(nameof(NavigatorMargin));

    public static readonly StyledProperty<IBrush?> NavigatorBackgroundProperty =
        AvaloniaProperty.Register<ScrollMarkerView, IBrush?>(nameof(NavigatorBackground));

    public static readonly StyledProperty<IBrush?> NavigatorBorderBrushProperty =
        AvaloniaProperty.Register<ScrollMarkerView, IBrush?>(nameof(NavigatorBorderBrush));

    public static readonly StyledProperty<Thickness> NavigatorBorderThicknessProperty =
        AvaloniaProperty.Register<ScrollMarkerView, Thickness>(nameof(NavigatorBorderThickness));

    public static readonly StyledProperty<CornerRadius> NavigatorCornerRadiusProperty =
        AvaloniaProperty.Register<ScrollMarkerView, CornerRadius>(nameof(NavigatorCornerRadius));

    public static readonly StyledProperty<Thickness> NavigatorPaddingProperty =
        AvaloniaProperty.Register<ScrollMarkerView, Thickness>(nameof(NavigatorPadding));

    public static readonly StyledProperty<int> MaxVisibleMarkerCountProperty =
        AvaloniaProperty.Register<ScrollMarkerView, int>(
            nameof(MaxVisibleMarkerCount),
            int.MaxValue,
            validate: value => value >= 1);

    public static readonly StyledProperty<double> MarkerSlotExtentProperty =
        AvaloniaProperty.Register<ScrollMarkerView, double>(
            nameof(MarkerSlotExtent),
            ScrollMarkerValueSanitizer.DefaultMarkerSlotExtent);

    public static readonly StyledProperty<ControlTheme?> ItemContainerThemeProperty =
        AvaloniaProperty.Register<ScrollMarkerView, ControlTheme?>(nameof(ItemContainerTheme));

    public static readonly StyledProperty<double> AnchorOffsetProperty =
        AvaloniaProperty.Register<ScrollMarkerView, double>(nameof(AnchorOffset));

    public static readonly StyledProperty<ScrollBarVisibility> MainScrollBarVisibilityProperty =
        AvaloniaProperty.Register<ScrollMarkerView, ScrollBarVisibility>(
            nameof(MainScrollBarVisibility),
            ScrollBarVisibility.Auto);

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public ScrollMarkerPlacement NavigatorPlacement
    {
        get => GetValue(NavigatorPlacementProperty);
        set => SetValue(NavigatorPlacementProperty, value);
    }

    public ScrollMarkerDisplayMode NavigatorDisplayMode
    {
        get => GetValue(NavigatorDisplayModeProperty);
        set => SetValue(NavigatorDisplayModeProperty, value);
    }

    public double NavigatorSpacing
    {
        get => GetValue(NavigatorSpacingProperty);
        set => SetValue(NavigatorSpacingProperty, value);
    }

    public Thickness NavigatorMargin
    {
        get => GetValue(NavigatorMarginProperty);
        set => SetValue(NavigatorMarginProperty, value);
    }

    public IBrush? NavigatorBackground
    {
        get => GetValue(NavigatorBackgroundProperty);
        set => SetValue(NavigatorBackgroundProperty, value);
    }

    public IBrush? NavigatorBorderBrush
    {
        get => GetValue(NavigatorBorderBrushProperty);
        set => SetValue(NavigatorBorderBrushProperty, value);
    }

    public Thickness NavigatorBorderThickness
    {
        get => GetValue(NavigatorBorderThicknessProperty);
        set => SetValue(NavigatorBorderThicknessProperty, value);
    }

    public CornerRadius NavigatorCornerRadius
    {
        get => GetValue(NavigatorCornerRadiusProperty);
        set => SetValue(NavigatorCornerRadiusProperty, value);
    }

    public Thickness NavigatorPadding
    {
        get => GetValue(NavigatorPaddingProperty);
        set => SetValue(NavigatorPaddingProperty, value);
    }

    public int MaxVisibleMarkerCount
    {
        get => GetValue(MaxVisibleMarkerCountProperty);
        set => SetValue(MaxVisibleMarkerCountProperty, value);
    }

    public double MarkerSlotExtent
    {
        get => GetValue(MarkerSlotExtentProperty);
        set => SetValue(MarkerSlotExtentProperty, value);
    }

    public ControlTheme? ItemContainerTheme
    {
        get => GetValue(ItemContainerThemeProperty);
        set => SetValue(ItemContainerThemeProperty, value);
    }

    public double AnchorOffset
    {
        get => GetValue(AnchorOffsetProperty);
        set => SetValue(AnchorOffsetProperty, value);
    }

    public ScrollBarVisibility MainScrollBarVisibility
    {
        get => GetValue(MainScrollBarVisibilityProperty);
        set => SetValue(MainScrollBarVisibilityProperty, value);
    }
}
