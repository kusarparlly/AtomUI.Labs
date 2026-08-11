using AtomUI.Toolkits.GalleryBase.Controls;
using AtomUIScrollViewer = AtomUI.Desktop.Controls.ScrollViewer;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AtomUILabsGallery.ShowCases.ScrollMarker;

public partial class ScrollMarkerShowCase : GalleryReactiveUserControl<ScrollMarkerViewModel>
{
    public ScrollMarkerShowCase()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.UIThread.Post(
            ResetInitialScrollPosition,
            DispatcherPriority.Background);
    }

    private void ResetInitialScrollPosition()
    {
        var pageScrollViewer = RootGalleryHost.GetVisualDescendants()
            .OfType<AtomUIScrollViewer>()
            .FirstOrDefault(viewer => viewer.Name == "PART_ScrollViewer");
        if (pageScrollViewer is not null)
        {
            pageScrollViewer.Offset = default;
        }
    }
}
