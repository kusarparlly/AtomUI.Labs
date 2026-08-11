using AtomUI.Theme;
using AtomUI.Labs.Controls.Led;
using AtomUI.Labs.Controls.ScrollMarker;
using AtomUI.Toolkits.GalleryBase;

namespace AtomUILabsGallery;

public static class ThemeManagerBuilderExtensions
{
    public static IThemeManagerBuilder UseLabsGalleryControls(this IThemeManagerBuilder themeManagerBuilder)
    {
        themeManagerBuilder.UseLed();
        themeManagerBuilder.UseScrollMarker();
        themeManagerBuilder.UseGalleryBase(AtomUILabsGalleryModule.Configure);
        return themeManagerBuilder;
    }
}
