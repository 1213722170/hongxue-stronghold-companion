using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class SidebarUiScaleViewTests
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    public static void Verify(GameShopTheme theme, bool night)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var directory = Path.Combine(Path.GetTempPath(), "sidebar-scale-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsManager(directory);
        var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
        var appearance = new SidebarAppearance { Theme = theme, Night = new() { [theme] = night } };
        var panel = new SidePanelWindow(new OperatorBrowseViewModel(), new EquipmentBrowseViewModel(), new TrackingViewModel(),
            new GameStateViewModel(), new BanViewModel(), new EnemyViewModel(),
            new LineupViewModel(new LineupStore(Path.Combine(directory, "lineups"), calculator.EquipmentRules), calculator),
            new LineupShareService(calculator.EquipmentRules, Path.Combine(directory, "share")), [], [], [], settings, theme,
            new SidePanelPositionSettings { FreeMovement = true }, appearance: appearance)
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Height = 650 };
        panel.Show();
        ThemeManager.Apply(theme, night);
        try
        {
            ((Button)panel.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var surface = (Border)panel.FindName("SettingsPanel");
            Wait(() => surface.Opacity >= 0.99999 && surface.RenderTransform is TranslateTransform move && Math.Abs(move.X) < 0.01);
            surface.BeginAnimation(UIElement.OpacityProperty, null);
            surface.Opacity = 1;
            var move = (TranslateTransform)surface.RenderTransform;
            move.BeginAnimation(TranslateTransform.XProperty, null);
            move.X = 0;
            ((TabControl)panel.FindName("SettingsCategoryList")).SelectedIndex = 1;
            Pump();
            var slider = (Slider)panel.FindName("UiScaleSlider");
            var reset = (Button)panel.FindName("ResetUiScaleButton");
            var root = (Border)panel.FindName("SidebarRoot");
            slider.Minimum.Should().Be(75);
            slider.Maximum.Should().Be(150);
            slider.TickFrequency.Should().Be(5);
            slider.IsSnapToTickEnabled.Should().BeTrue();
            slider.Value.Should().Be(100);
            var captions = Descendants<TextBlock>(surface).Select(text => text.Text).ToArray();
            captions.Should().Contain("主题相关").And.Contain("界面调整");
            captions.Should().NotContain("界面大小");
            var watch = Stopwatch.StartNew();
            for (int percent = 75; percent <= 150; percent += 5)
            {
                slider.Value = percent;
                Pump();
                panel.Width.Should().BeApproximately(310 * percent / 100.0, 1);
                ((ScaleTransform)root.LayoutTransform).ScaleX.Should().BeApproximately(percent / 100.0, 0.0001);
                ((TextBox)panel.FindName("UiScaleValueBox")).Text.Should().Be($"{percent}");
                File.Exists(Path.Combine(directory, "appearance.json")).Should().BeFalse("preview must not write on every slider tick");
                if (percent is 75 or 100 or 150)
                {
                    slider.BringIntoView();
                    Pump();
                    var bounds = slider.TransformToAncestor(root).TransformBounds(new Rect(slider.RenderSize));
                    bounds.Left.Should().BeGreaterThanOrEqualTo(0);
                    bounds.Right.Should().BeLessThanOrEqualTo(root.ActualWidth + 1);
                    reset.IsVisible.Should().BeTrue();
                    var viewport = (FrameworkElement)VisualTreeHelper.GetParent(root);
                    viewport.ActualWidth.Should().BeApproximately(panel.ActualWidth, 1);
                    Save(viewport, $"settings-scale-{theme}-{night}-{percent}");
                }
            }
            var previewMs = watch.Elapsed.TotalMilliseconds;
            slider.Value = 100;
            Pump();
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(track.ActualWidth, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            slider.Value.Should().Be(150, "the themed thumb retains Slider drag behavior");
            track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Wait(() => slider.IsEnabled && File.Exists(Path.Combine(directory, "appearance.json")));
            slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            File.Exists(Path.Combine(directory, "appearance.json.bak")).Should().BeFalse("drag completion and mouse-up persist one gesture once");
            Await(settings.LoadSidebarAppearanceAsync()).UiScale.Should().Be(1.5);

            var handle = new WindowInteropHelper(panel).Handle;
            SendMessage(handle, User32.WM_ENTERSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
            panel.Width = 350 * 1.5;
            Pump();
            SendMessage(handle, User32.WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
            Wait(() => File.Exists(Path.Combine(directory, "side-panel-position.json")));
            Await(settings.LoadSidePanelPositionAsync()).Placement!.WidthDip.Should().BeApproximately(350, 1);

            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(() => slider.IsEnabled);
            Await(settings.LoadSidebarAppearanceAsync()).UiScale.Should().Be(1);
            panel.Width.Should().BeApproximately(350, 1);
            using (var backup = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "appearance.json.bak"))))
                backup.RootElement.GetProperty("uiScale").GetDouble().Should().Be(1.5);
            Slider.IncreaseSmall.Execute(null, slider);
            slider.Value.Should().Be(105, "keyboard adjustment advances to the next 5% tick");
            slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(slider), 0, Key.Right) { RoutedEvent = Keyboard.KeyUpEvent });
            Wait(() => slider.IsEnabled);
            Await(settings.LoadSidebarAppearanceAsync()).UiScale.Should().Be(1.05);
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(() => slider.IsEnabled);
            var scale = User32.GetWindowDpiScale(handle);
            var work = User32.GetMonitorGeometry(0, 0).WorkArea;
            var game = new RECT { Left = work.Left + 20, Top = work.Top + 20, Right = work.Left + work.Width / 2, Bottom = work.Top + work.Height / 2 };
            ((CheckBox)panel.FindName("FreeSidePanelMovementToggle")).IsChecked = false;
            Wait(() => ((CheckBox)panel.FindName("FreeSidePanelMovementToggle")).IsEnabled);
            panel.UpdatePosition(game, false);
            slider.Value = 150;
            Pump();
            var native = User32.ReadWindowRect(handle);
            ((double)native.Width).Should().BeApproximately(350 * 1.5 * scale, 2);
            native.Left.Should().BeGreaterThanOrEqualTo(work.Left);
            native.Right.Should().BeLessThanOrEqualTo(work.Right);
            panel.UpdatePosition(game, true);
            Pump();
            ((double)User32.ReadWindowRect(handle).Width).Should().BeApproximately(native.Width, 2);

            var output = Path.Combine(TestOutput.Directory, "sidebar-scale");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, $"timing-{theme}-{night}.json"), JsonSerializer.Serialize(new { PreviewTicks = 16, PreviewMs = previewMs, PreviewWrites = 0, CommittedWrites = 1 }));
        }
        finally { panel.Close(); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static T Await<T>(Task<T> task)
    {
        Wait(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }
    private static void Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(5); }
        condition().Should().BeTrue("UI interaction finishes within five seconds");
    }

    private static void Save(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(TestOutput.Directory, "sidebar-scale");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
