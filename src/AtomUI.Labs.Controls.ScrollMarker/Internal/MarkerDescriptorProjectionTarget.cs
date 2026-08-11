using Avalonia;
using Avalonia.Controls.Templates;
using Avalonia.Data;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal sealed class MarkerDescriptorProjectionTarget : StyledElement, IDisposable
{
    private static readonly StyledProperty<object?> AnchorKeyValueProperty =
        AvaloniaProperty.Register<MarkerDescriptorProjectionTarget, object?>("AnchorKeyValue");

    private static readonly StyledProperty<object?> LabelValueProperty =
        AvaloniaProperty.Register<MarkerDescriptorProjectionTarget, object?>("LabelValue");

    private static readonly StyledProperty<object?> MarkerThemeValueProperty =
        AvaloniaProperty.Register<MarkerDescriptorProjectionTarget, object?>("MarkerThemeValue");

    private readonly List<IDisposable> _subscriptions = [];

    internal MarkerDescriptorProjectionTarget(
        object item,
        BindingBase anchorKeyBinding,
        BindingBase? labelBinding,
        BindingBase? markerThemeBinding)
    {
        _subscriptions.Add(Bind(AnchorKeyValueProperty, anchorKeyBinding));
        if (labelBinding is not null)
        {
            _subscriptions.Add(Bind(LabelValueProperty, labelBinding));
        }

        if (markerThemeBinding is not null)
        {
            _subscriptions.Add(Bind(MarkerThemeValueProperty, markerThemeBinding));
        }

        DataContext = item;
    }

    internal object? AnchorKeyValue => GetValue(AnchorKeyValueProperty);

    internal object? LabelValue => GetValue(LabelValueProperty);

    internal object? MarkerThemeValue => GetValue(MarkerThemeValueProperty);

    public void Dispose()
    {
        DataContext = null;
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }
}
