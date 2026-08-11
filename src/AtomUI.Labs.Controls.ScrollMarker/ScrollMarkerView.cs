using AtomUIScrollViewer = AtomUI.Desktop.Controls.ScrollViewer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AtomUI.Labs.Controls.ScrollMarker;

[TemplatePart(PartContentScrollViewer, typeof(AtomUIScrollViewer))]
[TemplatePart(PartNavigator, typeof(ScrollMarkerNavigator))]
public partial class ScrollMarkerView : ContentControl, IScrollMarkerHost
{
    private const double LayoutTolerance = 0.5d;
    private const double ActivationHysteresis = 4d;

    internal const string PartContentScrollViewer = "PART_ContentScrollViewer";
    internal const string PartNavigator = "PART_Navigator";

    private readonly ScrollMarkerCoordinator _coordinator = new();
    private readonly List<ScrollMarkerSection> _sections = [];
    private AtomUIScrollViewer? _contentScrollViewer;
    private ScrollMarkerNavigator? _navigator;
    private bool _restoringInvalidScrollBarValue;
    private bool _isAttached;
    private bool _configurationUpdateScheduled;
    private bool _hasUserScrollIntent;
    private bool _sectionOrderDirty = true;
    private bool _hasAutomaticHistory;

    static ScrollMarkerView()
    {
        AffectsMeasure<ScrollMarkerView>(
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

    public ScrollMarkerView()
    {
        _coordinator.NavigationRequested += OnNavigationRequested;
        LayoutUpdated += OnLayoutUpdated;
    }

    bool IScrollMarkerHost.IsNavigatorVisible => IsNavigatorVisible;

    internal bool IsNavigatorVisible => _coordinator.Descriptors.Count > 1;

    internal IReadOnlyList<ScrollMarkerSection> RegisteredSections => _sections;

    internal ScrollMarkerCoordinator Coordinator => _coordinator;

    internal int ConfigurationCommitCount { get; private set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        EnsureHostIsNotNested();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        base.OnDetachedFromVisualTree(e);
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
            ?? throw new InvalidOperationException($"ScrollMarkerView template must provide {PartContentScrollViewer}.");
        _navigator = e.NameScope.Find<ScrollMarkerNavigator>(PartNavigator)
            ?? throw new InvalidOperationException($"ScrollMarkerView template must provide {PartNavigator}.");

        _contentScrollViewer.ScrollChanged += OnContentScrollChanged;
        _contentScrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnContentPointerWheelIntent,
            RoutingStrategies.Tunnel,
            true);
        _navigator.Connect(_coordinator);
        ApplyTemplateConfiguration();
        UpdateActiveSectionFromViewport();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MainScrollBarVisibilityProperty &&
            !_restoringInvalidScrollBarValue &&
            change.NewValue is ScrollBarVisibility.Disabled)
        {
            _restoringInvalidScrollBarValue = true;
            try
            {
                SetCurrentValue(
                    MainScrollBarVisibilityProperty,
                    change.OldValue is ScrollBarVisibility oldValue ? oldValue : ScrollBarVisibility.Auto);
            }
            finally
            {
                _restoringInvalidScrollBarValue = false;
            }

            throw new InvalidOperationException(
                "MainScrollBarVisibility cannot be Disabled because the ScrollMarker main axis must remain scrollable.");
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
            change.Property == AnchorOffsetProperty ||
            change.Property == MainScrollBarVisibilityProperty)
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

    internal void RegisterSection(ScrollMarkerSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (_sections.Contains(section))
        {
            return;
        }

        ValidateAnchorKey(section.AnchorKey);
        if (_sections.Any(candidate =>
                string.Equals(candidate.AnchorKey, section.AnchorKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"ScrollMarkerSection AnchorKey '{section.AnchorKey}' is duplicated in one ScrollMarkerView.");
        }

        _sections.Add(section);
        _sectionOrderDirty = true;
        section.AttachOwner(this, section.AnchorKey);
        RebuildDescriptors();
    }

    internal void UnregisterSection(ScrollMarkerSection section)
    {
        if (!_sections.Remove(section))
        {
            return;
        }

        section.DetachOwner(this);
        _sectionOrderDirty = true;
        RebuildDescriptors();
    }

    internal void RefreshSectionMetadata(ScrollMarkerSection section)
    {
        if (_sections.Contains(section))
        {
            RebuildDescriptors();
        }
    }

    private static void ValidateAnchorKey(string? anchorKey)
    {
        if (string.IsNullOrWhiteSpace(anchorKey) ||
            !string.Equals(anchorKey, anchorKey.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ScrollMarkerSection.AnchorKey is required and cannot contain leading or trailing whitespace.");
        }
    }

    private void RebuildDescriptors()
    {
        EnsureSectionContentOrder();
        var descriptors = new List<MarkerDescriptor>(_sections.Count);
        for (var index = 0; index < _sections.Count; index++)
        {
            var section = _sections[index];
            if (_isAttached && !section.IsEffectivelyVisible)
            {
                continue;
            }

            descriptors.Add(new MarkerDescriptor(
                section.AnchorKey,
                string.IsNullOrEmpty(section.Label) ? section.AnchorKey : section.Label,
                section.MarkerTheme,
                index,
                section));
        }

        if (DescriptorsMatch(descriptors))
        {
            return;
        }

        _coordinator.ReplaceAll(descriptors);
        _navigator?.Configure(this);
        InvalidateMeasure();
    }

    private bool DescriptorsMatch(IReadOnlyList<MarkerDescriptor> descriptors)
    {
        if (_coordinator.Descriptors.Count != descriptors.Count)
        {
            return false;
        }

        for (var index = 0; index < descriptors.Count; index++)
        {
            var left = _coordinator.Descriptors[index];
            var right = descriptors[index];
            if (!string.Equals(left.AnchorKey, right.AnchorKey, StringComparison.Ordinal) ||
                !string.Equals(left.Label, right.Label, StringComparison.Ordinal) ||
                !ReferenceEquals(left.MarkerTheme, right.MarkerTheme) ||
                left.SourceIndex != right.SourceIndex ||
                !ReferenceEquals(left.Source, right.Source))
            {
                return false;
            }
        }

        return true;
    }

    private void EnsureSectionContentOrder()
    {
        if (!_sectionOrderDirty)
        {
            return;
        }

        var visualOrder = this.GetVisualDescendants()
            .OfType<ScrollMarkerSection>()
            .Where(section => ReferenceEquals(section.Owner, this))
            .ToArray();
        _sectionOrderDirty = false;
        if (visualOrder.Length != _sections.Count || visualOrder.SequenceEqual(_sections))
        {
            return;
        }

        _sections.Clear();
        _sections.AddRange(visualOrder);
    }

    private void EnsureHostIsNotNested()
    {
        if (this.GetVisualAncestors().Any(ancestor => ancestor is ScrollMarkerView or ScrollMarkerItemsView))
        {
            throw new InvalidOperationException(
                "ScrollMarkerView and ScrollMarkerItemsView cannot be nested in another ScrollMarker host.");
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
                MainScrollBarVisibility);
        }
        else
        {
            _contentScrollViewer.SetCurrentValue(
                Avalonia.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty,
                MainScrollBarVisibility);
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

        _navigator?.Configure(this);
        InvalidateMeasure();
    }

    private void ScheduleTemplateConfiguration()
    {
        if (_contentScrollViewer is null || _configurationUpdateScheduled)
        {
            return;
        }

        _configurationUpdateScheduled = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _configurationUpdateScheduled = false;
                ApplyTemplateConfiguration();
            },
            DispatcherPriority.Input);
    }

    private void ValidateLogicalConfiguration()
    {
        if (MainScrollBarVisibility == ScrollBarVisibility.Disabled)
        {
            throw new InvalidOperationException(
                "MainScrollBarVisibility cannot be Disabled because the ScrollMarker main axis must remain scrollable.");
        }

        if (Orientation == Orientation.Horizontal && FlowDirection == Avalonia.Media.FlowDirection.RightToLeft)
        {
            throw new InvalidOperationException(
                "Horizontal ScrollMarker supports left-to-right coordinates only in the first release.");
        }
    }

    private void OnNavigationRequested(object? sender, ScrollMarkerNavigationRequestedEventArgs e)
    {
        if (_contentScrollViewer is null ||
            e.Index < 0 ||
            e.Index >= _coordinator.Descriptors.Count ||
            _coordinator.Descriptors[e.Index].Source is not ScrollMarkerSection section)
        {
            _coordinator.CancelNavigation(e.Generation, MarkerNavigationCancelReason.InvalidTarget, true);
            return;
        }

        if (!_coordinator.TryTransition(e.Generation, MarkerNavigationPhase.ApplyingOffset) ||
            !TryGetEffectiveTargetOffset(section, out var targetOffset))
        {
            _coordinator.CancelNavigation(e.Generation, MarkerNavigationCancelReason.InvalidTarget, true);
            return;
        }

        var currentOffset = _contentScrollViewer.Offset;
        var target = Orientation == Orientation.Vertical
            ? new Vector(currentOffset.X, targetOffset)
            : new Vector(targetOffset, currentOffset.Y);
        _coordinator.SetEffectiveTargetOffset(e.Generation, targetOffset);
        _coordinator.TryTransition(e.Generation, MarkerNavigationPhase.AwaitingLayout);
        _contentScrollViewer.Offset = target;

        if (IsMainOffsetClose(targetOffset))
        {
            _coordinator.CompleteNavigation(e.Generation);
        }
    }

    private void OnContentScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var mainDelta = Orientation == Orientation.Vertical ? e.OffsetDelta.Y : e.OffsetDelta.X;
        if (_hasUserScrollIntent && Math.Abs(mainDelta) > 0d)
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
        }

        UpdateActiveSectionFromViewport();
    }

    private void OnContentPointerWheelIntent(object? sender, PointerWheelEventArgs e)
    {
        _hasUserScrollIntent = true;
        Dispatcher.UIThread.Post(
            () => _hasUserScrollIntent = false,
            DispatcherPriority.Background);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        RebuildDescriptors();
        CompletePendingDirectNavigationIfAligned();
        UpdateActiveSectionFromViewport();
    }

    private void UpdateActiveSectionFromViewport()
    {
        if (_contentScrollViewer is null || _sections.Count == 0)
        {
            return;
        }

        if (_coordinator.NavigationPhase != MarkerNavigationPhase.Idle)
        {
            return;
        }

        if (_coordinator.SelectionMode == MarkerSelectionMode.Explicit)
        {
            if (_coordinator.ActiveDescriptor?.Source is ScrollMarkerSection explicitSection &&
                TryGetEffectiveTargetOffset(explicitSection, out var explicitTarget) &&
                IsMainOffsetClose(explicitTarget))
            {
                return;
            }

            _coordinator.EnterAutomatic();
            _hasAutomaticHistory = false;
        }

        var currentOffset = Orientation == Orientation.Vertical
            ? _contentScrollViewer.Offset.Y
            : _contentScrollViewer.Offset.X;
        var probe = currentOffset + ScrollMarkerValueSanitizer.NonNegative(AnchorOffset);
        var candidates = new List<(double Start, int DescriptorIndex, int StableIndex)>();

        for (var descriptorIndex = 0; descriptorIndex < _coordinator.Descriptors.Count; descriptorIndex++)
        {
            var descriptor = _coordinator.Descriptors[descriptorIndex];
            if (descriptor.Source is not ScrollMarkerSection section)
            {
                continue;
            }

            if (!section.IsEffectivelyVisible)
            {
                continue;
            }

            if (!ScrollMarkerGeometry.TryGetLayoutStart(
                    section,
                    _contentScrollViewer,
                    Orientation,
                    out var sectionStart))
            {
                continue;
            }

            sectionStart += currentOffset;
            candidates.Add((sectionStart, descriptorIndex, descriptor.SourceIndex));
        }

        if (candidates.Count == 0)
        {
            return;
        }

        candidates.Sort(static (left, right) =>
        {
            var coordinateOrder = left.Start.CompareTo(right.Start);
            return coordinateOrder != 0 ? coordinateOrder : left.StableIndex.CompareTo(right.StableIndex);
        });

        var groups = new List<DirectPositionGroup>();
        foreach (var candidate in candidates)
        {
            if (groups.Count == 0 || Math.Abs(candidate.Start - groups[^1].Start) > LayoutTolerance)
            {
                groups.Add(new DirectPositionGroup(candidate.Start, candidate.DescriptorIndex, [candidate.DescriptorIndex]));
                continue;
            }

            var last = groups[^1];
            last.DescriptorIndexes.Add(candidate.DescriptorIndex);
            groups[^1] = last with { RepresentativeIndex = candidate.DescriptorIndex };
        }

        var rawGroupIndex = FindBaseGroup(groups, probe);
        var currentGroupIndex = groups.FindIndex(
            group => group.DescriptorIndexes.Contains(_coordinator.ActiveIndex));
        var maxOffset = Orientation == Orientation.Vertical
            ? Math.Max(0d, _contentScrollViewer.Extent.Height - _contentScrollViewer.Viewport.Height)
            : Math.Max(0d, _contentScrollViewer.Extent.Width - _contentScrollViewer.Viewport.Width);
        var isAtStart = currentOffset <= LayoutTolerance;
        var isAtEnd = !isAtStart && currentOffset >= maxOffset - LayoutTolerance;
        var targetGroupIndex = rawGroupIndex;

        if (isAtEnd)
        {
            targetGroupIndex = groups.Count - 1;
        }
        else if (!isAtStart && _hasAutomaticHistory && currentGroupIndex >= 0)
        {
            if (rawGroupIndex > currentGroupIndex)
            {
                var hysteresisCandidate = FindBaseGroup(groups, probe - ActivationHysteresis);
                targetGroupIndex = hysteresisCandidate > currentGroupIndex
                    ? hysteresisCandidate
                    : currentGroupIndex;
            }
            else if (rawGroupIndex < currentGroupIndex)
            {
                var hysteresisCandidate = FindBaseGroup(groups, probe + ActivationHysteresis);
                targetGroupIndex = hysteresisCandidate < currentGroupIndex
                    ? hysteresisCandidate
                    : currentGroupIndex;
            }
        }

        _coordinator.CommitAutomatic(groups[targetGroupIndex].RepresentativeIndex);
        _hasAutomaticHistory = true;
    }

    private static int FindBaseGroup(IReadOnlyList<DirectPositionGroup> groups, double probe)
    {
        var groupIndex = 0;
        for (var index = 1; index < groups.Count; index++)
        {
            if (groups[index].Start > probe + LayoutTolerance)
            {
                break;
            }

            groupIndex = index;
        }

        return groupIndex;
    }

    private bool TryGetEffectiveTargetOffset(ScrollMarkerSection section, out double targetOffset)
    {
        targetOffset = 0d;
        if (_contentScrollViewer is null ||
            !ScrollMarkerGeometry.TryGetLayoutStart(section, _contentScrollViewer, Orientation, out var sectionStart))
        {
            return false;
        }

        var currentMainOffset = Orientation == Orientation.Vertical
            ? _contentScrollViewer.Offset.Y
            : _contentScrollViewer.Offset.X;
        sectionStart += currentMainOffset;
        var maxOffset = Orientation == Orientation.Vertical
            ? Math.Max(0d, _contentScrollViewer.Extent.Height - _contentScrollViewer.Viewport.Height)
            : Math.Max(0d, _contentScrollViewer.Extent.Width - _contentScrollViewer.Viewport.Width);
        targetOffset = Math.Clamp(
            sectionStart - ScrollMarkerValueSanitizer.NonNegative(AnchorOffset),
            0d,
            maxOffset);
        return double.IsFinite(targetOffset);
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

    private void CompletePendingDirectNavigationIfAligned()
    {
        if (_coordinator.NavigationPhase != MarkerNavigationPhase.AwaitingLayout ||
            _coordinator.CurrentRequest is not { } request ||
            request.EffectiveTargetOffset is not { } targetOffset)
        {
            return;
        }

        if (IsMainOffsetClose(targetOffset))
        {
            _coordinator.CompleteNavigation(request.Generation);
        }
    }

    private sealed record DirectPositionGroup(
        double Start,
        int RepresentativeIndex,
        List<int> DescriptorIndexes);
}
