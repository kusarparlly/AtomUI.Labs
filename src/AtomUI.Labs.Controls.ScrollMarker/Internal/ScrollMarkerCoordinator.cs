using System.Collections.ObjectModel;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal sealed class ScrollMarkerCoordinator
{
    private readonly ObservableCollection<MarkerDescriptor> _descriptors = [];
    private readonly ReadOnlyObservableCollection<MarkerDescriptor> _readOnlyDescriptors;
    private int _activeIndex = -1;
    private long _navigationGeneration;

    internal ScrollMarkerCoordinator()
    {
        _readOnlyDescriptors = new ReadOnlyObservableCollection<MarkerDescriptor>(_descriptors);
    }

    internal ReadOnlyObservableCollection<MarkerDescriptor> Descriptors => _readOnlyDescriptors;

    internal int ActiveIndex => _activeIndex;

    internal MarkerDescriptor? ActiveDescriptor =>
        _activeIndex >= 0 && _activeIndex < _descriptors.Count ? _descriptors[_activeIndex] : null;

    internal MarkerNavigationPhase NavigationPhase { get; private set; }

    internal MarkerSelectionMode SelectionMode { get; private set; }

    internal MarkerNavigationRequest? CurrentRequest { get; private set; }

    internal MarkerNavigationTermination? LastTermination { get; private set; }

    internal NavigatorFollowMode NavigatorFollowMode { get; private set; }

    internal event EventHandler<int>? ActiveIndexChanged;

    internal event EventHandler<ScrollMarkerNavigationRequestedEventArgs>? NavigationRequested;

    internal event EventHandler<NavigatorFollowMode>? NavigatorFollowModeChanged;

    internal void ReplaceAll(IReadOnlyList<MarkerDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        _descriptors.Clear();
        foreach (var descriptor in descriptors)
        {
            _descriptors.Add(descriptor);
        }

        SetActiveIndex(descriptors.Count == 0 ? -1 : Math.Min(Math.Max(_activeIndex, 0), descriptors.Count - 1));
    }

    internal void Append(IReadOnlyList<MarkerDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        foreach (var descriptor in descriptors)
        {
            _descriptors.Add(descriptor);
        }

        if (_activeIndex < 0 && _descriptors.Count > 0)
        {
            SetActiveIndex(0);
        }
    }

    internal void SetActiveIndex(int index)
    {
        var normalized = _descriptors.Count == 0 ? -1 : Math.Clamp(index, 0, _descriptors.Count - 1);
        if (_activeIndex == normalized)
        {
            return;
        }

        _activeIndex = normalized;
        ActiveIndexChanged?.Invoke(this, normalized);
    }

    internal void RequestNavigation(int index)
    {
        if (index < 0 || index >= _descriptors.Count)
        {
            return;
        }

        if (CurrentRequest is { Index: var currentIndex } &&
            currentIndex == index &&
            NavigationPhase != MarkerNavigationPhase.Idle)
        {
            return;
        }

        if (CurrentRequest is { } superseded)
        {
            LastTermination = new MarkerNavigationTermination(
                superseded.Generation,
                false,
                MarkerNavigationCancelReason.Superseded);
        }

        var generation = ++_navigationGeneration;
        CurrentRequest = new MarkerNavigationRequest(
            _descriptors[index].AnchorKey,
            index,
            generation,
            null);
        NavigationPhase = MarkerNavigationPhase.Requested;
        SelectionMode = MarkerSelectionMode.Explicit;
        SetActiveIndex(index);
        NavigationRequested?.Invoke(this, new ScrollMarkerNavigationRequestedEventArgs(index, generation));
    }

    internal bool IsCurrentNavigation(long generation) => generation == _navigationGeneration;

    internal bool TryTransition(long generation, MarkerNavigationPhase phase)
    {
        if (CurrentRequest?.Generation != generation)
        {
            return false;
        }

        NavigationPhase = phase;
        return true;
    }

    internal void SetEffectiveTargetOffset(long generation, double targetOffset)
    {
        if (CurrentRequest is not { } request || request.Generation != generation)
        {
            return;
        }

        CurrentRequest = request with { EffectiveTargetOffset = targetOffset };
    }

    internal void CompleteNavigation(long generation)
    {
        if (CurrentRequest is not { } request || request.Generation != generation)
        {
            return;
        }

        LastTermination = new MarkerNavigationTermination(generation, true, null);
        CurrentRequest = null;
        NavigationPhase = MarkerNavigationPhase.Idle;
    }

    internal void CancelNavigation(long generation, MarkerNavigationCancelReason reason, bool enterAutomatic)
    {
        if (CurrentRequest is not { } request || request.Generation != generation)
        {
            return;
        }

        LastTermination = new MarkerNavigationTermination(generation, false, reason);
        CurrentRequest = null;
        NavigationPhase = MarkerNavigationPhase.Idle;
        _navigationGeneration++;
        if (enterAutomatic)
        {
            SelectionMode = MarkerSelectionMode.Automatic;
        }
    }

    internal void EnterAutomatic()
    {
        if (CurrentRequest is { } request)
        {
            CancelNavigation(request.Generation, MarkerNavigationCancelReason.LayoutDiverged, true);
            return;
        }

        SelectionMode = MarkerSelectionMode.Automatic;
    }

    internal void CommitAutomatic(int index)
    {
        if (NavigationPhase == MarkerNavigationPhase.Idle && SelectionMode == MarkerSelectionMode.Automatic)
        {
            SetActiveIndex(index);
        }
    }

    internal void BeginNavigatorBrowse()
    {
        if (NavigatorFollowMode == NavigatorFollowMode.Browse)
        {
            return;
        }

        NavigatorFollowMode = NavigatorFollowMode.Browse;
        NavigatorFollowModeChanged?.Invoke(this, NavigatorFollowMode);
    }

    internal void ResumeNavigatorFollow()
    {
        if (NavigatorFollowMode == NavigatorFollowMode.Follow)
        {
            return;
        }

        NavigatorFollowMode = NavigatorFollowMode.Follow;
        NavigatorFollowModeChanged?.Invoke(this, NavigatorFollowMode);
    }
}

internal enum MarkerNavigationPhase
{
    Idle,
    Requested,
    RealizingTarget,
    ApplyingOffset,
    AwaitingLayout
}

internal enum MarkerSelectionMode
{
    Automatic,
    Explicit
}

internal enum NavigatorFollowMode
{
    Follow,
    Browse
}

internal enum MarkerNavigationCancelReason
{
    Superseded,
    UserInterrupted,
    InvalidTarget,
    LayoutDiverged,
    TargetRealizationFailed,
    AlignmentDidNotConverge
}

internal readonly record struct MarkerNavigationRequest(
    string AnchorKey,
    int Index,
    long Generation,
    double? EffectiveTargetOffset);

internal readonly record struct MarkerNavigationTermination(
    long Generation,
    bool Completed,
    MarkerNavigationCancelReason? CancelReason);

internal sealed class ScrollMarkerNavigationRequestedEventArgs(int index, long generation) : EventArgs
{
    internal int Index { get; } = index;

    internal long Generation { get; } = generation;
}
