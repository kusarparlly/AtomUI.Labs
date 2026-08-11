using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Mixins;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AtomUI.Labs.Controls.ScrollMarker;

public class ScrollMarkerItem : ContentControl, ISelectable
{
    public static readonly StyledProperty<bool> IsSelectedProperty =
        SelectingItemsControl.IsSelectedProperty.AddOwner<ScrollMarkerItem>();

    public static readonly StyledProperty<ScrollMarkerShape> ShapeProperty =
        AvaloniaProperty.Register<ScrollMarkerItem, ScrollMarkerShape>(nameof(Shape), ScrollMarkerShape.Circle);

    public static readonly StyledProperty<double> MarkerSizeProperty =
        AvaloniaProperty.Register<ScrollMarkerItem, double>(
            nameof(MarkerSize),
            ScrollMarkerValueSanitizer.DefaultMarkerSize);

    public static readonly RoutedEvent<RoutedEventArgs> InvokedEvent =
        RoutedEvent.Register<ScrollMarkerItem, RoutedEventArgs>(nameof(Invoked), RoutingStrategies.Bubble);

    private MarkerDescriptor? _descriptor;
    private bool _restoringMarkerSize;

    static ScrollMarkerItem()
    {
        SelectableMixin.Attach<ScrollMarkerItem>(IsSelectedProperty);
        PressedMixin.Attach<ScrollMarkerItem>();
        FocusableProperty.OverrideDefaultValue<ScrollMarkerItem>(true);
        AffectsMeasure<ScrollMarkerItem>(MarkerSizeProperty);
    }

    public ScrollMarkerItem()
    {
        UseLayoutRounding = false;
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public ScrollMarkerShape Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public double MarkerSize
    {
        get => GetValue(MarkerSizeProperty);
        set => SetValue(MarkerSizeProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? Invoked
    {
        add => AddHandler(InvokedEvent, value);
        remove => RemoveHandler(InvokedEvent, value);
    }

    internal MarkerDescriptor? Descriptor => _descriptor;

    internal void Prepare(MarkerDescriptor descriptor, int index, double slotExtent)
    {
        _descriptor = descriptor;
        DataContext = descriptor.Source;
        ToolTip.SetTip(this, descriptor.Label);
        AutomationProperties.SetName(this, descriptor.Label);
        Tag = index;

        var effectiveSlotExtent = ScrollMarkerValueSanitizer.Positive(
            slotExtent,
            ScrollMarkerValueSanitizer.DefaultMarkerSlotExtent);
        MinWidth = ScrollMarkerValueSanitizer.DefaultMarkerSlotExtent;
        MinHeight = ScrollMarkerValueSanitizer.DefaultMarkerSlotExtent;
        Width = double.NaN;
        Height = effectiveSlotExtent;
    }

    internal void SetOrientation(Avalonia.Layout.Orientation orientation, double slotExtent)
    {
        var effectiveSlotExtent = ScrollMarkerValueSanitizer.Positive(
            slotExtent,
            ScrollMarkerValueSanitizer.DefaultMarkerSlotExtent);

        if (orientation == Avalonia.Layout.Orientation.Vertical)
        {
            Width = double.NaN;
            Height = effectiveSlotExtent;
        }
        else
        {
            Width = effectiveSlotExtent;
            Height = double.NaN;
        }
    }

    internal void Clear()
    {
        _descriptor = null;
        DataContext = null;
        ToolTip.SetTip(this, null);
        AutomationProperties.SetName(this, null);
        Tag = null;
        Theme = null;
        IsSelected = false;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (e.InitialPressMouseButton == MouseButton.Left &&
            new Rect(Bounds.Size).Contains(e.GetPosition(this)))
        {
            RaiseEvent(new RoutedEventArgs(InvokedEvent, this));
            e.Handled = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkerSizeProperty && !_restoringMarkerSize &&
            (!double.IsFinite(MarkerSize) || MarkerSize <= 0d))
        {
            _restoringMarkerSize = true;
            try
            {
                SetCurrentValue(MarkerSizeProperty, ScrollMarkerValueSanitizer.DefaultMarkerSize);
            }
            finally
            {
                _restoringMarkerSize = false;
            }
        }
    }
}
