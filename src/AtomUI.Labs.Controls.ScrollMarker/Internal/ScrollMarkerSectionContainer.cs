using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal sealed class ScrollMarkerSectionContainer : ContentControl
{
    internal int SourceIndex { get; private set; } = -1;

    internal int ContainerGeneration { get; private set; }

    internal MarkerDescriptor? Descriptor { get; private set; }

    internal void Prepare(object? item, MarkerDescriptor descriptor, int sourceIndex, IDataTemplate? itemTemplate)
    {
        if (Descriptor is not null || SourceIndex >= 0)
        {
            throw new InvalidOperationException("A recycled ScrollMarker section container must be cleared before Prepare.");
        }

        checked
        {
            ContainerGeneration++;
        }

        SourceIndex = sourceIndex;
        Descriptor = descriptor;
        Content = item;
        ContentTemplate = itemTemplate;
        DataContext = item;
    }

    internal void ClearPreparedState()
    {
        checked
        {
            ContainerGeneration++;
        }

        SourceIndex = -1;
        Descriptor = null;
        Content = null;
        ContentTemplate = null;
        DataContext = null;
        Theme = null;
    }
}
