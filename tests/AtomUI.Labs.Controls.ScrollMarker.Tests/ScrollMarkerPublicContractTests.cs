using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Shouldly;
using Xunit;

namespace AtomUI.Labs.Controls.ScrollMarker.Tests;

public sealed class ScrollMarkerPublicContractTests
{
    [Fact]
    public void Hosts_ShouldExposeTheDocumentedDefaults()
    {
        var direct = new ScrollMarkerView();
        var virtualItems = new ScrollMarkerItemsView();

        direct.Orientation.ShouldBe(Orientation.Vertical);
        virtualItems.Orientation.ShouldBe(Orientation.Vertical);
        direct.NavigatorPlacement.ShouldBe(ScrollMarkerPlacement.End);
        virtualItems.NavigatorPlacement.ShouldBe(ScrollMarkerPlacement.End);
        direct.NavigatorDisplayMode.ShouldBe(ScrollMarkerDisplayMode.Inline);
        virtualItems.NavigatorDisplayMode.ShouldBe(ScrollMarkerDisplayMode.Inline);
        direct.MarkerSlotExtent.ShouldBe(24d);
        virtualItems.MarkerSlotExtent.ShouldBe(24d);
        direct.MaxVisibleMarkerCount.ShouldBe(int.MaxValue);
        virtualItems.MaxVisibleMarkerCount.ShouldBe(int.MaxValue);
        direct.MainScrollBarVisibility.ShouldBe(ScrollBarVisibility.Auto);
        virtualItems.ContentEndFollowMode.ShouldBe(ScrollMarkerContentEndFollowMode.Automatic);
    }

    [Fact]
    public void Hosts_ShouldRejectNonPositiveMaxVisibleMarkerCount()
    {
        var direct = new ScrollMarkerView();
        var virtualItems = new ScrollMarkerItemsView();

        Should.Throw<ArgumentException>(() => direct.MaxVisibleMarkerCount = 0);
        Should.Throw<ArgumentException>(() => virtualItems.MaxVisibleMarkerCount = -1);

        direct.MaxVisibleMarkerCount.ShouldBe(int.MaxValue);
        virtualItems.MaxVisibleMarkerCount.ShouldBe(int.MaxValue);
    }

    [Fact]
    public void DirectHost_ShouldRejectDisabledMainAxisAndRetainLastValidValue()
    {
        var host = new ScrollMarkerView { MainScrollBarVisibility = ScrollBarVisibility.Visible };

        Should.Throw<InvalidOperationException>(() =>
            host.MainScrollBarVisibility = ScrollBarVisibility.Disabled);

        host.MainScrollBarVisibility.ShouldBe(ScrollBarVisibility.Visible);
    }

    [Fact]
    public void DedicatedPanels_ShouldUseAvaloniaVirtualizingStackPanelWithFixedCacheLength()
    {
        var contentPanel = new ScrollMarkerItemsPanel();
        var trackPanel = new ScrollMarkerTrackPanel();

        contentPanel.ShouldBeAssignableTo<Avalonia.Controls.VirtualizingStackPanel>();
        trackPanel.ShouldBeAssignableTo<Avalonia.Controls.VirtualizingStackPanel>();
        contentPanel.CacheLength.ShouldBe(0.5d);
        trackPanel.CacheLength.ShouldBe(0.5d);
    }

    [Fact]
    public void DirectHost_ShouldRejectInvalidAndDuplicateAnchorKeysTransactionally()
    {
        var host = new ScrollMarkerView();
        var valid = new ScrollMarkerSection { AnchorKey = "turn-1" };
        host.RegisterSection(valid);

        Should.Throw<InvalidOperationException>(() =>
            host.RegisterSection(new ScrollMarkerSection { AnchorKey = " turn-2" }));
        Should.Throw<InvalidOperationException>(() =>
            host.RegisterSection(new ScrollMarkerSection { AnchorKey = "turn-1" }));

        host.RegisteredSections.Count.ShouldBe(1);
        host.Coordinator.Descriptors.Count.ShouldBe(1);
    }

    [Fact]
    public void HorizontalRightToLeft_ShouldFailFastInBothHosts()
    {
        var direct = new ScrollMarkerView { Orientation = Orientation.Horizontal };
        var virtualItems = new ScrollMarkerItemsView { Orientation = Orientation.Horizontal };

        Should.Throw<InvalidOperationException>(() => direct.FlowDirection = FlowDirection.RightToLeft);
        Should.Throw<InvalidOperationException>(() => virtualItems.FlowDirection = FlowDirection.RightToLeft);

        virtualItems.HostState.ShouldBe(VirtualHostState.Faulted);
    }

    [Fact]
    public void RegisteredSection_ShouldKeepAnchorIdentityImmutable()
    {
        var host = new ScrollMarkerView();
        var section = new ScrollMarkerSection { AnchorKey = "turn-1" };
        host.RegisterSection(section);

        Should.Throw<InvalidOperationException>(() => section.AnchorKey = "turn-2");

        section.AnchorKey.ShouldBe("turn-1");
        host.Coordinator.Descriptors.Single().AnchorKey.ShouldBe("turn-1");
    }
}
