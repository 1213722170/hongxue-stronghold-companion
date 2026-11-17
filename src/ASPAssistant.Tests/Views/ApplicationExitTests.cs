using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

public class ApplicationExitTests
{
    [Theory]
    [InlineData("button")]
    [InlineData("window")]
    [InlineData("dialog")]
    public async Task ClosingMainWindow_ExitsProcessWithHiddenOverlay(string action)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add(typeof(AppExitProbe).Assembly.Location);
        start.ArgumentList.Add("--app-exit-probe");
        start.ArgumentList.Add(action);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            process.ExitCode.Should().Be(0,
                "choosing exit must finish without waiting for the hidden overlay; " +
                "23 means a confirmation appeared, 24 means the process stayed alive, 25 means OnExit was skipped. " +
                await output + await errors);
        }
        finally
        {
            // Only terminate the isolated probe that this test created.
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }
}

internal static class AppExitProbe
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--app-exit-probe") return 2;
        var folder = Path.Combine(Path.GetTempPath(), "app-exit-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            var app = new ProbeApplication(args[1], folder);
            // WPF cannot load App.xaml into a derived Application from another assembly.
            foreach (var resource in new[] { "Themes/Terminal.Dark.xaml", "Styles.xaml", "Logo.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/ASPAssistant.App;component/Resources/{resource}", UriKind.Relative)
                });
            var code = app.Run();
            return app.ExitObserved ? code : 25;
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    // Exercise the real App resources, shutdown policy, panel and tray while keeping
    // startup independent of the user's game, settings, native OCR and single-instance mutex.
    private sealed class ProbeApplication(string action, string folder) : ASPAssistant.App.App
    {
        public bool ExitObserved { get; private set; }
        private AssistantTray? _tray;

        protected override void OnStartup(StartupEventArgs e)
            => Dispatcher.BeginInvoke(CreateWindows, DispatcherPriority.ApplicationIdle);

        private void CreateWindows()
        {
            var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
            var panel = new SidePanelWindow(new OperatorBrowseViewModel(), new EquipmentBrowseViewModel(),
                new TrackingViewModel(), new GameStateViewModel(), new BanViewModel(), new EnemyViewModel(),
                new LineupViewModel(new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules), calculator),
                new LineupShareService(calculator.EquipmentRules, Path.Combine(folder, "share")), [], [], [],
                new SettingsManager(folder), GameShopTheme.Terminal, new SidePanelPositionSettings { FreeMovement = true },
                closeBehavior: action == "dialog" ? MainWindowCloseBehavior.Ask : MainWindowCloseBehavior.Exit)
            {
                ShowActivated = false, ShowInTaskbar = false, Opacity = 0, Left = -20000, Top = -20000
            };
            MainWindow = panel;
            panel.Show();
            var overlay = new Window { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            overlay.Show();
            overlay.Hide();
            _tray = new AssistantTray(panel, overlay);
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timeout.Tick += (_, _) =>
            {
                timeout.Stop();
                Shutdown(Windows.OfType<ConfirmDialog>().Any() ? 23 : 24);
            };
            timeout.Start();
            Dispatcher.BeginInvoke(() =>
            {
                if (action == "dialog")
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        var dialog = Windows.OfType<CloseBehaviorDialog>().Single();
                        ((CheckBox)dialog.FindName("RememberChoice")).IsChecked = true;
                        ((Button)dialog.FindName("ExitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }, DispatcherPriority.ApplicationIdle);
                }
                if (action is "button" or "dialog")
                    Descendants<Button>(panel).Single(b => AutomationProperties.GetName(b) == "关闭")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else SendMessage(new System.Windows.Interop.WindowInteropHelper(panel).Handle,
                    0x0112, (IntPtr)0xF060, IntPtr.Zero); // Alt+F4 / system close.
            }, DispatcherPriority.ApplicationIdle);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (action == "dialog" && File.ReadAllText(Path.Combine(folder, "close-behavior.json")) != "\"Exit\"")
                e.ApplicationExitCode = 26;
            _tray?.Dispose();
            base.OnExit(e);
            ExitObserved = true;
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var item in Descendants<T>(child)) yield return item;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
