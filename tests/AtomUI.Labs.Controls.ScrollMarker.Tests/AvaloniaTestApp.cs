using System.Threading;
using AtomUI;
using AtomUI.Desktop.Controls;
using Avalonia;
using Avalonia.Headless;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(AtomUI.Labs.Controls.ScrollMarker.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AtomUI.Labs.Controls.ScrollMarker.Tests;

internal static class AvaloniaTestApp
{
    private static readonly object SyncRoot = new();
    private static int _initialized;

    internal static void EnsureInitialized()
    {
        if (Volatile.Read(ref _initialized) == 1)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_initialized == 1)
            {
                return;
            }

            TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
            Volatile.Write(ref _initialized, 1);
        }
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia();
    }
}

internal sealed class TestApplication : Application
{
    public override void Initialize()
    {
        this.UseAtomUI(builder => builder.UseDesktopControls().UseScrollMarker());
    }
}
