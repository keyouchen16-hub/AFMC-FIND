using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Afmc.Find.App;

internal static class UiMotionChecks
{
    private static int passed;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static bool IsAnimating(UIElement element) =>
        DependencyPropertyHelper.GetValueSource(element, UIElement.OpacityProperty).IsAnimated;

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.001;

    private static void CheckPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "afmc-motion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Check(!new Preferences().ReduceMotion && !Preferences.Load(directory).ReduceMotion,
                "new and missing preferences leave motion enabled by default");
            File.WriteAllText(Path.Combine(directory, "preferences.json"),
                "{\"CloseToTray\":false,\"MinimizeToTray\":true,\"RememberHistory\":false}");
            var legacy = Preferences.Load(directory);
            Check(!legacy.ReduceMotion && !legacy.CloseToTray && legacy.MinimizeToTray && !legacy.RememberHistory,
                "legacy settings without ReduceMotion retain window and history preferences");

            var reduced = legacy with { ReduceMotion = true };
            reduced.Save(directory);
            Check(Preferences.Load(directory) == reduced,
                "saving reduced motion preserves every existing preference");
            var restored = reduced with { ReduceMotion = false };
            restored.Save(directory);
            Check(Preferences.Load(directory) == restored,
                "motion can be re-enabled and survives a second settings save");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static async Task WaitForIdleAnimation(UIElement element)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            await Task.Delay(25);
            await element.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        } while (IsAnimating(element) && elapsed.Elapsed < TimeSpan.FromSeconds(2));
    }

    private static async Task<bool> ObserveVisibleTransition(UIElement element, double baseOpacity)
    {
        // BeginAnimation starts on a WPF timing tick, not synchronously at the call site.
        // Observe a visible change before asserting cancellation of an active transition.
        var elapsed = Stopwatch.StartNew();
        do
        {
            await Task.Delay(10);
            if (IsAnimating(element) && element.Opacity < baseOpacity - 0.001 && element.Opacity >= 0)
                return true;
        } while (elapsed.Elapsed < TimeSpan.FromSeconds(2));
        return false;
    }

    public static int Run()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("UI motion checks require the STA test entry point.");
        passed = 0;
        CheckPreferences();
        var previousEnabled = UiMotion.Enabled;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var panel = new Border
        {
            Opacity = 0.87,
            Background = new SolidColorBrush(Color.FromRgb(36, 38, 43)),
            Padding = new Thickness(24),
            Child = new TextBlock { Text = "AFMC FIND · 动态效果检查", Foreground = Brushes.White }
        };
        var window = new Window
        {
            Width = 360,
            Height = 160,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = panel
        };
        var exit = 1;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(panel.IsVisible, "motion fixture is attached to a visible WPF window");
                UiMotion.Enabled = true;
                UiMotion.Reveal(panel);
                if (SystemParameters.ClientAreaAnimation)
                {
                    Check(await ObserveVisibleTransition(panel, 0.87),
                        "reveal visibly fades when system animations are enabled");
                    await WaitForIdleAnimation(panel);
                    Check(!IsAnimating(panel) && Near(panel.Opacity, 0.87),
                        "reveal finishes within a bounded interval and restores the original opacity");

                    UiMotion.Reveal(panel);
                    Check(await ObserveVisibleTransition(panel, 0.87),
                        "a subsequent reveal visibly starts after completion");
                    UiMotion.Enabled = false;
                    Check(!IsAnimating(panel) && Near(panel.Opacity, 0.87),
                        "reducing motion immediately cancels an in-progress transition");
                }
                else
                {
                    Check(!IsAnimating(panel) && Near(panel.Opacity, 0.87),
                        "system reduced-animation preference keeps reveal static");
                    Console.WriteLine("SKIP animated completion/cancellation paths: Windows client-area animations are disabled.");
                    UiMotion.Enabled = false;
                }

                UiMotion.Reveal(panel);
                Check(!IsAnimating(panel) && Near(panel.Opacity, 0.87),
                    "disabled motion reveals content at its intended opacity without a transition");
                await Task.Delay(350);
                Check(!IsAnimating(panel) && Near(panel.Opacity, 0.87),
                    "disabled motion has no delayed fade or stale completion effect");

                UiMotion.Enabled = true;
                UiMotion.Reveal(panel);
                if (SystemParameters.ClientAreaAnimation)
                    Check(await ObserveVisibleTransition(panel, 0.87),
                        "hiding is tested while a visible transition is in progress");
                panel.Visibility = Visibility.Collapsed;
                Check(!panel.IsVisible && !IsAnimating(panel) && Near(panel.Opacity, 0.87),
                    "hiding a panel removes its active opacity transition");
                UiMotion.Reveal(panel);
                Check(!IsAnimating(panel), "revealing hidden content does not start a background transition");
                panel.Visibility = Visibility.Visible;
                await Task.Delay(350);
                Check(panel.IsVisible && !IsAnimating(panel) && Near(panel.Opacity, 0.87),
                    "showing content again does not resume an old hidden transition");

                panel.Opacity = 0.93;
                UiMotion.Reveal(panel);
                if (SystemParameters.ClientAreaAnimation)
                    Check(await ObserveVisibleTransition(panel, 0.93),
                        "content can visibly fade again after being hidden and shown");
                await WaitForIdleAnimation(panel);
                Check(!IsAnimating(panel) && Near(panel.Opacity, 0.93),
                    "a later reveal preserves a changed base opacity");
                Console.WriteLine($"UI MOTION PASSED {passed}");
                exit = 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
            }
            finally
            {
                UiMotion.Enabled = false;
                window.Close();
                UiMotion.Enabled = previousEnabled;
                app.Shutdown(exit);
            }
        };
        app.Run(window);
        return exit;
    }
}
