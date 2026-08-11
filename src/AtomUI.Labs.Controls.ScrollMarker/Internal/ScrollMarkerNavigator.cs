using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AtomUIButton = AtomUI.Desktop.Controls.Button;
using AtomUIScrollViewer = AtomUI.Desktop.Controls.ScrollViewer;
using AtomUI.Icons.AntDesign;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal sealed class ScrollMarkerNavigator : SelectingItemsControl
{
    private static readonly TimeSpan MarkerTrackMotionDuration = TimeSpan.FromMilliseconds(160d);
    private const double LayoutComparisonTolerance = 1d;

    internal const string PartLayout = "PART_NavigatorLayout";
    internal const string PartScrollViewer = "PART_NavigatorScrollViewer";
    internal const string PartItemsPresenter = "PART_ItemsPresenter";
    internal const string PartPreviousMarkerButton = "PART_PreviousMarkerButton";
    internal const string PartNextMarkerButton = "PART_NextMarkerButton";

    internal static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<ScrollMarkerNavigator, Orientation>(nameof(Orientation), Orientation.Vertical);

    internal static readonly StyledProperty<double> MarkerSlotExtentProperty =
        AvaloniaProperty.Register<ScrollMarkerNavigator, double>(
            nameof(MarkerSlotExtent),
            ScrollMarkerValueSanitizer.DefaultMarkerSlotExtent);

    private static readonly StyledProperty<double> MarkerTrackTranslationProperty =
        AvaloniaProperty.Register<ScrollMarkerNavigator, double>(nameof(MarkerTrackTranslation));

    private static readonly FuncTemplate<Panel?> DefaultPanel = new(() => new ScrollMarkerTrackPanel());
    private ScrollMarkerCoordinator? _coordinator;
    private ControlTheme? _itemContainerTheme;
    private AtomUIScrollViewer? _scrollViewer;
    private ItemsPresenter? _itemsPresenter;
    private TranslateTransform? _trackTransform;
    private ScrollMarkerNavigatorPanel? _layoutPanel;
    private AtomUIButton? _previousMarkerButton;
    private AtomUIButton? _nextMarkerButton;
    private int _maxVisibleMarkerCount = int.MaxValue;
    private int _effectiveVisibleMarkerCount;
    private CancellationTokenSource? _markerTrackMotionCancellation;
    private MarkerTrackMotionRequest? _pendingMarkerTrackMotion;
    private long _markerTrackMotionGeneration;
    private int _animatedAdjacentNavigationIndex = -1;
    private bool _hasMarkerOverflow;
    private double _effectiveMarkerSlotExtent = ScrollMarkerValueSanitizer.DefaultMarkerSlotExtent;

    static ScrollMarkerNavigator()
    {
        ItemsPanelProperty.OverrideDefaultValue<ScrollMarkerNavigator>(DefaultPanel);
        ScrollMarkerItem.InvokedEvent.AddClassHandler<ScrollMarkerNavigator>(
            (navigator, args) => navigator.OnItemInvoked(args));
    }

    public ScrollMarkerNavigator()
    {
        LayoutUpdated += OnLayoutUpdated;
    }

    internal Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    internal double MarkerSlotExtent
    {
        get => GetValue(MarkerSlotExtentProperty);
        set => SetValue(MarkerSlotExtentProperty, value);
    }

    internal int EffectiveVisibleMarkerCount => _effectiveVisibleMarkerCount;

    internal double EffectiveMarkerSlotExtent => _effectiveMarkerSlotExtent;

    internal bool HasMarkerOverflow => _hasMarkerOverflow;

    internal void Connect(ScrollMarkerCoordinator coordinator)
    {
        if (ReferenceEquals(_coordinator, coordinator))
        {
            return;
        }

        Disconnect();
        _coordinator = coordinator;
        ItemsSource = coordinator.Descriptors;
        coordinator.ActiveIndexChanged += OnActiveIndexChanged;
        coordinator.NavigatorFollowModeChanged += OnNavigatorFollowModeChanged;
        SelectedIndex = coordinator.ActiveIndex;
        IsVisible = coordinator.Descriptors.Count > 1;
        UpdateMarkerLayoutMetrics();
    }

    internal void Disconnect()
    {
        CancelMarkerTrackMotion();
        if (_coordinator is not null)
        {
            _coordinator.ActiveIndexChanged -= OnActiveIndexChanged;
            _coordinator.NavigatorFollowModeChanged -= OnNavigatorFollowModeChanged;
        }

        _coordinator = null;
        ItemsSource = null;
        SelectedIndex = -1;
        UpdateMarkerLayoutMetrics();
    }

    internal void Configure(IScrollMarkerHost host)
    {
        Orientation = host.Orientation;
        MarkerSlotExtent = ScrollMarkerValueSanitizer.Positive(
            host.MarkerSlotExtent,
            ScrollMarkerValueSanitizer.DefaultMarkerSlotExtent);
        _maxVisibleMarkerCount = Math.Max(1, host.MaxVisibleMarkerCount);
        _itemContainerTheme = host.ItemContainerTheme;
        Margin = ScrollMarkerValueSanitizer.NonNegative(host.NavigatorMargin);
        Background = host.NavigatorBackground;
        BorderBrush = host.NavigatorBorderBrush;
        BorderThickness = ScrollMarkerValueSanitizer.NonNegative(host.NavigatorBorderThickness);
        CornerRadius = ScrollMarkerValueSanitizer.NonNegative(host.NavigatorCornerRadius);
        Padding = ScrollMarkerValueSanitizer.NonNegative(host.NavigatorPadding);
        IsVisible = host.IsNavigatorVisible;
        ConfigureNavigationButtons();
        _layoutPanel?.InvalidateMeasure();
        UpdateMarkerLayoutMetrics();

        if (ItemsPanelRoot is ScrollMarkerTrackPanel panel)
        {
            panel.Orientation = Orientation;
        }

        foreach (var child in GetRealizedContainers())
        {
            if (child is ScrollMarkerItem item)
            {
                item.SetOrientation(Orientation, _effectiveMarkerSlotExtent);
            }
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        CancelMarkerTrackMotion();
        if (_itemsPresenter is not null && ReferenceEquals(_itemsPresenter.RenderTransform, _trackTransform))
        {
            _itemsPresenter.RenderTransform = null;
        }

        if (_scrollViewer is not null)
        {
            _scrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
            _scrollViewer.RemoveHandler(InputElement.PointerPressedEvent, OnNavigatorPointerPressed);
        }

        if (_previousMarkerButton is not null)
        {
            _previousMarkerButton.Click -= OnPreviousMarkerClick;
        }

        if (_nextMarkerButton is not null)
        {
            _nextMarkerButton.Click -= OnNextMarkerClick;
        }

        base.OnApplyTemplate(e);
        _layoutPanel = e.NameScope.Find<ScrollMarkerNavigatorPanel>(PartLayout)
            ?? throw new InvalidOperationException($"ScrollMarkerNavigator template must provide {PartLayout}.");
        _scrollViewer = e.NameScope.Find<AtomUIScrollViewer>(PartScrollViewer)
            ?? throw new InvalidOperationException($"ScrollMarkerNavigator template must provide {PartScrollViewer}.");
        _itemsPresenter = e.NameScope.Find<ItemsPresenter>(PartItemsPresenter)
            ?? throw new InvalidOperationException($"ScrollMarkerNavigator template must provide {PartItemsPresenter}.");
        _trackTransform = new TranslateTransform();
        _itemsPresenter.RenderTransform = _trackTransform;
        _previousMarkerButton = e.NameScope.Find<AtomUIButton>(PartPreviousMarkerButton)
            ?? throw new InvalidOperationException($"ScrollMarkerNavigator template must provide {PartPreviousMarkerButton}.");
        _nextMarkerButton = e.NameScope.Find<AtomUIButton>(PartNextMarkerButton)
            ?? throw new InvalidOperationException($"ScrollMarkerNavigator template must provide {PartNextMarkerButton}.");

        _scrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnPointerWheelChanged,
            RoutingStrategies.Tunnel,
            true);
        _scrollViewer.AddHandler(
            InputElement.PointerPressedEvent,
            OnNavigatorPointerPressed,
            RoutingStrategies.Tunnel,
            true);
        _previousMarkerButton.Click += OnPreviousMarkerClick;
        _nextMarkerButton.Click += OnNextMarkerClick;
        ConfigureNavigationButtons();
        ConfigureScrollViewer();
        UpdateMarkerLayoutMetrics();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OrientationProperty)
        {
            CancelMarkerTrackMotion();
            ConfigureNavigationButtons();
            ConfigureScrollViewer();
            _layoutPanel?.InvalidateMeasure();
            ApplyTrackMainTranslation(MarkerTrackTranslation);
        }
        else if (change.Property == MarkerTrackTranslationProperty)
        {
            ApplyTrackMainTranslation(MarkerTrackTranslation);
        }
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new ScrollMarkerItem();
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = typeof(ScrollMarkerItem);
        return item is not ScrollMarkerItem;
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is not ScrollMarkerItem markerItem || item is not MarkerDescriptor descriptor)
        {
            throw new InvalidOperationException("ScrollMarkerNavigator received an invalid marker container or descriptor.");
        }

        markerItem.Prepare(descriptor, index, _effectiveMarkerSlotExtent);
        markerItem.SetOrientation(Orientation, _effectiveMarkerSlotExtent);
        markerItem.Theme = descriptor.MarkerTheme ?? _itemContainerTheme;
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        if (container is ScrollMarkerItem markerItem)
        {
            markerItem.Clear();
        }

        base.ClearContainerForItemOverride(container);
    }

    private void OnItemInvoked(Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (args.Source is ScrollMarkerItem { Tag: int index })
        {
            CancelMarkerTrackMotion();
            _coordinator?.ResumeNavigatorFollow();
            _coordinator?.RequestNavigation(index);
        }
    }

    private void OnActiveIndexChanged(object? sender, int index)
    {
        var previousIndex = SelectedIndex;
        var animateAdjacentNavigation = index == _animatedAdjacentNavigationIndex;
        _animatedAdjacentNavigationIndex = -1;
        SelectedIndex = index;
        IsVisible = _coordinator?.Descriptors.Count > 1;
        UpdateNavigationButtonStates();
        if (index >= 0 && _coordinator?.NavigatorFollowMode == NavigatorFollowMode.Follow)
        {
            FollowActiveMarker(
                animate: Math.Abs(index - previousIndex) == 1 &&
                         (_coordinator.SelectionMode == MarkerSelectionMode.Automatic ||
                          animateAdjacentNavigation),
                allowExplicitMotion: animateAdjacentNavigation);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_coordinator is null || _coordinator.Descriptors.Count == 0)
        {
            return;
        }

        var focusedIndex = GetRealizedContainers()
            .OfType<ScrollMarkerItem>()
            .FirstOrDefault(item => item.IsKeyboardFocusWithin)?.Tag as int? ?? _coordinator.ActiveIndex;
        var targetIndex = focusedIndex;
        var navigationKey = false;
        if ((Orientation == Orientation.Vertical && e.Key == Key.Up) ||
            (Orientation == Orientation.Horizontal && e.Key == Key.Left))
        {
            targetIndex = Math.Max(0, focusedIndex - 1);
            navigationKey = true;
        }
        else if ((Orientation == Orientation.Vertical && e.Key == Key.Down) ||
                 (Orientation == Orientation.Horizontal && e.Key == Key.Right))
        {
            targetIndex = Math.Min(_coordinator.Descriptors.Count - 1, focusedIndex + 1);
            navigationKey = true;
        }
        else if (e.Key == Key.Home)
        {
            targetIndex = 0;
            navigationKey = true;
        }
        else if (e.Key == Key.End)
        {
            targetIndex = _coordinator.Descriptors.Count - 1;
            navigationKey = true;
        }
        else if (e.Key == Key.Escape)
        {
            _coordinator.ResumeNavigatorFollow();
            FollowActiveMarker();
            e.Handled = true;
            return;
        }
        else if (e.Key is Key.Enter or Key.Space && focusedIndex >= 0)
        {
            _coordinator.ResumeNavigatorFollow();
            _coordinator.RequestNavigation(focusedIndex);
            e.Handled = true;
            return;
        }

        if (navigationKey)
        {
            _coordinator.BeginNavigatorBrowse();
            ScrollIntoView(targetIndex);
            Dispatcher.UIThread.Post(
                () => ContainerFromIndex(targetIndex)?.Focus(),
                DispatcherPriority.Loaded);
            e.Handled = true;
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        CancelMarkerTrackMotion();
        _coordinator?.BeginNavigatorBrowse();

        if (Orientation != Orientation.Horizontal ||
            _scrollViewer is null ||
            !_hasMarkerOverflow)
        {
            return;
        }

        var wheelDelta = Math.Abs(e.Delta.X) >= Math.Abs(e.Delta.Y)
            ? e.Delta.X
            : e.Delta.Y;
        if (Math.Abs(wheelDelta) <= double.Epsilon)
        {
            return;
        }

        var maximumOffset = Math.Max(
            0d,
            _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width);
        var targetOffset = Math.Clamp(
            _scrollViewer.Offset.X - wheelDelta * _effectiveMarkerSlotExtent,
            0d,
            maximumOffset);
        _scrollViewer.Offset = new Vector(targetOffset, 0d);
        e.Handled = true;
    }

    private void OnNavigatorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CancelMarkerTrackMotion();
    }

    private void OnPreviousMarkerClick(object? sender, RoutedEventArgs e)
    {
        MoveToAdjacentMarker(-1);
    }

    private void OnNextMarkerClick(object? sender, RoutedEventArgs e)
    {
        MoveToAdjacentMarker(1);
    }

    private void OnNavigatorFollowModeChanged(object? sender, NavigatorFollowMode mode)
    {
        if (mode == NavigatorFollowMode.Follow)
        {
            FollowActiveMarker();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (ItemsPanelRoot is ScrollMarkerTrackPanel panel && panel.Orientation != Orientation)
        {
            panel.Orientation = Orientation;
        }

        UpdateMarkerLayoutMetrics();
        TryStartPendingMarkerTrackMotion();
    }

    private void UpdateMarkerLayoutMetrics()
    {
        var minimum = Math.Max(
            ScrollMarkerValueSanitizer.DefaultMarkerSlotExtent,
            ScrollMarkerValueSanitizer.Positive(
                MarkerSlotExtent,
                ScrollMarkerValueSanitizer.DefaultMarkerSlotExtent));
        var count = _coordinator?.Descriptors.Count ?? 0;
        var layoutExtent = _layoutPanel is null
            ? 0d
            : Orientation == Orientation.Vertical
                ? _layoutPanel.Bounds.Height
                : _layoutPanel.Bounds.Width;
        if (!double.IsFinite(layoutExtent) || layoutExtent <= 0d)
        {
            _effectiveVisibleMarkerCount = count > 0 ? 1 : 0;
            UpdateNavigationButtonStates();
            return;
        }

        var capacityWithoutButtons = CalculateGeometryCapacity(layoutExtent, minimum);
        var overflow = count > Math.Min(_maxVisibleMarkerCount, capacityWithoutButtons);
        if (SetNavigationButtonsVisible(overflow))
        {
            _layoutPanel?.InvalidateMeasure();
            return;
        }

        var viewportExtent = _scrollViewer is null
            ? 0d
            : Orientation == Orientation.Vertical
                ? _scrollViewer.Viewport.Height
                : _scrollViewer.Viewport.Width;
        if (!double.IsFinite(viewportExtent) || viewportExtent <= 0d)
        {
            viewportExtent = layoutExtent;
        }

        var capacityByGeometry = CalculateGeometryCapacity(viewportExtent, minimum);
        var effectiveVisibleCount = count > 0
            ? Math.Min(count, Math.Min(_maxVisibleMarkerCount, capacityByGeometry))
            : 0;
        var effectiveSlotExtent = effectiveVisibleCount > 0
            ? Math.Max(minimum, viewportExtent / effectiveVisibleCount)
            : minimum;
        var capacityChanged = effectiveVisibleCount != _effectiveVisibleMarkerCount;
        var extentChanged = Math.Abs(effectiveSlotExtent - _effectiveMarkerSlotExtent) > 0.01d;
        _effectiveVisibleMarkerCount = effectiveVisibleCount;
        _effectiveMarkerSlotExtent = effectiveSlotExtent;

        if (capacityChanged || extentChanged)
        {
            CancelMarkerTrackMotion();
            foreach (var child in GetRealizedContainers())
            {
                if (child is ScrollMarkerItem markerItem)
                {
                    markerItem.SetOrientation(Orientation, effectiveSlotExtent);
                }
            }

            ItemsPanelRoot?.InvalidateMeasure();
        }

        UpdateNavigationButtonStates();
    }

    private void FollowActiveMarker(bool animate = false, bool allowExplicitMotion = false)
    {
        if (_coordinator?.ActiveIndex is >= 0 and var index)
        {
            if (animate && ShouldAnimateMarkerTrackMotion(index))
            {
                BeginMarkerTrackMotion(index, allowExplicitMotion);
                return;
            }

            CancelMarkerTrackMotion();
            ScrollIntoView(index);
        }
    }

    private bool ShouldAnimateMarkerTrackMotion(int index)
    {
        if (_scrollViewer is null ||
            !_hasMarkerOverflow ||
            index < 0 ||
            !double.IsFinite(_effectiveMarkerSlotExtent) ||
            _effectiveMarkerSlotExtent <= 0d)
        {
            return false;
        }

        var viewportExtent = Orientation == Orientation.Vertical
            ? _scrollViewer.Viewport.Height
            : _scrollViewer.Viewport.Width;
        var currentOffset = Orientation == Orientation.Vertical
            ? _scrollViewer.Offset.Y
            : _scrollViewer.Offset.X;
        if (!double.IsFinite(viewportExtent) || viewportExtent <= 0d ||
            !double.IsFinite(currentOffset))
        {
            return false;
        }

        var markerStart = index * _effectiveMarkerSlotExtent;
        var markerEnd = markerStart + _effectiveMarkerSlotExtent;
        var viewportEnd = currentOffset + viewportExtent;
        return markerStart < currentOffset - LayoutComparisonTolerance ||
               markerEnd > viewportEnd + LayoutComparisonTolerance;
    }

    private void BeginMarkerTrackMotion(int index, bool allowExplicitMotion)
    {
        if (_scrollViewer is null || _trackTransform is null)
        {
            return;
        }

        var previousOffset = GetNavigatorMainOffset();
        var carriedTranslation = GetTrackMainTranslation();
        CancelMarkerTrackMotion(carriedTranslation);
        var generation = _markerTrackMotionGeneration;
        _pendingMarkerTrackMotion = new MarkerTrackMotionRequest(
            generation,
            index,
            previousOffset,
            carriedTranslation,
            allowExplicitMotion);
        ScrollIntoView(index);
        Dispatcher.UIThread.Post(
            () => TryStartPendingMarkerTrackMotion(finalAttempt: true),
            DispatcherPriority.Loaded);
    }

    private void TryStartPendingMarkerTrackMotion(bool finalAttempt = false)
    {
        if (_pendingMarkerTrackMotion is not { } request ||
            request.Generation != _markerTrackMotionGeneration)
        {
            return;
        }

        if (_coordinator is null ||
            _coordinator.ActiveIndex != request.Index ||
            (!request.AllowExplicitMotion &&
             _coordinator.SelectionMode != MarkerSelectionMode.Automatic) ||
            _coordinator.NavigatorFollowMode != NavigatorFollowMode.Follow)
        {
            CancelMarkerTrackMotion();
            return;
        }

        var offsetDelta = GetNavigatorMainOffset() - request.PreviousOffset;
        if (!double.IsFinite(offsetDelta) || Math.Abs(offsetDelta) <= 0.01d)
        {
            if (finalAttempt)
            {
                CancelMarkerTrackMotion();
            }

            return;
        }

        _pendingMarkerTrackMotion = null;
        StartMarkerTrackMotion(request.CarriedTranslation + offsetDelta, request.Generation);
    }

    private void StartMarkerTrackMotion(double startTranslation, long generation)
    {
        if (_trackTransform is null ||
            generation != _markerTrackMotionGeneration ||
            !double.IsFinite(startTranslation) ||
            Math.Abs(startTranslation) <= 0.01d)
        {
            return;
        }

        SetTrackMainTranslation(0d);
        var animation = new Animation
        {
            Duration = MarkerTrackMotionDuration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(MarkerTrackTranslationProperty, startTranslation) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(MarkerTrackTranslationProperty, 0d) }
                }
            }
        };

        var cancellation = new CancellationTokenSource();
        _markerTrackMotionCancellation = cancellation;
        Dispatcher.UIThread.InvokeAsync(
            async () => await RunMarkerTrackMotionAsync(animation, cancellation));
    }

    private async Task RunMarkerTrackMotionAsync(
        Animation animation,
        CancellationTokenSource cancellation)
    {
        try
        {
            await animation.RunAsync(this, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_markerTrackMotionCancellation, cancellation))
            {
                _markerTrackMotionCancellation = null;
                SetTrackMainTranslation(0d);
            }

            cancellation.Dispose();
        }
    }

    private void CancelMarkerTrackMotion(double preservedTranslation = 0d)
    {
        _markerTrackMotionGeneration++;
        _animatedAdjacentNavigationIndex = -1;
        _pendingMarkerTrackMotion = null;
        var cancellation = _markerTrackMotionCancellation;
        _markerTrackMotionCancellation = null;
        cancellation?.Cancel();
        SetTrackMainTranslation(preservedTranslation);
    }

    private double GetNavigatorMainOffset()
    {
        if (_scrollViewer is null)
        {
            return 0d;
        }

        return Orientation == Orientation.Vertical
            ? _scrollViewer.Offset.Y
            : _scrollViewer.Offset.X;
    }

    private double GetTrackMainTranslation()
    {
        return MarkerTrackTranslation;
    }

    private void SetTrackMainTranslation(double value)
    {
        SetCurrentValue(MarkerTrackTranslationProperty, value);
    }

    private void ApplyTrackMainTranslation(double value)
    {
        if (_trackTransform is null)
        {
            return;
        }

        if (Orientation == Orientation.Vertical)
        {
            _trackTransform.SetCurrentValue(TranslateTransform.XProperty, 0d);
            _trackTransform.SetCurrentValue(TranslateTransform.YProperty, value);
        }
        else
        {
            _trackTransform.SetCurrentValue(TranslateTransform.XProperty, value);
            _trackTransform.SetCurrentValue(TranslateTransform.YProperty, 0d);
        }
    }

    private double MarkerTrackTranslation => GetValue(MarkerTrackTranslationProperty);

    private void ConfigureScrollViewer()
    {
        if (_scrollViewer is null)
        {
            return;
        }

        if (Orientation == Orientation.Vertical)
        {
            _scrollViewer.SetCurrentValue(
                Avalonia.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty,
                ScrollBarVisibility.Disabled);
            _scrollViewer.SetCurrentValue(
                Avalonia.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty,
                ScrollBarVisibility.Hidden);
        }

        else
        {
            _scrollViewer.SetCurrentValue(
                Avalonia.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty,
                ScrollBarVisibility.Hidden);
            _scrollViewer.SetCurrentValue(
                Avalonia.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty,
                ScrollBarVisibility.Disabled);
        }

        _scrollViewer.SetCurrentValue(
            Avalonia.Controls.ScrollViewer.IsScrollChainingEnabledProperty,
            false);

        if (ItemsPanelRoot is ScrollMarkerTrackPanel panel)
        {
            panel.Orientation = Orientation;
        }

        _layoutPanel?.InvalidateMeasure();
    }

    private void ConfigureNavigationButtons()
    {
        if (_previousMarkerButton is null || _nextMarkerButton is null)
        {
            return;
        }

        // AtomUI Button computes :icon-only when Content changes. Force that state transition
        // after assigning the runtime icon so the IconPresenter owns the complete centered slot.
        _previousMarkerButton.Content = string.Empty;
        _nextMarkerButton.Content = string.Empty;
        _previousMarkerButton.Icon = Orientation == Orientation.Vertical
            ? new UpOutlined()
            : new LeftOutlined();
        _nextMarkerButton.Icon = Orientation == Orientation.Vertical
            ? new DownOutlined()
            : new RightOutlined();
        _previousMarkerButton.Content = null;
        _nextMarkerButton.Content = null;
        AutomationProperties.SetName(_previousMarkerButton, "Previous marker");
        AutomationProperties.SetName(_nextMarkerButton, "Next marker");
        ToolTip.SetTip(_previousMarkerButton, "Previous marker");
        ToolTip.SetTip(_nextMarkerButton, "Next marker");
    }

    private bool SetNavigationButtonsVisible(bool visible)
    {
        if (_hasMarkerOverflow == visible &&
            (_previousMarkerButton is null || _previousMarkerButton.IsVisible == visible) &&
            (_nextMarkerButton is null || _nextMarkerButton.IsVisible == visible))
        {
            return false;
        }

        _hasMarkerOverflow = visible;
        if (_previousMarkerButton is not null)
        {
            _previousMarkerButton.IsVisible = visible;
        }

        if (_nextMarkerButton is not null)
        {
            _nextMarkerButton.IsVisible = visible;
        }

        return true;
    }

    private void UpdateNavigationButtonStates()
    {
        if (_previousMarkerButton is null || _nextMarkerButton is null)
        {
            return;
        }

        var count = _coordinator?.Descriptors.Count ?? 0;
        var activeIndex = _coordinator?.ActiveIndex ?? -1;
        if (!_hasMarkerOverflow || count == 0 || activeIndex < 0)
        {
            _previousMarkerButton.IsEnabled = false;
            _nextMarkerButton.IsEnabled = false;
            return;
        }

        _previousMarkerButton.IsEnabled = activeIndex > 0;
        _nextMarkerButton.IsEnabled = activeIndex < count - 1;
    }

    private void MoveToAdjacentMarker(int delta)
    {
        var count = _coordinator?.Descriptors.Count ?? 0;
        var activeIndex = _coordinator?.ActiveIndex ?? -1;
        if (!_hasMarkerOverflow || count == 0 || activeIndex < 0)
        {
            return;
        }

        var targetIndex = Math.Clamp(activeIndex + delta, 0, count - 1);
        if (targetIndex == activeIndex)
        {
            return;
        }

        CancelMarkerTrackMotion();
        _coordinator?.ResumeNavigatorFollow();
        _animatedAdjacentNavigationIndex = targetIndex;
        _coordinator?.RequestNavigation(targetIndex);
    }

    private static int CalculateGeometryCapacity(double viewportExtent, double minimumSlotExtent)
    {
        if (!double.IsFinite(viewportExtent) || viewportExtent <= 0d)
        {
            return 1;
        }

        var capacity = Math.Floor(viewportExtent / minimumSlotExtent);
        return Math.Max(1, (int)Math.Min(int.MaxValue, capacity));
    }

    private sealed record MarkerTrackMotionRequest(
        long Generation,
        int Index,
        double PreviousOffset,
        double CarriedTranslation,
        bool AllowExplicitMotion);
}
