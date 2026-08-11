using System.Collections.ObjectModel;
using AtomUIScrollViewer = AtomUI.Desktop.Controls.ScrollViewer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Shouldly;
using Xunit;

namespace AtomUI.Labs.Controls.ScrollMarker.Tests;

public sealed class ScrollMarkerVirtualItemsContractTests
{
    static ScrollMarkerVirtualItemsContractTests()
    {
        AvaloniaTestApp.EnsureInitialized();
    }

    [Fact]
    public void InitialProjection_ShouldCreateOneLightweightDescriptorPerDataItem()
    {
        var source = new ObservableCollection<TestSectionData>
        {
            new("turn-1", "Question"),
            new("turn-2", null)
        };
        var host = CreateHost(source);

        ShowInWindow(host, () =>
        {
            host.IsFaulted.ShouldBeFalse();
            host.DescriptorCount.ShouldBe(2);
            host.Coordinator.Descriptors[0].Label.ShouldBe("Question");
            host.Coordinator.Descriptors[1].Label.ShouldBe("turn-2");
            host.Coordinator.Descriptors[1].Source.ShouldBeNull();
        });
    }

    [Fact]
    public void EndAppend_ShouldCommitDescriptorsIncrementally()
    {
        var source = new ObservableCollection<TestSectionData>
        {
            new("turn-1", "Question")
        };
        var host = CreateHost(source);

        ShowInWindow(host, () =>
        {
            source.Add(new TestSectionData("turn-2", "Answer"));
            host.IsFaulted.ShouldBeFalse();
            host.DescriptorCount.ShouldBe(2);
            host.Coordinator.Descriptors[1].SourceIndex.ShouldBe(1);
        });
    }

    [Fact]
    public void NonEndMutation_ShouldLatchFaultBeforeAnyDescriptorCommit()
    {
        var source = new ObservableCollection<TestSectionData>
        {
            new("turn-1", null),
            new("turn-2", null)
        };
        var host = CreateHost(source);

        ShowInWindow(host, () =>
        {
            Should.Throw<InvalidOperationException>(() => source.RemoveAt(0));
            host.IsFaulted.ShouldBeTrue();
            host.HostState.ShouldBe(VirtualHostState.Faulted);
            host.HostGeneration.ShouldBe(1);
            host.HostFault.ShouldNotBeNull();
            host.DescriptorCount.ShouldBe(2);
            host.Coordinator.Descriptors.Select(descriptor => descriptor.AnchorKey)
                .ShouldBe(new[] { "turn-1", "turn-2" });

            source.Add(new TestSectionData("turn-3", null));
            host.DescriptorCount.ShouldBe(2);
        });
    }

    [Fact]
    public void DuplicateEndAppend_ShouldFaultWithoutPartialDescriptorCommit()
    {
        var source = new ObservableCollection<TestSectionData>
        {
            new("turn-1", null)
        };
        var host = CreateHost(source);

        ShowInWindow(host, () =>
        {
            Should.Throw<InvalidOperationException>(() =>
                source.Add(new TestSectionData("turn-1", "duplicate")));
            host.IsFaulted.ShouldBeTrue();
            host.DescriptorCount.ShouldBe(1);
        });
    }

    [Fact]
    public void ReplacingAcceptedItemsSource_ShouldFault()
    {
        var source = new ObservableCollection<TestSectionData> { new("turn-1", null) };
        var host = CreateHost(source);

        ShowInWindow(host, () =>
        {
            Should.Throw<InvalidOperationException>(() =>
                host.ItemsSource = new ObservableCollection<TestSectionData> { new("turn-2", null) });
            host.IsFaulted.ShouldBeTrue();
            host.DescriptorCount.ShouldBe(1);
        });
    }

    [Fact]
    public void LargeSource_ShouldRealizeOnlyAViewportBoundedSubset()
    {
        var source = new ObservableCollection<TestSectionData>(
            Enumerable.Range(0, 1_000).Select(index => new TestSectionData($"turn-{index}", null)));
        var host = CreateHost(source);

        ShowInWindow(host, () =>
        {
            var realizedSections = host.GetVisualDescendants()
                .OfType<ScrollMarkerSectionContainer>()
                .ToArray();

            host.DescriptorCount.ShouldBe(1_000);
            realizedSections.Length.ShouldBeGreaterThan(0);
            realizedSections.Length.ShouldBeLessThan(100);
            var realizedMarkers = host.GetVisualDescendants().OfType<ScrollMarkerItem>().ToArray();
            var navigator = host.GetVisualDescendants().OfType<ScrollMarkerNavigator>().Single();
            realizedMarkers.Length.ShouldBeGreaterThan(0);
            realizedMarkers.Length.ShouldBeLessThan(100);
            realizedMarkers.All(marker =>
                    Math.Abs(marker.Height - navigator.EffectiveMarkerSlotExtent) <= 0.01d)
                .ShouldBeTrue();
            navigator.EffectiveMarkerSlotExtent.ShouldBeGreaterThanOrEqualTo(24d);
            host.GetVisualDescendants().OfType<ScrollMarkerItemsPanel>().Single().CacheLength.ShouldBe(0.5d);
            host.GetVisualDescendants().OfType<ScrollMarkerTrackPanel>().Single().CacheLength.ShouldBe(0.5d);
        }, height: 320);
    }

    [Fact]
    public void MaxVisibleMarkerCount_ShouldConfigureSharedNavigatorAndNavigateToAdjacentItem()
    {
        var source = new ObservableCollection<TestSectionData>(
            Enumerable.Range(0, 30).Select(index => new TestSectionData($"turn-{index}", null)));
        var host = CreateHost(source);
        host.MaxVisibleMarkerCount = 6;

        ShowInWindow(host, () =>
        {
            var navigator = host.GetVisualDescendants().OfType<ScrollMarkerNavigator>().Single();
            var previous = navigator.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Name == ScrollMarkerNavigator.PartPreviousMarkerButton);
            var next = navigator.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Name == ScrollMarkerNavigator.PartNextMarkerButton);

            navigator.EffectiveVisibleMarkerCount.ShouldBe(6);
            navigator.HasMarkerOverflow.ShouldBeTrue();
            previous.IsVisible.ShouldBeTrue();
            next.IsVisible.ShouldBeTrue();
            previous.IsEnabled.ShouldBeFalse();
            next.IsEnabled.ShouldBeTrue();

            ClickControl(next);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(1);
            host.Coordinator.NavigatorFollowMode.ShouldBe(NavigatorFollowMode.Follow);
            GetContentScrollViewer(host).Offset.Y.ShouldBe(48d, 0.5d);
            previous.IsEnabled.ShouldBeTrue();
        }, height: 320);
    }

    [Fact]
    public void FarMarkerNavigation_ShouldRealizeTargetAndLetLatestRequestWin()
    {
        var source = new ObservableCollection<TestSectionData>(
            Enumerable.Range(0, 1_000).Select(index => new TestSectionData($"turn-{index}", null)));
        var host = CreateHost(source);

        ShowInWindow(host, () =>
        {
            host.Coordinator.RequestNavigation(700);
            host.Coordinator.RequestNavigation(900);
            Dispatcher.UIThread.RunJobs();

            host.Coordinator.ActiveIndex.ShouldBe(900);
            host.Coordinator.NavigationPhase.ShouldBe(MarkerNavigationPhase.Idle);
            host.Coordinator.SelectionMode.ShouldBe(MarkerSelectionMode.Explicit);
            host.Coordinator.LastTermination.ShouldNotBeNull();
            host.Coordinator.LastTermination!.Value.Completed.ShouldBeTrue();
            host.GetVisualDescendants()
                .OfType<ScrollMarkerSectionContainer>()
                .Any(container => container.SourceIndex == 900)
                .ShouldBeTrue();
            host.GetVisualDescendants()
                .OfType<ScrollMarkerItem>()
                .Any(marker => marker.Tag is 900)
                .ShouldBeTrue();
        }, height: 320);
    }

    [Fact]
    public void NavigatorVisibility_ShouldSwitchOnlyAfterSecondDescriptorCommits()
    {
        var source = new ObservableCollection<TestSectionData> { new("turn-1", null) };
        var host = CreateHost(source);

        ShowInWindow(host, () =>
        {
            var navigator = host.GetVisualDescendants().OfType<ScrollMarkerNavigator>().Single();
            navigator.IsVisible.ShouldBeFalse();

            source.Add(new TestSectionData("turn-2", null));
            Dispatcher.UIThread.RunJobs();

            navigator.IsVisible.ShouldBeTrue();
        });
    }

    [Fact]
    public void InvalidInitialBatch_ShouldFaultBeforePublishingDescriptors()
    {
        var source = new ObservableCollection<TestSectionData>
        {
            new("turn-1", null),
            new("turn-1", "duplicate")
        };
        var host = CreateHost(source);
        var window = new Window { Width = 640, Height = 240, Content = host };

        try
        {
            Should.Throw<InvalidOperationException>(() => window.Show());
            host.IsFaulted.ShouldBeTrue();
            host.DescriptorCount.ShouldBe(0);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ControlDataItem_ShouldBeRejectedWithIndexAndActualType()
    {
        var host = new ScrollMarkerItemsView
        {
            AnchorKeyBinding = new Binding("Tag"),
            ItemTemplate = new FuncDataTemplate<object>((_, _) => new Border()),
            ItemsSource = new object[] { new Border { Tag = "turn-1" } }
        };
        var window = new Window { Width = 640, Height = 240, Content = host };

        try
        {
            var exception = Should.Throw<InvalidOperationException>(() => window.Show());
            exception.Message.ShouldContain(nameof(ScrollMarkerItemsView));
            exception.Message.ShouldContain("[0]");
            exception.Message.ShouldContain(typeof(Border).FullName!);
            host.IsFaulted.ShouldBeTrue();
            host.DescriptorCount.ShouldBe(0);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void NullItemsSource_ShouldAllowOneLaterInitialSourceEstablishment()
    {
        var host = new ScrollMarkerItemsView
        {
            AnchorKeyBinding = new Binding(nameof(TestSectionData.AnchorKey)),
            LabelBinding = new Binding(nameof(TestSectionData.Label)),
            ItemTemplate = new FuncDataTemplate<TestSectionData>((_, _) => new Border { Height = 48 })
        };

        ShowInWindow(host, () =>
        {
            host.DescriptorCount.ShouldBe(0);
            host.ItemsSource = new ObservableCollection<TestSectionData>
            {
                new("turn-1", null),
                new("turn-2", null)
            };
            Dispatcher.UIThread.RunJobs();

            host.IsFaulted.ShouldBeFalse();
            host.DescriptorCount.ShouldBe(2);
        });
    }

    [Fact]
    public void PreloadedHistoryAtStart_ShouldNotFollowAnEndAppend()
    {
        var source = new ObservableCollection<TestSectionData>(
            Enumerable.Range(0, 20).Select(index => new TestSectionData($"turn-{index}", null)));
        var host = CreateHost(source);

        ShowInWindow(host, () =>
        {
            var scrollViewer = GetContentScrollViewer(host);
            host.IsFollowingContentEnd.ShouldBeFalse();
            scrollViewer.Offset.Y.ShouldBe(0d, 0.5d);

            source.Add(new TestSectionData("turn-20", null));
            Dispatcher.UIThread.RunJobs();

            scrollViewer.Offset.Y.ShouldBe(0d, 0.5d);
            host.IsFollowingContentEnd.ShouldBeFalse();
        });
    }

    [Fact]
    public void NavigationToEnd_ShouldArmAutomaticFollowForNextAppend()
    {
        var source = new ObservableCollection<TestSectionData>(
            Enumerable.Range(0, 20).Select(index => new TestSectionData($"turn-{index}", null)));
        var host = CreateHost(source);

        ShowInWindow(host, () =>
        {
            var scrollViewer = GetContentScrollViewer(host);
            host.Coordinator.RequestNavigation(source.Count - 1);
            Dispatcher.UIThread.RunJobs();
            host.IsFollowingContentEnd.ShouldBeTrue();

            source.Add(new TestSectionData("turn-20", null));
            Dispatcher.UIThread.RunJobs();

            var maxOffset = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
            scrollViewer.Offset.Y.ShouldBe(maxOffset, 0.5d);
            host.IsFollowingContentEnd.ShouldBeTrue();
        });
    }

    [Fact]
    public void DisabledEndFollow_ShouldNeverWriteOffsetForAppend()
    {
        var source = new ObservableCollection<TestSectionData>(
            Enumerable.Range(0, 20).Select(index => new TestSectionData($"turn-{index}", null)));
        var host = CreateHost(source);
        host.ContentEndFollowMode = ScrollMarkerContentEndFollowMode.Disabled;

        ShowInWindow(host, () =>
        {
            var scrollViewer = GetContentScrollViewer(host);
            var offsetBeforeAppend = scrollViewer.Offset.Y;
            source.Add(new TestSectionData("turn-20", null));
            Dispatcher.UIThread.RunJobs();

            scrollViewer.Offset.Y.ShouldBe(offsetBeforeAppend, 0.5d);
            host.IsFollowingContentEnd.ShouldBeFalse();
        });
    }

    private static ScrollMarkerItemsView CreateHost(ObservableCollection<TestSectionData> source)
    {
        return new ScrollMarkerItemsView
        {
            AnchorKeyBinding = new Binding(nameof(TestSectionData.AnchorKey)),
            LabelBinding = new Binding(nameof(TestSectionData.Label)),
            ItemTemplate = new FuncDataTemplate<TestSectionData>((_, _) => new Border { Height = 48 }),
            ItemsSource = source
        };
    }

    private static AtomUIScrollViewer GetContentScrollViewer(ScrollMarkerItemsView host)
    {
        return host.GetVisualDescendants()
            .OfType<AtomUIScrollViewer>()
            .Single(viewer => viewer.Name == ScrollMarkerItemsView.PartContentScrollViewer);
    }

    private static void ClickControl(Control control)
    {
        var window = TopLevel.GetTopLevel(control).ShouldBeOfType<Window>();
        var localCenter = new Point(control.Bounds.Width / 2d, control.Bounds.Height / 2d);
        var windowPoint = control.TranslatePoint(localCenter, window);
        windowPoint.ShouldNotBeNull();
        new Rect(window.Bounds.Size).Contains(windowPoint.Value).ShouldBeTrue();

        window.MouseDown(windowPoint.Value, MouseButton.Left);
        window.MouseUp(windowPoint.Value, MouseButton.Left);
    }

    private static void ShowInWindow(Control content, Action assertion, double height = 240)
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

    private sealed record TestSectionData(string AnchorKey, string? Label);
}
