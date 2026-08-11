using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace AtomUI.Labs.Controls.ScrollMarker;

public class ScrollMarkerSection : ContentControl
{
    public static readonly StyledProperty<string> AnchorKeyProperty =
        AvaloniaProperty.Register<ScrollMarkerSection, string>(nameof(AnchorKey), string.Empty);

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<ScrollMarkerSection, string?>(nameof(Label));

    public static readonly StyledProperty<ControlTheme?> MarkerThemeProperty =
        AvaloniaProperty.Register<ScrollMarkerSection, ControlTheme?>(nameof(MarkerTheme));

    private ScrollMarkerView? _owner;
    private string? _registeredAnchorKey;
    private bool _restoringAnchorKey;

    public string AnchorKey
    {
        get => GetValue(AnchorKeyProperty);
        set => SetValue(AnchorKeyProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public ControlTheme? MarkerTheme
    {
        get => GetValue(MarkerThemeProperty);
        set => SetValue(MarkerThemeProperty, value);
    }

    internal ScrollMarkerView? Owner => _owner;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var host = this.GetVisualAncestors().FirstOrDefault(
            ancestor => ancestor is ScrollMarkerView or ScrollMarkerItemsView);
        if (host is ScrollMarkerItemsView)
        {
            throw new InvalidOperationException(
                "ScrollMarkerSection cannot be used inside ScrollMarkerItemsView. Use ItemTemplate data items in Virtual Items mode.");
        }

        if (host is ScrollMarkerView directHost)
        {
            directHost.RegisterSection(this);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _owner?.UnregisterSection(this);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AnchorKeyProperty &&
            _owner is not null &&
            !_restoringAnchorKey &&
            !string.Equals(change.NewValue as string, _registeredAnchorKey, StringComparison.Ordinal))
        {
            _restoringAnchorKey = true;
            try
            {
                SetCurrentValue(AnchorKeyProperty, _registeredAnchorKey ?? string.Empty);
            }
            finally
            {
                _restoringAnchorKey = false;
            }

            throw new InvalidOperationException(
                "AnchorKey cannot change while a ScrollMarkerSection is registered. Remove the section before changing its stable identity.");
        }

        if (_owner is not null && (change.Property == LabelProperty || change.Property == MarkerThemeProperty))
        {
            _owner.RefreshSectionMetadata(this);
        }
    }

    internal void AttachOwner(ScrollMarkerView owner, string registeredAnchorKey)
    {
        _owner = owner;
        _registeredAnchorKey = registeredAnchorKey;
    }

    internal void DetachOwner(ScrollMarkerView owner)
    {
        if (!ReferenceEquals(_owner, owner))
        {
            return;
        }

        _owner = null;
        _registeredAnchorKey = null;
    }
}
