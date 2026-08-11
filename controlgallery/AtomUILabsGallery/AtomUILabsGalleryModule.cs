using System.Reflection;
using AtomUI.Controls;
using AtomUI.Icons.AntDesign;
using AtomUI.Toolkits.GalleryBase.Configuration;
using AtomUI.Toolkits.GalleryBase.Routing;
using AtomUILabsGallery.ShowCases;
using AtomUILabsGallery.ShowCases.Led;
using AtomUILabsGallery.ShowCases.ScrollMarker;
using Avalonia.Controls;
using ReactiveUI;

namespace AtomUILabsGallery;

public static class AtomUILabsGalleryModule
{
    public static GalleryBaseConfiguration CreateConfiguration()
    {
        var options = new GalleryBaseOptions();
        Configure(options);
        return options.BuildConfiguration();
    }

    public static GalleryBaseConfiguration GetConfiguration()
    {
        return GalleryBaseConfigurationProvider.Current ?? CreateConfiguration();
    }

    public static void Configure(GalleryBaseOptions options)
    {
        ConfigureBranding(options.Branding);
        ConfigureNavigation(options.Navigation);
        ConfigureRoutes(options.Routes);
    }

    public static void RegisterViews(DefaultViewLocator locator)
    {
        CreateConfiguration().Routes.RegisterViews(locator);
    }

    private static void ConfigureBranding(GalleryBrandingOptions branding)
    {
        branding.AppName     = "AtomUI Labs Gallery";
        branding.VersionText = $"v{GetAssemblyMetadataValue("AtomUILabsVersion")}";
        branding.Links.Add(new GalleryLink("Website", "https://www.atomui.net", Icon(AntDesignIconKind.GlobalOutlined)));
        branding.Links.Add(new GalleryLink("GitHub", "https://github.com/chinware/atomui", Icon(AntDesignIconKind.GithubOutlined)));
    }

    private static void ConfigureNavigation(AtomUI.Toolkits.GalleryBase.Navigation.GalleryNavigationBuilder navigation)
    {
        navigation.DefaultRoute = OverviewViewModel.ID;
        navigation.DefaultOpenKeys.Add("Labs");

        navigation.AddPage(OverviewViewModel.ID, "Overview", Icon(AntDesignIconKind.HomeOutlined));

        var labs = navigation.AddGroup("Labs", "Labs Controls", Icon(AntDesignIconKind.AppstoreOutlined));
        labs.AddPage(LedSegmentViewModel.ID, "LED Segment Display", Icon(AntDesignIconKind.FieldNumberOutlined));
        labs.AddPage(LedMatrixViewModel.ID, "LED Matrix Display", Icon(AntDesignIconKind.TableOutlined));
        labs.AddPage(ScrollMarkerViewModel.ID, "Scroll Marker", Icon(AntDesignIconKind.UnorderedListOutlined));
    }

    private static void ConfigureRoutes(GalleryRouteRegistry routes)
    {
        routes.Map(OverviewViewModel.ID, screen => new OverviewViewModel(screen), () => new OverviewShowCase());
        routes.Map(LedSegmentViewModel.ID, screen => new LedSegmentViewModel(screen), () => new LedSegmentShowCase());
        routes.Map(LedMatrixViewModel.ID, screen => new LedMatrixViewModel(screen), () => new LedMatrixShowCase());
        routes.Map(
            ScrollMarkerViewModel.ID,
            screen => new ScrollMarkerViewModel(screen),
            () => new ScrollMarkerShowCase());
    }

    private static Func<PathIcon> Icon(AntDesignIconKind kind)
    {
        return () => (PathIcon)new AntDesignIconProvider(kind).ProvideValue(null!);
    }

    private static string GetAssemblyMetadataValue(string key)
    {
        return typeof(AtomUILabsGalleryModule)
               .Assembly
               .GetCustomAttributes<AssemblyMetadataAttribute>()
               .FirstOrDefault(attribute => attribute.Key == key)
               ?.Value ?? "0.0.0";
    }
}
