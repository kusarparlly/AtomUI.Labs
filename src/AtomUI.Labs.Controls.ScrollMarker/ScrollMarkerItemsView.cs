using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using AtomUIScrollViewer = AtomUI.Desktop.Controls.ScrollViewer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AtomUI.Labs.Controls.ScrollMarker;

[TemplatePart(PartContentScrollViewer, typeof(AtomUIScrollViewer))]
[TemplatePart(PartNavigator, typeof(ScrollMarkerNavigator))]
public partial class ScrollMarkerItemsView : ItemsControl, IScrollMarkerHost
{
    internal const string PartContentScrollViewer = "PART_ContentScrollViewer";
    internal const string PartNavigator = "PART_Navigator";
    internal const double ActiveHysteresis = 4d;

    private static readonly FuncTemplate<Panel?> DefaultPanel = new(() => new ScrollMarkerItemsPanel());
    private readonly ScrollMarkerCoordinator _coordinator = new();
    private AtomUIScrollViewer? _contentScrollViewer;
    private ScrollMarkerNavigator? _navigator;
    private IEnumerable? _acceptedItemsSource;
    private bool _hasAcceptedItemsSource;
    private bool _hasProjectedInitialItems;
    private bool _isInitialized;
    private bool _isFaulted;
    private bool _isWritingOffset;
    private bool _followEndAfterNextLayout;
    private bool _isFollowingEnd = true;
    private double _lastActiveStart;
    private readonly Dictionary<int, (ScrollMarkerSectionContainer Container, int Generation)> _realizedContainers = [];
    private bool _configurationUpdateScheduled;
    private bool _hasEstablishedEndFollowState;
    private bool _hasUserScrollIntent;
    private double _lastObservedMaxOffset;
    private long _endFollowGeneration;

    static ScrollMarkerItemsView()
    {
        ItemsPanelProperty.OverrideDefaultValue<ScrollMarkerItemsView>(DefaultPanel);
        AffectsMeasure<ScrollMarkerItemsView>(
            OrientationProperty,
            NavigatorPlacementProperty,
            NavigatorDisplayModeProperty,
            NavigatorSpacingProperty,
            NavigatorMarginProperty,
            NavigatorBorderThicknessProperty,
            NavigatorPaddingProperty,
            MaxVisibleMarkerCountProperty,
            MarkerSlotExtentProperty);
    }

    public ScrollMarkerItemsView()
    {
        Items.CollectionChanged += OnItemsCollectionChanged;
        _coordinator.NavigationRequested += OnNavigationRequested;
        LayoutUpdated += OnLayoutUpdated;
    }

    bool IScrollMarkerHost.IsNavigatorVisible => IsNavigatorVisible;

    internal bool IsNavigatorVisible => _coordinator.Descriptors.Count > 1;

    internal bool IsFaulted => _isFaulted;

    internal VirtualHostState HostState { get; private set; }

    internal VirtualHostFault? HostFault { get; private set; }

    internal int HostGeneration { get; private set; }

    internal ScrollMarkerCoordinator Coordinator => _coordinator;

    internal int DescriptorCount => _coordinator.Descriptors.Count;

    internal int ConfigurationCommitCount { get; private set; }

    internal bool IsFollowingContentEnd => _isFollowingEnd;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _isInitialized = true;
        if (!_hasProjectedInitialItems)
        {
            ProjectInitialItems();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureHostIsNotNested();
        if (!_hasProjectedInitialItems)
        {
            ProjectInitialItems();
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_contentScrollViewer is not null)
        {
            _contentScrollViewer.ScrollChanged -= OnContentScrollChanged;
            _contentScrollViewer.RemoveHandler(
                InputElement.PointerWheelChangedEvent,
                OnContentPointerWheelIntent);
        }

        _navigator?.Disconnect();
        base.OnApplyTemplate(e);

        _contentScrollViewer = e.NameScope.Find<AtomUIScrollViewer>(PartContentScrollViewer)
            ?? throw new InvalidOperationException($"ScrollMarkerItemsView template must provide {PartContentScrollViewer}.");
        _navigator = e.NameScope.Find<ScrollMarkerNavigator>(PartNavigator)
            ?? throw new InvalidOperationException($"ScrollMarkerItemsView template must provide {PartNavigator}.");

        _contentScrollViewer.ScrollChanged += OnContentScrollChanged;
        _contentScrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnContentPointerWheelIntent,
            RoutingStrategies.Tunnel,
            true);
        _navigator.Connect(_coordinator);
        ApplyTemplateConfiguration();
        EnsureRequiredItemsPanel();
        UpdateActiveItemFromRealizedContainers();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            var newSource = change.NewValue as IEnumerable;
            if (!_hasAcceptedItemsSource)
            {
                _hasAcceptedItemsSource = true;
                _acceptedItemsSource = newSource;
            }
            else if (!ReferenceEquals(_acceptedItemsSource, newSource))
            {
                ThrowFaulted("Replacing ItemsSource is not supported after a Virtual Items host has accepted its source.");
            }
        }

        if (_hasProjectedInitialItems && _coordinator.Descriptors.Count > 0 &&
            (change.Property == AnchorKeyBindingProperty ||
             change.Property == LabelBindingProperty ||
             change.Property == MarkerThemeBindingProperty))
        {
            ThrowFaulted("Metadata projection bindings cannot change after descriptors have been committed.");
        }

        if (change.Property == ContentEndFollowModeProperty &&
            ContentEndFollowMode == ScrollMarkerContentEndFollowMode.Disabled)
        {
            _isFollowingEnd = false;
            _followEndAfterNextLayout = false;
            _endFollowGeneration++;
        }

        if (change.Property == OrientationProperty ||
            change.Property == NavigatorPlacementProperty ||
            change.Property == NavigatorDisplayModeProperty ||
            change.Property == NavigatorSpacingProperty ||
            change.Property == NavigatorMarginProperty ||
            change.Property == NavigatorBackgroundProperty ||
            change.Property == NavigatorBorderBrushProperty ||
            change.Property == NavigatorBorderThicknessProperty ||
            change.Property == NavigatorCornerRadiusProperty ||
            change.Property == NavigatorPaddingProperty ||
            change.Property == MaxVisibleMarkerCountProperty ||
            change.Property == MarkerSlotExtentProperty ||
             change.Property == ItemContainerThemeProperty ||
             change.Property == AnchorOffsetProperty)
        {
            ValidateLogicalConfiguration();
            ScheduleTemplateConfiguration();
        }

        if (change.Property == FlowDirectionProperty)
        {
            ValidateLogicalConfiguration();
            ScheduleTemplateConfiguration();
        }
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new ScrollMarkerSectionContainer();
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = typeof(ScrollMarkerSectionContainer);
        return true;
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (_isFaulted)
        {
            return;
        }

        if (container is not ScrollMarkerSectionContainer sectionContainer ||
            index < 0 ||
            index >= _coordinator.Descriptors.Count)
        {
            ThrowFaulted("Virtual Items container preparation received an invalid container or source index.");
            return;
        }

        sectionContainer.Prepare(item, _coordinator.Descriptors[index], index, ItemTemplate);
        _realizedContainers[index] = (sectionContainer, sectionContainer.ContainerGeneration);
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        if (container is ScrollMarkerSectionContainer sectionContainer)
        {
            if (sectionContainer.SourceIndex is var sourceIndex &&
                _realizedContainers.TryGetValue(sourceIndex, out var realized) &&
                ReferenceEquals(realized.Container, sectionContainer) &&
                realized.Generation == sectionContainer.ContainerGeneration)
            {
                _realizedContainers.Remove(sourceIndex);
            }

            sectionContainer.ClearPreparedState();
        }

        base.ClearContainerForItemOverride(container);
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isFaulted || !_isInitialized)
        {
            return;
        }

        if (!_hasProjectedInitialItems)
        {
            ProjectInitialItems();
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset &&
            _coordinator.Descriptors.Count == 0 &&
            !_hasAcceptedItemsSource &&
            ItemsSource is not null)
        {
            _hasAcceptedItemsSource = true;
            _acceptedItemsSource = ItemsSource;
            _hasProjectedInitialItems = false;
            ProjectInitialItems();
            return;
        }

        if (e.Action != NotifyCollectionChangedAction.Add ||
            e.NewItems is null ||
            e.NewStartingIndex != _coordinator.Descriptors.Count ||
            e.NewStartingIndex + e.NewItems.Count != Items.Count)
        {
            ThrowFaulted("Virtual Items mode only supports contiguous additions at the logical End.");
            return;
        }

        AppendItems(e.NewItems, e.NewStartingIndex);
    }

    private void ProjectInitialItems()
    {
        if (_isFaulted || _hasProjectedInitialItems)
        {
            return;
        }

        var staged = new List<MarkerDescriptor>(Items.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Items.Count; index++)
        {
            var descriptor = ProjectDescriptor(Items[index], index);
            if (!keys.Add(descriptor.AnchorKey))
            {
                ThrowFaulted($"Duplicate AnchorKey '{descriptor.AnchorKey}' was found in the initial ItemsSource.");
            }

            staged.Add(descriptor);
        }

        _coordinator.ReplaceAll(staged);
        _hasProjectedInitialItems = true;
        _navigator?.Configure(this);
        InvalidateMeasure();
    }

    private void AppendItems(IList newItems, int startingIndex)
    {
        var existingKeys = new HashSet<string>(
            _coordinator.Descriptors.Select(descriptor => descriptor.AnchorKey),
            StringComparer.Ordinal);
        var staged = new List<MarkerDescriptor>(newItems.Count);
        for (var relativeIndex = 0; relativeIndex < newItems.Count; relativeIndex++)
        {
            var sourceIndex = startingIndex + relativeIndex;
            var descriptor = ProjectDescriptor(newItems[relativeIndex], sourceIndex);
            if (!existingKeys.Add(descriptor.AnchorKey))
            {
                ThrowFaulted($"Duplicate AnchorKey '{descriptor.AnchorKey}' was found in an End append batch.");
            }

            staged.Add(descriptor);
        }

        _followEndAfterNextLayout =
            ContentEndFollowMode == ScrollMarkerContentEndFollowMode.Automatic && _isFollowingEnd;
        _coordinator.Append(staged);
        if (_followEndAfterNextLayout)
        {
            var generation = ++_endFollowGeneration;
            var finalIndex = _coordinator.Descriptors.Count - 1;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!_isFaulted &&
                        generation == _endFollowGeneration &&
                        _followEndAfterNextLayout &&
                        _coordinator.NavigationPhase == MarkerNavigationPhase.Idle)
                    {
                        ScrollIntoView(finalIndex);
                    }
                },
                DispatcherPriority.Loaded);
        }

        _navigator?.Configure(this);
        InvalidateMeasure();
    }

    private MarkerDescriptor ProjectDescriptor(object? item, int sourceIndex)
    {
        if (item is null)
        {
            ThrowFaulted($"ItemsSource[{sourceIndex}] is null. Virtual Items requires non-null business data items.");
        }

        if (item is Control)
        {
            ThrowFaulted(
                $"{nameof(ScrollMarkerItemsView)} ItemsSource[{sourceIndex}] has unsupported Control item type " +
                $"'{item.GetType().FullName}'. Virtual Items accepts business data only and creates controls through ItemTemplate.");
        }

        if (ItemTemplate is null)
        {
            ThrowFaulted("A non-empty Virtual Items source requires ItemTemplate.");
        }

        var anchorBinding = AnchorKeyBinding;
        if (anchorBinding is null)
        {
            ThrowFaulted("A non-empty Virtual Items source requires AnchorKeyBinding.");
        }

        using var target = new MarkerDescriptorProjectionTarget(
            item!,
            anchorBinding!,
            LabelBinding,
            MarkerThemeBinding);
        var anchorValue = target.AnchorKeyValue;
        if (anchorValue is not string)
        {
            ThrowFaulted($"AnchorKeyBinding for ItemsSource[{sourceIndex}] must produce a string.");
        }

        var anchorKey = (string)anchorValue;
        if (string.IsNullOrWhiteSpace(anchorKey) ||
            !string.Equals(anchorKey, anchorKey.Trim(), StringComparison.Ordinal))
        {
            ThrowFaulted(
                $"AnchorKeyBinding for ItemsSource[{sourceIndex}] must produce a non-empty string without leading or trailing whitespace.");
        }

        var labelValue = target.LabelValue;
        if (labelValue is not null && labelValue is not string)
        {
            ThrowFaulted($"LabelBinding for ItemsSource[{sourceIndex}] must produce string or null.");
        }

        var themeValue = target.MarkerThemeValue;
        if (themeValue is not null && themeValue is not ControlTheme)
        {
            ThrowFaulted($"MarkerThemeBinding for ItemsSource[{sourceIndex}] must produce ControlTheme or null.");
        }

        var label = labelValue as string;
        return new MarkerDescriptor(
            anchorKey,
            string.IsNullOrEmpty(label) ? anchorKey : label,
            themeValue as ControlTheme,
            sourceIndex,
            null);
    }

    [DoesNotReturn]
    private void ThrowFaulted(string message)
    {
        if (!_isFaulted)
        {
            _isFaulted = true;
            HostState = VirtualHostState.Faulted;
            HostFault = new VirtualHostFault(message);
            checked
            {
                HostGeneration++;
            }

            PseudoClasses.Set(":faulted", true);
        }

        throw new InvalidOperationException(message);
    }

    private void EnsureHostIsNotNested()
    {
        if (this.GetVisualAncestors().Any(ancestor => ancestor is ScrollMarkerView or ScrollMarkerItemsView))
        {
            ThrowFaulted("ScrollMarkerView and ScrollMarkerItemsView cannot be nested in another ScrollMarker host.");
        }
    }

    private void ApplyTemplateConfiguration()
    {
        if (_contentScrollViewer is null)
        {
            return;
        }

        ValidateLogicalConfiguration();
        ConfigurationCommitCount++;

        if (Orientation == Orientation.Vertical)
        {
            _contentScrollViewer.SetCurrentValue(
                Avalonia.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty,
                ScrollBarVisibility.Disabled);
            _contentScrollViewer.SetCurrentValue(
                Avalonia.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty,
                ScrollBarVisibility.Auto);
        }
        else
        {
            _contentScrollViewer.SetCurrentValue(
                Avalonia.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty,
                ScrollBarVisibility.Auto);
            _contentScrollViewer.SetCurrentValue(
                Avalonia.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty,
                ScrollBarVisibility.Disabled);
        }

        _contentScrollViewer.SetCurrentValue(
            Avalonia.Controls.ScrollViewer.IsScrollChainingEnabledProperty,
            true);
        _contentScrollViewer.SetCurrentValue(
            Avalonia.Controls.ScrollViewer.IsDeferredScrollingEnabledProperty,
            false);
        _contentScrollViewer.SetCurrentValue(
            Avalonia.Controls.ScrollViewer.IsScrollInertiaEnabledProperty,
            true);
        _contentScrollViewer.SetCurrentValue(
            Avalonia.Controls.ScrollViewer.BringIntoViewOnFocusChangeProperty,
            false);

        if (ItemsPanelRoot is ScrollMarkerItemsPanel itemsPanel)
        {
            itemsPanel.Orientation = Orientation;
        }

        _navigator?.Configure(this);
        InvalidateMeasure();
    }

    private void ScheduleTemplateConfiguration()
    {
        if (_contentScrollViewer is null || _configurationUpdateScheduled || _isFaulted)
        {
            return;
        }

        _configurationUpdateScheduled = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _configurationUpdateScheduled = false;
                if (!_isFaulted)
                {
                    ApplyTemplateConfiguration();
                }
            },
            DispatcherPriority.Input);
    }

    private void ValidateLogicalConfiguration()
    {
        if (Orientation == Orientation.Horizontal && FlowDirection == Avalonia.Media.FlowDirection.RightToLeft)
        {
            ThrowFaulted("Horizontal ScrollMarker supports left-to-right coordinates only in the first release.");
        }
    }

    private void EnsureRequiredItemsPanel()
    {
        if (ItemsPanelRoot is not null && ItemsPanelRoot is not ScrollMarkerItemsPanel)
        {
            ThrowFaulted(
                "ScrollMarkerItemsView requires its sealed ScrollMarkerItemsPanel; replacing the content ItemsPanel is not supported.");
        }
    }

    private void OnNavigationRequested(object? sender, ScrollMarkerNavigationRequestedEventArgs e)
    {
        if (_isFaulted || e.Index < 0 || e.Index >= _coordinator.Descriptors.Count)
        {
            return;
        }

        _isFollowingEnd =
            ContentEndFollowMode == ScrollMarkerContentEndFollowMode.Automatic &&
            e.Index == _coordinator.Descriptors.Count - 1;
        if (!_coordinator.TryTransition(e.Generation, MarkerNavigationPhase.RealizingTarget))
        {
            return;
        }

        ScrollIntoView(e.Index);
        Dispatcher.UIThread.Post(
            () => ConfirmNavigationOffset(e.Index, e.Generation, 1),
            DispatcherPriority.Loaded);
    }

    private void ConfirmNavigationOffset(int index, long generation, int realizationAttempt)
    {
        if (_isFaulted ||
            !_coordinator.IsCurrentNavigation(generation) ||
            _contentScrollViewer is null)
        {
            return;
        }

        if (!_realizedContainers.TryGetValue(index, out var realized) ||
            realized.Container.ContainerGeneration != realized.Generation)
        {
            if (realizationAttempt < 2)
            {
                ScrollIntoView(index);
                Dispatcher.UIThread.Post(
                    () => ConfirmNavigationOffset(index, generation, realizationAttempt + 1),
                    DispatcherPriority.Loaded);
            }
            else
            {
                _coordinator.CancelNavigation(
                    generation,
                    MarkerNavigationCancelReason.TargetRealizationFailed,
                    true);
            }

            return;
        }

        if (!_coordinator.TryTransition(generation, MarkerNavigationPhase.ApplyingOffset) ||
            !TryGetContainerTargetOffset(index, out var targetOffset))
        {
            _coordinator.CancelNavigation(generation, MarkerNavigationCancelReason.InvalidTarget, true);
            return;
        }

        ApplyNavigationOffset(index, generation, targetOffset, 1);
    }

    private void OnContentScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_contentScrollViewer is null)
        {
            return;
        }

        TryEstablishInitialEndFollowState();
        var mainDelta = Orientation == Orientation.Vertical ? e.OffsetDelta.Y : e.OffsetDelta.X;
        if (!_isWritingOffset && _hasUserScrollIntent && Math.Abs(mainDelta) > 0d)
        {
            if (_coordinator.CurrentRequest is { } request)
            {
                _coordinator.CancelNavigation(
                    request.Generation,
                    MarkerNavigationCancelReason.UserInterrupted,
                    true);
            }
            else
            {
                _coordinator.EnterAutomatic();
            }

            _coordinator.ResumeNavigatorFollow();
            _isFollowingEnd =
                ContentEndFollowMode == ScrollMarkerContentEndFollowMode.Automatic && IsAtContentEnd();
            _followEndAfterNextLayout = false;
            _endFollowGeneration++;
        }

        UpdateActiveItemFromRealizedContainers();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        EnsureRequiredItemsPanel();
        TryEstablishInitialEndFollowState();
        if (_followEndAfterNextLayout &&
            _contentScrollViewer is not null &&
            _coordinator.NavigationPhase == MarkerNavigationPhase.Idle)
        {
            _followEndAfterNextLayout = false;
            var maxX = Math.Max(0d, _contentScrollViewer.Extent.Width - _contentScrollViewer.Viewport.Width);
            var maxY = Math.Max(0d, _contentScrollViewer.Extent.Height - _contentScrollViewer.Viewport.Height);
            _isWritingOffset = true;
            try
            {
                _contentScrollViewer.Offset = Orientation == Orientation.Vertical
                    ? new Vector(_contentScrollViewer.Offset.X, maxY)
                    : new Vector(maxX, _contentScrollViewer.Offset.Y);
            }
            finally
            {
                _isWritingOffset = false;
            }

            _isFollowingEnd = true;
        }

        else if (_hasEstablishedEndFollowState &&
                 ContentEndFollowMode == ScrollMarkerContentEndFollowMode.Automatic &&
                 _isFollowingEnd &&
                 _coordinator.NavigationPhase == MarkerNavigationPhase.Idle &&
                 _contentScrollViewer is not null)
        {
            var maxOffset = GetMaxContentOffset();
            if (Math.Abs(maxOffset - _lastObservedMaxOffset) > 0.5d && !IsMainOffsetClose(maxOffset))
            {
                _isWritingOffset = true;
                try
                {
                    _contentScrollViewer.Offset = Orientation == Orientation.Vertical
                        ? new Vector(_contentScrollViewer.Offset.X, maxOffset)
                        : new Vector(maxOffset, _contentScrollViewer.Offset.Y);
                }
                finally
                {
                    _isWritingOffset = false;
                }
            }
        }

        if (_contentScrollViewer is not null)
        {
            _lastObservedMaxOffset = GetMaxContentOffset();
        }

        UpdateActiveItemFromRealizedContainers();
    }

    private void OnContentPointerWheelIntent(object? sender, PointerWheelEventArgs e)
    {
        _hasUserScrollIntent = true;
        Dispatcher.UIThread.Post(
            () => _hasUserScrollIntent = false,
            DispatcherPriority.Background);
    }

    private void TryEstablishInitialEndFollowState()
    {
        if (_hasEstablishedEndFollowState || _contentScrollViewer is null)
        {
            return;
        }

        var viewportExtent = Orientation == Orientation.Vertical
            ? _contentScrollViewer.Viewport.Height
            : _contentScrollViewer.Viewport.Width;
        var contentExtent = Orientation == Orientation.Vertical
            ? _contentScrollViewer.Extent.Height
            : _contentScrollViewer.Extent.Width;
        if (viewportExtent <= 0d || (contentExtent <= 0d && _coordinator.Descriptors.Count > 0))
        {
            return;
        }

        _hasEstablishedEndFollowState = true;
        _isFollowingEnd =
            ContentEndFollowMode == ScrollMarkerContentEndFollowMode.Automatic && IsAtContentEnd();
        _lastObservedMaxOffset = GetMaxContentOffset();
    }

    private bool IsAtContentEnd()
    {
        if (_contentScrollViewer is null)
        {
            return false;
        }

        var actual = Orientation == Orientation.Vertical
            ? _contentScrollViewer.Offset.Y
            : _contentScrollViewer.Offset.X;
        return Math.Abs(GetMaxContentOffset() - actual) <= 0.5d;
    }

    private double GetMaxContentOffset()
    {
        if (_contentScrollViewer is null)
        {
            return 0d;
        }

        return Orientation == Orientation.Vertical
            ? Math.Max(0d, _contentScrollViewer.Extent.Height - _contentScrollViewer.Viewport.Height)
            : Math.Max(0d, _contentScrollViewer.Extent.Width - _contentScrollViewer.Viewport.Width);
    }

    private void UpdateActiveItemFromRealizedContainers()
    {
        if (_isFaulted || _contentScrollViewer is null || _coordinator.Descriptors.Count == 0)
        {
            return;
        }

        if (_coordinator.NavigationPhase != MarkerNavigationPhase.Idle)
        {
            return;
        }

        if (_coordinator.SelectionMode == MarkerSelectionMode.Explicit)
        {
            if (_coordinator.ActiveIndex >= 0 &&
                TryGetContainerTargetOffset(_coordinator.ActiveIndex, out var explicitTarget) &&
                IsMainOffsetClose(explicitTarget))
            {
                return;
            }

            if (_coordinator.ActiveIndex >= 0 && !_realizedContainers.ContainsKey(_coordinator.ActiveIndex))
            {
                return;
            }

            _coordinator.EnterAutomatic();
        }

        var candidates = new List<(double Start, double End, int Index)>();
        var currentOffset = Orientation == Orientation.Vertical
            ? _contentScrollViewer.Offset.Y
            : _contentScrollViewer.Offset.X;
        foreach (var control in GetRealizedContainers())
        {
            if (control is not ScrollMarkerSectionContainer { SourceIndex: >= 0 } container)
            {
                continue;
            }

            if (!ScrollMarkerGeometry.TryGetLayoutStart(
                    container,
                    _contentScrollViewer,
                    Orientation,
                    out var start))
            {
                continue;
            }

            start += currentOffset;
            var extent = Orientation == Orientation.Vertical ? container.Bounds.Height : container.Bounds.Width;
            candidates.Add((start, start + Math.Max(0d, extent), container.SourceIndex));
        }

        if (candidates.Count == 0)
        {
            return;
        }

        candidates.Sort(static (left, right) =>
        {
            var coordinateOrder = left.Start.CompareTo(right.Start);
            return coordinateOrder != 0 ? coordinateOrder : left.Index.CompareTo(right.Index);
        });

        var probe = currentOffset + ScrollMarkerValueSanitizer.NonNegative(AnchorOffset);
        var maxOffset = Orientation == Orientation.Vertical
            ? Math.Max(0d, _contentScrollViewer.Extent.Height - _contentScrollViewer.Viewport.Height)
            : Math.Max(0d, _contentScrollViewer.Extent.Width - _contentScrollViewer.Viewport.Width);
        var isAtStart = currentOffset <= 0.5d;
        var isAtEnd = Math.Abs(maxOffset - currentOffset) <= 0.5d;
        (double Start, double End, int Index)? selected = null;

        if (isAtStart && candidates[0].Index == 0)
        {
            selected = candidates[0];
        }
        else if (isAtEnd && candidates[^1].Index == _coordinator.Descriptors.Count - 1)
        {
            selected = candidates[^1];
        }
        else
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (candidate.Start > probe)
                {
                    if (index > 0 && candidates[index - 1].Index + 1 == candidate.Index)
                    {
                        selected = candidates[index - 1];
                    }

                    break;
                }

                if (probe <= candidate.End + 0.5d)
                {
                    selected = candidate;
                }
            }
        }

        if (selected is null)
        {
            return;
        }

        var selectedValue = selected.Value;
        var oldIndex = _coordinator.ActiveIndex;
        if (oldIndex >= 0 && selectedValue.Index != oldIndex &&
            Math.Abs(selectedValue.Index - oldIndex) <= 1 &&
            !isAtStart && !isAtEnd)
        {
            if (selectedValue.Index > oldIndex && selectedValue.Start > probe - ActiveHysteresis)
            {
                return;
            }

            if (selectedValue.Index < oldIndex && probe > _lastActiveStart - ActiveHysteresis)
            {
                return;
            }
        }

        _lastActiveStart = selectedValue.Start;
        _coordinator.CommitAutomatic(selectedValue.Index);
    }

    private bool TryGetContainerTargetOffset(int index, out double targetOffset)
    {
        targetOffset = 0d;
        if (_contentScrollViewer is null ||
            !_realizedContainers.TryGetValue(index, out var realized) ||
            realized.Container.ContainerGeneration != realized.Generation ||
            !ScrollMarkerGeometry.TryGetLayoutStart(
                realized.Container,
                _contentScrollViewer,
                Orientation,
                out var start))
        {
            return false;
        }

        var currentMainOffset = Orientation == Orientation.Vertical
            ? _contentScrollViewer.Offset.Y
            : _contentScrollViewer.Offset.X;
        start += currentMainOffset;
        var maxOffset = Orientation == Orientation.Vertical
            ? Math.Max(0d, _contentScrollViewer.Extent.Height - _contentScrollViewer.Viewport.Height)
            : Math.Max(0d, _contentScrollViewer.Extent.Width - _contentScrollViewer.Viewport.Width);
        targetOffset = Math.Clamp(
            start - ScrollMarkerValueSanitizer.NonNegative(AnchorOffset),
            0d,
            maxOffset);
        return double.IsFinite(targetOffset);
    }

    private void ApplyNavigationOffset(int index, long generation, double targetOffset, int writeCount)
    {
        if (_contentScrollViewer is null || !_coordinator.IsCurrentNavigation(generation))
        {
            return;
        }

        var current = _contentScrollViewer.Offset;
        var target = Orientation == Orientation.Vertical
            ? new Vector(current.X, targetOffset)
            : new Vector(targetOffset, current.Y);
        _coordinator.SetEffectiveTargetOffset(generation, targetOffset);
        _coordinator.TryTransition(generation, MarkerNavigationPhase.AwaitingLayout);
        _isWritingOffset = true;
        try
        {
            _contentScrollViewer.Offset = target;
        }
        finally
        {
            _isWritingOffset = false;
        }

        Dispatcher.UIThread.Post(
            () => VerifyNavigationOffset(index, generation, writeCount),
            DispatcherPriority.Loaded);
    }

    private void VerifyNavigationOffset(int index, long generation, int writeCount)
    {
        if (_isFaulted || !_coordinator.IsCurrentNavigation(generation))
        {
            return;
        }

        if (_coordinator.CurrentRequest?.EffectiveTargetOffset is { } targetOffset &&
            IsMainOffsetClose(targetOffset))
        {
            _coordinator.CompleteNavigation(generation);
            return;
        }

        if (writeCount < 2 &&
            _coordinator.TryTransition(generation, MarkerNavigationPhase.ApplyingOffset) &&
            TryGetContainerTargetOffset(index, out var correctedTarget))
        {
            ApplyNavigationOffset(index, generation, correctedTarget, writeCount + 1);
            return;
        }

        _coordinator.CancelNavigation(
            generation,
            MarkerNavigationCancelReason.AlignmentDidNotConverge,
            true);
        UpdateActiveItemFromRealizedContainers();
    }

    private bool IsMainOffsetClose(double targetOffset)
    {
        if (_contentScrollViewer is null)
        {
            return false;
        }

        var actual = Orientation == Orientation.Vertical
            ? _contentScrollViewer.Offset.Y
            : _contentScrollViewer.Offset.X;
        return Math.Abs(actual - targetOffset) <= 0.5d;
    }
}

internal enum VirtualHostState
{
    Running,
    Faulted
}

internal sealed record VirtualHostFault(string Reason);
