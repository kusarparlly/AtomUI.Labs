using AtomUI.Theme;

namespace AtomUI.Labs.Controls.ScrollMarker;

public static class ScrollMarkerThemeManagerBuilderExtensions
{
    public static IThemeManagerBuilder UseScrollMarker(this IThemeManagerBuilder themeManagerBuilder)
    {
        ArgumentNullException.ThrowIfNull(themeManagerBuilder);
        themeManagerBuilder.AddControlThemesProvider(new ScrollMarkerThemesProvider());
        return themeManagerBuilder;
    }
}
