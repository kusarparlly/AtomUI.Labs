using System.Diagnostics;
using AtomUI;
using AtomUI.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AtomUI.Labs.Controls.ScrollMarker.Performance;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = RunnerOptions.Parse(args);
        AppBuilder.Configure<PerformanceApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .SetupWithoutStarting();

        var source = Enumerable.Range(0, options.ItemCount)
            .Select(index => new PerformanceItem($"turn-{index}", index % 11 == 0 ? 96d : 48d))
            .ToArray();
        var baseline = MeasureBaseline(source, options.OperationCount, options.Seed);
        var candidate = MeasureCandidate(source, options.OperationCount, options.Seed);

        PrintResult(options, baseline, candidate);
        var deterministicPass =
            candidate.DescriptorCount == options.ItemCount &&
            candidate.RealizedContentCount is > 0 and < 256 &&
            candidate.RealizedMarkerCount is > 0 and < 256 &&
            candidate.FinalNavigationPhase == MarkerNavigationPhase.Idle;
        var relativePass = !options.EnforceRelativeGates ||
            (candidate.SteadyScrollMedianMs <= baseline.SteadyScrollMedianMs * 1.20d &&
             candidate.SteadyScrollP95Ms <= baseline.SteadyScrollP95Ms * 1.25d &&
             candidate.FarNavigationP95Ms <= baseline.FarNavigationP95Ms * 1.25d &&
             candidate.SteadyScrollAllocatedBytes <= baseline.SteadyScrollAllocatedBytes * 1.25d);

        Console.WriteLine($"Deterministic virtualization gates: {(deterministicPass ? "PASS" : "FAIL")}");
        Console.WriteLine(
            $"Relative performance gates: {(options.EnforceRelativeGates ? (relativePass ? "PASS" : "FAIL") : "NOT ENFORCED (use --relative-gates for a single-process diagnostic)")}");
        return deterministicPass && relativePass ? 0 : 1;
    }

    private static PerformanceResult MeasureBaseline(
        IReadOnlyList<PerformanceItem> source,
        int operationCount,
        int seed)
    {
        var itemsControl = new ItemsControl
        {
            ItemsSource = source,
            ItemTemplate = CreateItemTemplate(),
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = 0.5d }),
            Template = new FuncControlTemplate<ItemsControl>((owner, _) =>
                new Avalonia.Controls.ScrollViewer
                {
                    Name = "PART_BaselineScrollViewer",
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        ItemsPanel = owner.ItemsPanel
                    }
                })
        };

        return Measure(
            itemsControl,
            source.Count,
            operationCount,
            seed,
            () => itemsControl.GetVisualDescendants()
                .OfType<Avalonia.Controls.ScrollViewer>()
                .Single(viewer => viewer.Name == "PART_BaselineScrollViewer"),
            index => itemsControl.ScrollIntoView(index),
            () => itemsControl.GetVisualDescendants().OfType<ContentPresenter>().Count(),
            () => 0,
            () => 0,
            () => MarkerNavigationPhase.Idle);
    }

    private static PerformanceResult MeasureCandidate(
        IReadOnlyList<PerformanceItem> source,
        int operationCount,
        int seed)
    {
        var host = new ScrollMarkerItemsView
        {
            ItemsSource = source,
            ItemTemplate = CreateItemTemplate(),
            AnchorKeyBinding = new Binding(nameof(PerformanceItem.AnchorKey))
        };

        return Measure(
            host,
            source.Count,
            operationCount,
            seed,
            () => host.GetVisualDescendants()
                .OfType<AtomUI.Desktop.Controls.ScrollViewer>()
                .Single(viewer => viewer.Name == ScrollMarkerItemsView.PartContentScrollViewer),
            index => host.Coordinator.RequestNavigation(index),
            () => host.GetVisualDescendants().OfType<ScrollMarkerSectionContainer>().Count(),
            () => host.GetVisualDescendants().OfType<ScrollMarkerItem>().Count(),
            () => host.DescriptorCount,
            () => host.Coordinator.NavigationPhase);
    }

    private static PerformanceResult Measure(
        Control control,
        int logicalItemCount,
        int operationCount,
        int seed,
        Func<Avalonia.Controls.ScrollViewer> getScrollViewer,
        Action<int> navigate,
        Func<int> getRealizedContentCount,
        Func<int> getRealizedMarkerCount,
        Func<int> getDescriptorCount,
        Func<MarkerNavigationPhase> getNavigationPhase)
    {
        ForceFullCollection();
        var allocatedBeforeInitialization = GC.GetAllocatedBytesForCurrentThread();
        var initialization = Stopwatch.StartNew();
        var window = new Avalonia.Controls.Window { Width = 960, Height = 640, Content = control };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            initialization.Stop();
            var initializationAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeInitialization;
            var scrollViewer = getScrollViewer();
            var maxOffset = Math.Max(0d, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);

            for (var index = 0; index < 20; index++)
            {
                scrollViewer.Offset = new Vector(0d, maxOffset * index / 20d);
                Dispatcher.UIThread.RunJobs();
            }

            var steady = MeasureOperations(operationCount, index =>
            {
                scrollViewer.Offset = new Vector(0d, maxOffset * ((index * 17) % operationCount) / operationCount);
                Dispatcher.UIThread.RunJobs();
            });

            var random = new Random(seed);
            var far = MeasureOperations(operationCount, _ =>
            {
                navigate(random.Next(0, logicalItemCount));
                Dispatcher.UIThread.RunJobs();
            });

            return new PerformanceResult(
                initialization.Elapsed.TotalMilliseconds,
                initializationAllocated,
                steady.MedianMs,
                steady.P95Ms,
                steady.AllocatedBytes,
                far.P95Ms,
                getRealizedContentCount(),
                getRealizedMarkerCount(),
                getDescriptorCount(),
                getNavigationPhase());
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static OperationResult MeasureOperations(int count, Action<int> operation)
    {
        var samples = new double[count];
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < count; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            operation(index);
            stopwatch.Stop();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        return new OperationResult(
            samples[count / 2],
            samples[Math.Min(count - 1, (int)Math.Ceiling(count * 0.95d) - 1)],
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static FuncDataTemplate<PerformanceItem> CreateItemTemplate()
    {
        return new FuncDataTemplate<PerformanceItem>((item, _) =>
            new Border { Height = item?.Height ?? 48d });
    }

    private static void PrintResult(
        RunnerOptions options,
        PerformanceResult baseline,
        PerformanceResult candidate)
    {
        Console.WriteLine($"ScrollMarker Scheme A diagnostic runner: N={options.ItemCount}, operations={options.OperationCount}, seed={options.Seed}");
        Console.WriteLine("Scenario    Init ms  Init MB  Scroll median  Scroll P95  Scroll MB  Far-nav P95  Content K  Marker K  Descriptors");
        PrintRow("Baseline", baseline);
        PrintRow("Candidate", candidate);
    }

    private static void PrintRow(string name, PerformanceResult result)
    {
        Console.WriteLine(
            $"{name,-11}{result.InitializationMs,8:0.00}{result.InitializationAllocatedBytes / 1024d / 1024d,9:0.00}" +
            $"{result.SteadyScrollMedianMs,15:0.000}{result.SteadyScrollP95Ms,12:0.000}" +
            $"{result.SteadyScrollAllocatedBytes / 1024d / 1024d,11:0.00}{result.FarNavigationP95Ms,13:0.000}" +
            $"{result.RealizedContentCount,11}{result.RealizedMarkerCount,10}{result.DescriptorCount,13}");
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record PerformanceItem(string AnchorKey, double Height);

    private sealed record OperationResult(double MedianMs, double P95Ms, long AllocatedBytes);

    private sealed record PerformanceResult(
        double InitializationMs,
        long InitializationAllocatedBytes,
        double SteadyScrollMedianMs,
        double SteadyScrollP95Ms,
        long SteadyScrollAllocatedBytes,
        double FarNavigationP95Ms,
        int RealizedContentCount,
        int RealizedMarkerCount,
        int DescriptorCount,
        MarkerNavigationPhase FinalNavigationPhase);

    private sealed record RunnerOptions(int ItemCount, int OperationCount, int Seed, bool EnforceRelativeGates)
    {
        internal static RunnerOptions Parse(string[] args)
        {
            var itemCount = 10_000;
            var operationCount = 250;
            var seed = 173;
            var enforceRelativeGates = false;
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] == "--items" && index + 1 < args.Length && int.TryParse(args[++index], out var items))
                {
                    itemCount = Math.Max(100, items);
                }
                else if (args[index] == "--operations" && index + 1 < args.Length && int.TryParse(args[++index], out var operations))
                {
                    operationCount = Math.Max(10, operations);
                }
                else if (args[index] == "--seed" && index + 1 < args.Length && int.TryParse(args[++index], out var parsedSeed))
                {
                    seed = parsedSeed;
                }
                else if (args[index] == "--relative-gates")
                {
                    enforceRelativeGates = true;
                }
                else if (args[index] == "--formal")
                {
                    throw new ArgumentException(
                        "--formal is intentionally unavailable: the formal contract requires clean multi-process repetitions, environment capture, long-soak and desktop validation. Use --relative-gates only for a local diagnostic.");
                }
            }

            return new RunnerOptions(itemCount, operationCount, seed, enforceRelativeGates);
        }
    }
}

internal sealed class PerformanceApplication : Application
{
    public override void Initialize()
    {
        this.UseAtomUI(builder => builder.UseDesktopControls().UseScrollMarker());
    }
}
