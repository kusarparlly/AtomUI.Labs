using Avalonia;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;

namespace AtomUI.Labs.Controls.ScrollMarker;

public partial class ScrollMarkerItemsView
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        ScrollMarkerView.OrientationProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<ScrollMarkerPlacement> NavigatorPlacementProperty =
        ScrollMarkerView.NavigatorPlacementProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<ScrollMarkerDisplayMode> NavigatorDisplayModeProperty =
        ScrollMarkerView.NavigatorDisplayModeProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<double> NavigatorSpacingProperty =
        ScrollMarkerView.NavigatorSpacingProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<Thickness> NavigatorMarginProperty =
        ScrollMarkerView.NavigatorMarginProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<IBrush?> NavigatorBackgroundProperty =
        ScrollMarkerView.NavigatorBackgroundProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<IBrush?> NavigatorBorderBrushProperty =
        ScrollMarkerView.NavigatorBorderBrushProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<Thickness> NavigatorBorderThicknessProperty =
        ScrollMarkerView.NavigatorBorderThicknessProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<CornerRadius> NavigatorCornerRadiusProperty =
        ScrollMarkerView.NavigatorCornerRadiusProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<Thickness> NavigatorPaddingProperty =
        ScrollMarkerView.NavigatorPaddingProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<int> MaxVisibleMarkerCountProperty =
        ScrollMarkerView.MaxVisibleMarkerCountProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<double> MarkerSlotExtentProperty =
        ScrollMarkerView.MarkerSlotExtentProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<double> AnchorOffsetProperty =
        ScrollMarkerView.AnchorOffsetProperty.AddOwner<ScrollMarkerItemsView>();

    public static readonly StyledProperty<BindingBase?> AnchorKeyBindingProperty =
        AvaloniaProperty.Register<ScrollMarkerItemsView, BindingBase?>(nameof(AnchorKeyBinding));

    public static readonly StyledProperty<BindingBase?> LabelBindingProperty =
        AvaloniaProperty.Register<ScrollMarkerItemsView, BindingBase?>(nameof(LabelBinding));

    public static readonly StyledProperty<BindingBase?> MarkerThemeBindingProperty =
        AvaloniaProperty.Register<ScrollMarkerItemsView, BindingBase?>(nameof(MarkerThemeBinding));

    public static readonly StyledProperty<ScrollMarkerContentEndFollowMode> ContentEndFollowModeProperty =
        AvaloniaProperty.Register<ScrollMarkerItemsView, ScrollMarkerContentEndFollowMode>(
            nameof(ContentEndFollowMode),
            ScrollMarkerContentEndFollowMode.Automatic);

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

    public double AnchorOffset
    {
        get => GetValue(AnchorOffsetProperty);
        set => SetValue(AnchorOffsetProperty, value);
    }

    [AssignBinding]
    [InheritDataTypeFromItems(nameof(ItemsSource))]
    public BindingBase? AnchorKeyBinding
    {
        get => GetValue(AnchorKeyBindingProperty);
        set => SetValue(AnchorKeyBindingProperty, value);
    }

    [AssignBinding]
    [InheritDataTypeFromItems(nameof(ItemsSource))]
    public BindingBase? LabelBinding
    {
        get => GetValue(LabelBindingProperty);
        set => SetValue(LabelBindingProperty, value);
    }

    [AssignBinding]
    [InheritDataTypeFromItems(nameof(ItemsSource))]
    public BindingBase? MarkerThemeBinding
    {
        get => GetValue(MarkerThemeBindingProperty);
        set => SetValue(MarkerThemeBindingProperty, value);
    }

    public ScrollMarkerContentEndFollowMode ContentEndFollowMode
    {
        get => GetValue(ContentEndFollowModeProperty);
        set => SetValue(ContentEndFollowModeProperty, value);
    }
}
