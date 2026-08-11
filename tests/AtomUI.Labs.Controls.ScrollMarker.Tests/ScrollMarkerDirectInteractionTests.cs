using AtomUIScrollViewer = AtomUI.Desktop.Controls.ScrollViewer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Shouldly;
using Xunit;

namespace AtomUI.Labs.Controls.ScrollMarker.Tests;

public sealed class ScrollMarkerDirectInteractionTests
{
    static ScrollMarkerDirectInteractionTests()
    {
        AvaloniaTestApp.EnsureInitialized();
    }

    [Fact]
    public void MarkerNavigation_ShouldAlignSectionStartWithoutAnimation()
    {
        var host = CreateVerticalHost(4, 120d);

        ShowInWindow(host, () =>
        {
            var scrollViewer = GetContentScrollViewer(host);

            host.Coordinator.RequestNavigation(1);
            Dispatcher.UIThread.RunJobs();

            scrollViewer.Offset.Y.ShouldBe(120d, 0.5d);
            host.Coordinator.ActiveIndex.ShouldBe(1);
            host.Coordinator.NavigationPhase.ShouldBe(MarkerNavigationPhase.Idle);
            host.Coordinator.SelectionMode.ShouldBe(MarkerSelectionMode.Explicit);
            host.Coordinator.LastTermination.ShouldNotBeNull();
            host.Coordinator.LastTermination!.Value.Completed.ShouldBeTrue();
        });
    }

    [Fact]
    public void PointerClickOnVerticalMarker_ShouldNavigateToSection()
    {
        var host = CreateVerticalHost(4, 120d);

        ShowInWindow(host, () =>
        {
            var marker = GetMarker(host, 2);
            ClickMarker(marker);
            Dispatcher.UIThread.RunJobs();

            GetContentScrollViewer(host).Offset.Y.ShouldBe(240d, 0.5d);
            host.Coordinator.ActiveIndex.ShouldBe(2);
            host.Coordinator.NavigationPhase.ShouldBe(MarkerNavigationPhase.Idle);
            host.Coordinator.SelectionMode.ShouldBe(MarkerSelectionMode.Explicit);
        });
    }

    [Fact]
    public void PointerClickOnHorizontalMarker_ShouldNavigateToSection()
    {
        var host = CreateHorizontalHost(8, 160d);

        ShowInWindow(host, () =>
        {
            var marker = GetMarker(host, 2);
            ClickMarker(marker);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(2);
            GetContentScrollViewer(host).Offset.X.ShouldBe(320d, 0.5d);
            host.Coordinator.NavigationPhase.ShouldBe(MarkerNavigationPhase.Idle);
            host.Coordinator.SelectionMode.ShouldBe(MarkerSelectionMode.Explicit);
        });
    }

    [Fact]
    public void RightClickOnMarker_ShouldNotNavigate()
    {
        var host = CreateVerticalHost(4, 120d);

        ShowInWindow(host, () =>
        {
            ClickMarker(GetMarker(host, 2), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(0);
            GetContentScrollViewer(host).Offset.Y.ShouldBe(0d, 0.5d);
        });
    }

    [Fact]
    public void PointerReleasedOutsideMarker_ShouldNotNavigate()
    {
        var host = CreateVerticalHost(4, 120d);

        ShowInWindow(host, () =>
        {
            ClickMarker(GetMarker(host, 2), MouseButton.Left, releaseOutside: true);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(0);
            GetContentScrollViewer(host).Offset.Y.ShouldBe(0d, 0.5d);
        });
    }

    [Fact]
    public void ConsecutiveMarkerNavigation_ShouldLetLatestRequestWin()
    {
        var host = CreateVerticalHost(6, 120d);

        ShowInWindow(host, () =>
        {
            host.Coordinator.RequestNavigation(1);
            host.Coordinator.RequestNavigation(4);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(4);
            GetContentScrollViewer(host).Offset.Y.ShouldBeGreaterThan(400d);
        });
    }

    [Fact]
    public void EqualCoordinateNestedSections_ShouldSelectLastStableSection()
    {
        var nested = new ScrollMarkerSection
        {
            AnchorKey = "child",
            Height = 120,
            VerticalAlignment = VerticalAlignment.Top,
            Content = new TextBlock { Text = "nested" }
        };
        var parent = new ScrollMarkerSection
        {
            AnchorKey = "parent",
            Content = nested
        };
        var host = new ScrollMarkerView { Content = parent };

        ShowInWindow(host, () =>
        {
            host.RegisteredSections.Count.ShouldBe(2);
            host.Coordinator.Descriptors.Select(descriptor => descriptor.AnchorKey)
                .ShouldBe(new[] { "parent", "child" });
            var scrollViewer = GetContentScrollViewer(host);
            var parentStart = parent.TranslatePoint(default, scrollViewer)!.Value.Y + scrollViewer.Offset.Y;
            var childStart = nested.TranslatePoint(default, scrollViewer)!.Value.Y + scrollViewer.Offset.Y;
            childStart.ShouldBe(parentStart, 0.001d);
            host.Coordinator.ActiveDescriptor.ShouldNotBeNull();
            host.Coordinator.ActiveDescriptor!.AnchorKey.ShouldBe("child");
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Navigator_ShouldBeHiddenForZeroOrOneSection(int sectionCount)
    {
        var host = CreateVerticalHost(sectionCount, 120d);

        ShowInWindow(host, () =>
        {
            var navigator = host.GetVisualDescendants().OfType<ScrollMarkerNavigator>().Single();
            navigator.IsVisible.ShouldBeFalse();
        });
    }

    [Fact]
    public void SynchronousConfigurationChanges_ShouldCommitOneFinalSnapshot()
    {
        var host = CreateVerticalHost(3, 120d);

        ShowInWindow(host, () =>
        {
            var commitsBeforeChanges = host.ConfigurationCommitCount;
            host.Orientation = Orientation.Horizontal;
            host.MainScrollBarVisibility = ScrollBarVisibility.Visible;

            host.ConfigurationCommitCount.ShouldBe(commitsBeforeChanges);
            Dispatcher.UIThread.RunJobs();

            host.ConfigurationCommitCount.ShouldBe(commitsBeforeChanges + 1);
            var scrollViewer = GetContentScrollViewer(host);
            scrollViewer.HorizontalScrollBarVisibility.ShouldBe(ScrollBarVisibility.Visible);
            scrollViewer.VerticalScrollBarVisibility.ShouldBe(ScrollBarVisibility.Disabled);
        });
    }

    [Fact]
    public void NavigationGeometry_ShouldIgnoreRenderTransform()
    {
        var first = new ScrollMarkerSection
        {
            AnchorKey = "first",
            Height = 120,
            Content = new TextBlock { Text = "first" }
        };
        var second = new ScrollMarkerSection
        {
            AnchorKey = "second",
            Height = 120,
            RenderTransform = new TranslateTransform(0, -100),
            Content = new TextBlock { Text = "second" }
        };
        var tail = new Border { Height = 240 };
        var host = new ScrollMarkerView
        {
            Content = new StackPanel { Children = { first, second, tail } }
        };

        ShowInWindow(host, () =>
        {
            host.Coordinator.RequestNavigation(1);
            Dispatcher.UIThread.RunJobs();

            GetContentScrollViewer(host).Offset.Y.ShouldBe(120d, 0.5d);
        });
    }

    [Fact]
    public void EffectivelyHiddenSection_ShouldNotCreateADirectMarker()
    {
        var visible = new ScrollMarkerSection
        {
            AnchorKey = "visible",
            Height = 120,
            Content = new TextBlock { Text = "visible" }
        };
        var hidden = new ScrollMarkerSection
        {
            AnchorKey = "hidden",
            Height = 120,
            IsVisible = false,
            Content = new TextBlock { Text = "hidden" }
        };
        var host = new ScrollMarkerView
        {
            Content = new StackPanel { Children = { visible, hidden } }
        };

        ShowInWindow(host, () =>
        {
            host.Coordinator.Descriptors.Count.ShouldBe(1);
            host.GetVisualDescendants().OfType<ScrollMarkerNavigator>().Single().IsVisible.ShouldBeFalse();

            hidden.IsVisible = true;
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.Descriptors.Count.ShouldBe(2);
            host.GetVisualDescendants().OfType<ScrollMarkerNavigator>().Single().IsVisible.ShouldBeTrue();
        });
    }

    [Fact]
    public void ZeroSizeSection_ShouldRemainANavigableMarker()
    {
        var emptyAnchor = new ScrollMarkerSection { AnchorKey = "empty", Height = 0 };
        var contentAnchor = new ScrollMarkerSection
        {
            AnchorKey = "content",
            Height = 120,
            Content = new TextBlock { Text = "content" }
        };
        var host = new ScrollMarkerView
        {
            Content = new StackPanel { Children = { emptyAnchor, contentAnchor } }
        };

        ShowInWindow(host, () =>
        {
            host.Coordinator.Descriptors.Select(descriptor => descriptor.AnchorKey)
                .ShouldBe(new[] { "empty", "content" });
            host.Coordinator.ActiveDescriptor.ShouldNotBeNull();
            host.Coordinator.ActiveDescriptor!.AnchorKey.ShouldBe("content");
        });
    }

    [Fact]
    public void AutomaticSelection_ShouldUseFourDipBidirectionalHysteresis()
    {
        var host = CreateVerticalHost(4, 100d);

        ShowInWindow(host, () =>
        {
            var scrollViewer = GetContentScrollViewer(host);
            host.Coordinator.ActiveIndex.ShouldBe(0);

            scrollViewer.Offset = new Vector(0, 102d);
            Dispatcher.UIThread.RunJobs();
            host.Coordinator.ActiveIndex.ShouldBe(0);

            scrollViewer.Offset = new Vector(0, 104d);
            Dispatcher.UIThread.RunJobs();
            host.Coordinator.ActiveIndex.ShouldBe(1);

            scrollViewer.Offset = new Vector(0, 97d);
            Dispatcher.UIThread.RunJobs();
            host.Coordinator.ActiveIndex.ShouldBe(1);

            scrollViewer.Offset = new Vector(0, 95d);
            Dispatcher.UIThread.RunJobs();
            host.Coordinator.ActiveIndex.ShouldBe(0);
        });
    }

    [Fact]
    public void SmallMarkerSet_ShouldUseEqualSlotsThatFillNavigatorViewport()
    {
        var host = CreateVerticalHost(3, 120d);

        ShowInWindow(host, () =>
        {
            Dispatcher.UIThread.RunJobs();
            var navigator = host.GetVisualDescendants().OfType<ScrollMarkerNavigator>().Single();
            var markerScrollViewer = navigator.GetVisualDescendants()
                .OfType<AtomUIScrollViewer>()
                .Single(viewer => viewer.Name == "PART_NavigatorScrollViewer");
            var markers = navigator.GetVisualDescendants().OfType<ScrollMarkerItem>().ToArray();

            markers.Length.ShouldBe(3);
            markers.Select(marker => marker.Height).Distinct().Count().ShouldBe(1);
            markers[0].Height.ShouldBeGreaterThan(24d);
            (markers[0].Height * markers.Length).ShouldBe(markerScrollViewer.Viewport.Height, 0.5d);
            markerScrollViewer.IsScrollChainingEnabled.ShouldBeFalse();
            navigator.HasMarkerOverflow.ShouldBeFalse();
            GetNavigationButton(navigator, ScrollMarkerNavigator.PartPreviousMarkerButton).IsVisible.ShouldBeFalse();
            GetNavigationButton(navigator, ScrollMarkerNavigator.PartNextMarkerButton).IsVisible.ShouldBeFalse();
        });
    }

    [Fact]
    public void InitialNavigatorRealization_ShouldNotScrollAnAncestorViewport()
    {
        var generatedSections = Enumerable.Range(0, 30)
            .Select(index => new ScrollMarkerSection
            {
                AnchorKey = $"generated-{index}",
                Label = $"Generated {index}",
                Height = 48d,
                Content = new TextBlock { Text = $"Generated content {index}" }
            })
            .ToArray();
        var overflowingHost = new ScrollMarkerView
        {
            Height = 240d,
            MaxVisibleMarkerCount = 6,
            Content = new ItemsControl { ItemsSource = generatedSections }
        };
        var outerScrollViewer = new AtomUIScrollViewer
        {
            Content = new StackPanel
            {
                Children =
                {
                    new Border { Height = 360d },
                    overflowingHost,
                    new Border { Height = 360d }
                }
            }
        };
        var ancestorReceivedNavigatorBringIntoView = false;
        outerScrollViewer.AddHandler(
            Control.RequestBringIntoViewEvent,
            (_, _) => ancestorReceivedNavigatorBringIntoView = true);

        ShowInWindow(outerScrollViewer, () =>
        {
            overflowingHost.Coordinator.ActiveIndex.ShouldBe(0);
            outerScrollViewer.Offset.Y.ShouldBe(0d, 0.5d);

            GetMarker(overflowingHost, 0).BringIntoView();
            Dispatcher.UIThread.RunJobs();

            ancestorReceivedNavigatorBringIntoView.ShouldBeFalse();
        });
    }

    [Fact]
    public void NavigationButtons_ShouldNavigateToAdjacentMarkersAndScrollContent()
    {
        var host = CreateVerticalHost(30, 120d);
        host.MaxVisibleMarkerCount = 6;

        ShowInWindow(host, () =>
        {
            var navigator = GetNavigator(host);
            var markerScrollViewer = GetNavigatorScrollViewer(navigator);
            var previous = GetNavigationButton(navigator, ScrollMarkerNavigator.PartPreviousMarkerButton);
            var next = GetNavigationButton(navigator, ScrollMarkerNavigator.PartNextMarkerButton);
            var contentScrollViewer = GetContentScrollViewer(host);

            navigator.HasMarkerOverflow.ShouldBeTrue();
            navigator.EffectiveVisibleMarkerCount.ShouldBe(6);
            CountVisibleMarkers(navigator, markerScrollViewer).ShouldBe(6);
            previous.IsVisible.ShouldBeTrue();
            next.IsVisible.ShouldBeTrue();
            previous.IsEnabled.ShouldBeFalse();
            next.IsEnabled.ShouldBeTrue();
            var previousAtomButton = (AtomUI.Desktop.Controls.Button)previous;
            var nextAtomButton = (AtomUI.Desktop.Controls.Button)next;
            previousAtomButton.Content.ShouldBeNull();
            nextAtomButton.Content.ShouldBeNull();
            var previousIcon = previousAtomButton.Icon
                .ShouldBeOfType<AtomUI.Icons.AntDesign.UpOutlined>();
            var nextIcon = nextAtomButton.Icon
                .ShouldBeOfType<AtomUI.Icons.AntDesign.DownOutlined>();
            previousIcon.Bounds.Width.ShouldBeGreaterThanOrEqualTo(12d);
            previousIcon.Bounds.Height.ShouldBeGreaterThanOrEqualTo(12d);
            nextIcon.Bounds.Width.ShouldBeGreaterThanOrEqualTo(12d);
            nextIcon.Bounds.Height.ShouldBeGreaterThanOrEqualTo(12d);
            var previousIconOrigin = previousIcon.TranslatePoint(default, previous);
            var nextIconOrigin = nextIcon.TranslatePoint(default, next);
            previousIconOrigin.ShouldNotBeNull();
            nextIconOrigin.ShouldNotBeNull();
            (previousIconOrigin!.Value.X + previousIcon.Bounds.Width / 2d)
                .ShouldBe(previous.Bounds.Width / 2d, 0.25d);
            (nextIconOrigin!.Value.X + nextIcon.Bounds.Width / 2d)
                .ShouldBe(next.Bounds.Width / 2d, 0.25d);
            (previousIconOrigin.Value.Y + previousIcon.Bounds.Height / 2d)
                .ShouldBe(previous.Bounds.Height / 2d, 0.25d);
            (nextIconOrigin.Value.Y + nextIcon.Bounds.Height / 2d)
                .ShouldBe(next.Bounds.Height / 2d, 0.25d);
            previousAtomButton.ButtonType
                .ShouldBe(AtomUI.Desktop.Controls.ButtonType.Text);
            nextAtomButton.ButtonType
                .ShouldBe(AtomUI.Desktop.Controls.ButtonType.Text);

            ClickControl(next);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(1);
            host.Coordinator.NavigatorFollowMode.ShouldBe(NavigatorFollowMode.Follow);
            contentScrollViewer.Offset.Y.ShouldBe(120d, 0.5d);
            markerScrollViewer.Offset.Y.ShouldBe(0d, 0.5d);
            previous.IsEnabled.ShouldBeTrue();

            ClickControl(next);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(2);
            contentScrollViewer.Offset.Y.ShouldBe(240d, 0.5d);

            ClickControl(previous);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(1);
            contentScrollViewer.Offset.Y.ShouldBe(120d, 0.5d);
            markerScrollViewer.Offset.Y.ShouldBe(0d, 0.5d);
            markerScrollViewer.Transitions!
                .OfType<Avalonia.Animation.VectorTransition>()
                .ShouldBeEmpty();
        });
    }

    [Fact]
    public void AutomaticAdjacentFollow_ShouldKeepAnimatingAcrossSuccessiveMarkers()
    {
        var host = CreateVerticalHost(30, 120d);
        host.MaxVisibleMarkerCount = 6;

        ShowInWindow(host, () =>
        {
            var navigator = GetNavigator(host);
            var markerScrollViewer = GetNavigatorScrollViewer(navigator);
            var contentScrollViewer = GetContentScrollViewer(host);
            var itemsPresenter = navigator.GetVisualDescendants()
                .OfType<Avalonia.Controls.Presenters.ItemsPresenter>()
                .Single(presenter => presenter.Name == ScrollMarkerNavigator.PartItemsPresenter);
            var trackTransform = itemsPresenter.RenderTransform.ShouldBeOfType<TranslateTransform>();

            host.Coordinator.EnterAutomatic();
            for (var index = 1; index < 6; index++)
            {
                contentScrollViewer.Offset = new Vector(0d, index * 120d + 12d);
                Dispatcher.UIThread.RunJobs();
            }

            markerScrollViewer.Offset.Y.ShouldBe(0d, 0.5d);

            contentScrollViewer.Offset = new Vector(0d, 6d * 120d + 12d);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(6);
            var firstOffset = markerScrollViewer.Offset.Y;
            firstOffset.ShouldBeGreaterThan(0d);
            AssertTrackMotionProgress(trackTransform, firstOffset);

            contentScrollViewer.Offset = new Vector(0d, 7d * 120d + 12d);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(7);
            var secondOffset = markerScrollViewer.Offset.Y;
            secondOffset.ShouldBeGreaterThan(firstOffset);
            AssertTrackMotionProgress(trackTransform, secondOffset - firstOffset);

            contentScrollViewer.Offset = new Vector(0d, 8d * 120d + 12d);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(8);
            var thirdOffset = markerScrollViewer.Offset.Y;
            thirdOffset.ShouldBeGreaterThan(secondOffset);
            AssertTrackMotionProgress(trackTransform, thirdOffset - secondOffset);

            for (var index = 7; index >= 3; index--)
            {
                contentScrollViewer.Offset = new Vector(0d, index * 120d + 12d);
                Dispatcher.UIThread.RunJobs();
                host.Coordinator.ActiveIndex.ShouldBe(index);
                markerScrollViewer.Offset.Y.ShouldBe(thirdOffset, 0.5d);
            }

            contentScrollViewer.Offset = new Vector(0d, 2d * 120d + 12d);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(2);
            var reverseOffset = markerScrollViewer.Offset.Y;
            reverseOffset.ShouldBeLessThan(thirdOffset);
            AssertTrackMotionProgress(trackTransform, reverseOffset - thirdOffset);

            host.Coordinator.RequestNavigation(9);
            Dispatcher.UIThread.RunJobs();

            trackTransform.Y.ShouldBe(0d, 0.01d);
        });
    }

    [Fact]
    public void HorizontalAutomaticFollow_ShouldAnimateSuccessiveMarkersOnXAxis()
    {
        var host = CreateHorizontalHost(30, 160d);
        host.MaxVisibleMarkerCount = 6;

        ShowInWindow(host, () =>
        {
            var navigator = GetNavigator(host);
            var markerScrollViewer = GetNavigatorScrollViewer(navigator);
            var contentScrollViewer = GetContentScrollViewer(host);
            var trackTransform = navigator.GetVisualDescendants()
                .OfType<Avalonia.Controls.Presenters.ItemsPresenter>()
                .Single(presenter => presenter.Name == ScrollMarkerNavigator.PartItemsPresenter)
                .RenderTransform.ShouldBeOfType<TranslateTransform>();

            host.Coordinator.EnterAutomatic();
            for (var index = 1; index < 6; index++)
            {
                contentScrollViewer.Offset = new Vector(index * 160d + 12d, 0d);
                Dispatcher.UIThread.RunJobs();
            }

            contentScrollViewer.Offset = new Vector(6d * 160d + 12d, 0d);
            Dispatcher.UIThread.RunJobs();
            var firstOffset = markerScrollViewer.Offset.X;
            firstOffset.ShouldBeGreaterThan(0d);
            AssertTrackMotionProgress(trackTransform, firstOffset, horizontal: true);

            contentScrollViewer.Offset = new Vector(7d * 160d + 12d, 0d);
            Dispatcher.UIThread.RunJobs();
            var secondOffset = markerScrollViewer.Offset.X;
            secondOffset.ShouldBeGreaterThan(firstOffset);
            AssertTrackMotionProgress(trackTransform, secondOffset - firstOffset, horizontal: true);
        });
    }

    private static void AssertTrackMotionProgress(
        TranslateTransform trackTransform,
        double startTranslation,
        bool horizontal = false)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(60);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
        var intermediate = horizontal ? trackTransform.X : trackTransform.Y;
        if (startTranslation > 0d)
        {
            intermediate.ShouldBeGreaterThan(0d);
            intermediate.ShouldBeLessThan(startTranslation);
        }
        else
        {
            intermediate.ShouldBeLessThan(0d);
            intermediate.ShouldBeGreaterThan(startTranslation);
        }

        Thread.Sleep(140);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
        var completed = horizontal ? trackTransform.X : trackTransform.Y;
        completed.ShouldBe(0d, 0.5d);
    }

    [Fact]
    public void Navigator_ShouldKeepTooltipMetadataButDisableTooltipPresentation()
    {
        var host = CreateVerticalHost(30, 120d);
        host.MaxVisibleMarkerCount = 6;

        ShowInWindow(host, () =>
        {
            var navigator = GetNavigator(host);
            var marker = GetMarker(host, 0);
            var previous = GetNavigationButton(navigator, ScrollMarkerNavigator.PartPreviousMarkerButton);
            var next = GetNavigationButton(navigator, ScrollMarkerNavigator.PartNextMarkerButton);
            var contentText = GetContentScrollViewer(host)
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .First();

            ToolTip.GetTip(marker).ShouldBe("Section 0");
            ToolTip.GetTip(previous).ShouldBe("Previous marker");
            ToolTip.GetTip(next).ShouldBe("Next marker");
            ToolTip.GetServiceEnabled(marker).ShouldBeFalse();
            ToolTip.GetServiceEnabled(previous).ShouldBeFalse();
            ToolTip.GetServiceEnabled(next).ShouldBeFalse();
            ToolTip.GetServiceEnabled(contentText).ShouldBeTrue();
        });
    }

    [Fact]
    public void NavigationButtons_ShouldAnimateWhenCrossingEitherVisibleWindowEdge()
    {
        var host = CreateVerticalHost(30, 120d);
        host.MaxVisibleMarkerCount = 6;

        ShowInWindow(host, () =>
        {
            var navigator = GetNavigator(host);
            var markerScrollViewer = GetNavigatorScrollViewer(navigator);
            var previous = GetNavigationButton(navigator, ScrollMarkerNavigator.PartPreviousMarkerButton);
            var next = GetNavigationButton(navigator, ScrollMarkerNavigator.PartNextMarkerButton);
            var contentScrollViewer = GetContentScrollViewer(host);
            var trackTransform = navigator.GetVisualDescendants()
                .OfType<Avalonia.Controls.Presenters.ItemsPresenter>()
                .Single(presenter => presenter.Name == ScrollMarkerNavigator.PartItemsPresenter)
                .RenderTransform.ShouldBeOfType<TranslateTransform>();

            host.Coordinator.RequestNavigation(5);
            Dispatcher.UIThread.RunJobs();
            host.Coordinator.ActiveIndex.ShouldBe(5);
            host.Coordinator.BeginNavigatorBrowse();
            host.Coordinator.NavigatorFollowMode.ShouldBe(NavigatorFollowMode.Browse);

            ClickControl(next);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(6);
            host.Coordinator.NavigatorFollowMode.ShouldBe(NavigatorFollowMode.Follow);
            contentScrollViewer.Offset.Y.ShouldBe(720d, 0.5d);
            var endEdgeOffset = markerScrollViewer.Offset.Y;
            endEdgeOffset.ShouldBeGreaterThan(0d);
            AssertTrackMotionProgress(trackTransform, endEdgeOffset);

            for (var expectedIndex = 5; expectedIndex >= 1; expectedIndex--)
            {
                ClickControl(previous);
                Dispatcher.UIThread.RunJobs();
                host.Coordinator.ActiveIndex.ShouldBe(expectedIndex);
                markerScrollViewer.Offset.Y.ShouldBeGreaterThan(0d);
                trackTransform.Y.ShouldBe(0d, 0.01d);
            }

            var startEdgeOffset = markerScrollViewer.Offset.Y;
            ClickControl(previous);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(0);
            contentScrollViewer.Offset.Y.ShouldBe(0d, 0.5d);
            markerScrollViewer.Offset.Y.ShouldBe(0d, 0.5d);
            AssertTrackMotionProgress(trackTransform, -startEdgeOffset);
        });
    }

    [Fact]
    public void NavigationButtons_ShouldRespectFirstAndLastMarkerBoundaries()
    {
        var host = CreateVerticalHost(30, 120d);
        host.MaxVisibleMarkerCount = 6;

        ShowInWindow(host, () =>
        {
            var navigator = GetNavigator(host);
            var previous = GetNavigationButton(navigator, ScrollMarkerNavigator.PartPreviousMarkerButton);
            var next = GetNavigationButton(navigator, ScrollMarkerNavigator.PartNextMarkerButton);

            previous.IsEnabled.ShouldBeFalse();
            next.IsEnabled.ShouldBeTrue();

            host.Coordinator.RequestNavigation(29);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(29);
            previous.IsEnabled.ShouldBeTrue();
            next.IsEnabled.ShouldBeFalse();

            ClickControl(previous);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(28);
            next.IsEnabled.ShouldBeTrue();
        });
    }

    [Fact]
    public void HorizontalNextButton_ShouldNavigateToAdjacentSectionOnXAxis()
    {
        var host = CreateHorizontalHost(30, 160d);
        host.MaxVisibleMarkerCount = 6;

        ShowInWindow(host, () =>
        {
            var navigator = GetNavigator(host);
            var markerScrollViewer = GetNavigatorScrollViewer(navigator);
            var previous = GetNavigationButton(navigator, ScrollMarkerNavigator.PartPreviousMarkerButton);
            var next = GetNavigationButton(navigator, ScrollMarkerNavigator.PartNextMarkerButton);

            ((AtomUI.Desktop.Controls.Button)previous).Icon
                .ShouldBeOfType<AtomUI.Icons.AntDesign.LeftOutlined>();
            ((AtomUI.Desktop.Controls.Button)next).Icon
                .ShouldBeOfType<AtomUI.Icons.AntDesign.RightOutlined>();

            ClickControl(next);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(1);
            GetContentScrollViewer(host).Offset.X.ShouldBe(160d, 0.5d);
            markerScrollViewer.Offset.Y.ShouldBe(0d, 0.5d);
        });
    }

    [Fact]
    public void HorizontalNavigator_ShouldMapVerticalMouseWheelToHorizontalBrowse()
    {
        var host = CreateHorizontalHost(30, 160d);
        host.MaxVisibleMarkerCount = 6;

        ShowInWindow(host, () =>
        {
            var navigator = GetNavigator(host);
            var markerScrollViewer = GetNavigatorScrollViewer(navigator);
            var contentScrollViewer = GetContentScrollViewer(host);

            markerScrollViewer.Offset.X.ShouldBe(0d, 0.5d);
            WheelControl(markerScrollViewer, new Vector(0d, -1d));
            Dispatcher.UIThread.RunJobs();

            markerScrollViewer.Offset.X.ShouldBe(
                navigator.EffectiveMarkerSlotExtent,
                0.5d);
            markerScrollViewer.Offset.Y.ShouldBe(0d, 0.5d);
            contentScrollViewer.Offset.X.ShouldBe(0d, 0.5d);
            host.Coordinator.ActiveIndex.ShouldBe(0);
            host.Coordinator.NavigatorFollowMode.ShouldBe(NavigatorFollowMode.Browse);

            WheelControl(markerScrollViewer, new Vector(0d, 1d));
            Dispatcher.UIThread.RunJobs();

            markerScrollViewer.Offset.X.ShouldBe(0d, 0.5d);
            contentScrollViewer.Offset.X.ShouldBe(0d, 0.5d);
        });
    }

    [Fact]
    public void GeometryCapacity_ShouldNotChangeAdjacentNavigationStep()
    {
        var host = CreateVerticalHost(20, 48d);
        host.MaxVisibleMarkerCount = 6;

        ShowInWindow(host, () =>
        {
            var navigator = GetNavigator(host);
            var next = GetNavigationButton(navigator, ScrollMarkerNavigator.PartNextMarkerButton);

            navigator.EffectiveVisibleMarkerCount.ShouldBeLessThan(6);
            ClickControl(next);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(1);
            GetContentScrollViewer(host).Offset.Y.ShouldBe(48d, 0.5d);
        }, height: 160d);
    }

    private static ScrollMarkerView CreateVerticalHost(int count, double sectionHeight)
    {
        var content = new StackPanel();
        for (var index = 0; index < count; index++)
        {
            content.Children.Add(new ScrollMarkerSection
            {
                AnchorKey = $"section-{index}",
                Label = $"Section {index}",
                Height = sectionHeight,
                Content = new TextBlock { Text = $"Content {index}" }
            });
        }

        return new ScrollMarkerView { Content = content };
    }

    private static ScrollMarkerView CreateHorizontalHost(int count, double sectionWidth)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        for (var index = 0; index < count; index++)
        {
            content.Children.Add(new ScrollMarkerSection
            {
                AnchorKey = $"section-{index}",
                Label = $"Section {index}",
                Width = sectionWidth,
                Content = new TextBlock { Text = $"Content {index}" }
            });
        }

        return new ScrollMarkerView
        {
            Orientation = Orientation.Horizontal,
            Content = content
        };
    }

    private static ScrollMarkerItem GetMarker(ScrollMarkerView host, int index)
    {
        var navigator = host.GetVisualDescendants().OfType<ScrollMarkerNavigator>().Single();
        navigator.ScrollIntoView(index);
        Dispatcher.UIThread.RunJobs();

        return host.GetVisualDescendants()
            .OfType<ScrollMarkerItem>()
            .Single(marker => marker.Tag is int markerIndex && markerIndex == index);
    }

    private static ScrollMarkerNavigator GetNavigator(ScrollMarkerView host)
    {
        return host.GetVisualDescendants().OfType<ScrollMarkerNavigator>().Single();
    }

    private static AtomUIScrollViewer GetNavigatorScrollViewer(ScrollMarkerNavigator navigator)
    {
        return navigator.GetVisualDescendants()
            .OfType<AtomUIScrollViewer>()
            .Single(viewer => viewer.Name == ScrollMarkerNavigator.PartScrollViewer);
    }

    private static Button GetNavigationButton(ScrollMarkerNavigator navigator, string name)
    {
        return navigator.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Name == name);
    }

    private static int CountVisibleMarkers(
        ScrollMarkerNavigator navigator,
        AtomUIScrollViewer markerScrollViewer)
    {
        var viewportRect = new Rect(markerScrollViewer.Viewport);
        return navigator.GetVisualDescendants()
            .OfType<ScrollMarkerItem>()
            .Count(marker =>
            {
                var origin = marker.TranslatePoint(default, markerScrollViewer);
                if (origin is null)
                {
                    return false;
                }

                var markerRect = new Rect(origin.Value, marker.Bounds.Size);
                return markerRect.Left < viewportRect.Right &&
                       markerRect.Right > viewportRect.Left &&
                       markerRect.Top < viewportRect.Bottom &&
                       markerRect.Bottom > viewportRect.Top;
            });
    }

    private static void ClickControl(Control control)
    {
        var window = TopLevel.GetTopLevel(control).ShouldBeOfType<Window>();
        var localCenter = new Point(control.Bounds.Width / 2d, control.Bounds.Height / 2d);
        var windowPoint = control.TranslatePoint(localCenter, window);
        windowPoint.ShouldNotBeNull();
        var visualPath = string.Join(
            " -> ",
            control.GetVisualAncestors().Select(visual => $"{visual.GetType().Name}:{visual.Bounds}"));
        new Rect(window.Bounds.Size).Contains(windowPoint.Value).ShouldBeTrue(
            $"Control {control.Bounds} center {windowPoint.Value} must be inside window {window.Bounds}. " +
            $"Visual path: {visualPath}");

        window.MouseDown(windowPoint.Value, MouseButton.Left);
        window.MouseUp(windowPoint.Value, MouseButton.Left);
    }

    private static void WheelControl(Control control, Vector delta)
    {
        var window = TopLevel.GetTopLevel(control).ShouldBeOfType<Window>();
        var localCenter = new Point(control.Bounds.Width / 2d, control.Bounds.Height / 2d);
        var windowPoint = control.TranslatePoint(localCenter, window);
        windowPoint.ShouldNotBeNull();
        new Rect(window.Bounds.Size).Contains(windowPoint.Value).ShouldBeTrue();

        window.MouseWheel(windowPoint.Value, delta);
    }

    private static void ClickMarker(
        ScrollMarkerItem marker,
        MouseButton button = MouseButton.Left,
        bool releaseOutside = false)
    {
        var window = TopLevel.GetTopLevel(marker).ShouldBeOfType<Window>();
        var localCenter = new Point(marker.Bounds.Width / 2d, marker.Bounds.Height / 2d);
        var windowPoint = marker.TranslatePoint(localCenter, window);
        windowPoint.ShouldNotBeNull();
        new Rect(window.Bounds.Size).Contains(windowPoint.Value).ShouldBeTrue(
            $"Marker center {windowPoint.Value} must be inside window {window.Bounds}.");

        window.MouseDown(windowPoint.Value, button);
        var releasePoint = releaseOutside
            ? new Point(window.Bounds.Width - 4d, window.Bounds.Height - 4d)
            : windowPoint.Value;
        window.MouseUp(releasePoint, button);
    }

    private static AtomUIScrollViewer GetContentScrollViewer(ScrollMarkerView host)
    {
        return host.GetVisualDescendants()
            .OfType<AtomUIScrollViewer>()
            .Single(viewer => viewer.Name == ScrollMarkerView.PartContentScrollViewer);
    }

    private static void ShowInWindow(Control content, Action assertion, double height = 240d)
    {
        var window = new Window { Width = 640, Height = height, Content = content };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            assertion();
        }
        finally
        {
            window.Close();
        }
    }
}
