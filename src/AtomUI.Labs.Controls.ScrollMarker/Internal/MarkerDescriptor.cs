using Avalonia.Styling;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal sealed record MarkerDescriptor(
    string AnchorKey,
    string Label,
    ControlTheme? MarkerTheme,
    int SourceIndex,
    object? Source);
