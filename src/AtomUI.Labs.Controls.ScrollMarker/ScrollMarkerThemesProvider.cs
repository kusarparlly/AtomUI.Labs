using AtomUI.Theme;
using Avalonia.Markup.Xaml;

namespace AtomUI.Labs.Controls.ScrollMarker;

internal sealed class ScrollMarkerThemesProvider : ControlThemesProvider
{
    public ScrollMarkerThemesProvider()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
