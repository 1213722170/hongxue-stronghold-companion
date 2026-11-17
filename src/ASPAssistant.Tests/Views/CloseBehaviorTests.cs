using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class CloseBehaviorTests
{
    public static void Verify(SidePanelWindow panel, AssistantTray tray, SettingsManager settings, string folder)
    {
        void Request(string action, bool remember)
        {
            Exception? error = null;
            panel.Dispatcher.BeginInvoke(() =>
            {
                var dialog = Application.Current.Windows.OfType<CloseBehaviorDialog>().Single();
                try
                {
                    ((CheckBox)dialog.FindName("RememberChoice")).IsChecked = remember;
                    dialog.UpdateLayout();
                    // Also verify the theme's real dialog layout fits its window.
                    var button = (Button)dialog.FindName("TrayButton");
                    button.ActualWidth.Should().BeGreaterThan(70);
                    var image = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    image.Render(dialog);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    var output = Path.Combine(TestOutput.Directory, "close-behavior-previews");
                    Directory.CreateDirectory(output);
                    using (var stream = File.Create(Path.Combine(output, "dialog.png"))) encoder.Save(stream);
                    if (action == "escape")
                        dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), 0, Key.Escape)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    else ((Button)dialog.FindName(action)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { error = ex; dialog.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            Await(panel.RequestUserCloseAsync());
            if (error is not null) throw error;
        }

        Request("CancelButton", true);
        panel.IsAssistantHidden.Should().BeFalse();
        panel.IsVisible.Should().BeTrue();
        File.Exists(Path.Combine(folder, "close-behavior.json")).Should().BeFalse("cancel must not persist the checkbox");
        Request("escape", true);
        panel.IsAssistantHidden.Should().BeFalse();
        File.Exists(Path.Combine(folder, "close-behavior.json")).Should().BeFalse();

        Request("TrayButton", false);
        panel.IsAssistantHidden.Should().BeTrue();
        File.Exists(Path.Combine(folder, "close-behavior.json")).Should().BeFalse();
        tray.Restore();
        panel.IsAssistantHidden.Should().BeFalse();

        Request("TrayButton", true);
        panel.IsAssistantHidden.Should().BeTrue();
        Await(new SettingsManager(folder).LoadMainWindowCloseBehaviorAsync()).Should().Be(MainWindowCloseBehavior.MinimizeToTray);
        tray.Restore();
        Await(panel.RequestUserCloseAsync()); // Remembered: completes without another modal dialog.
        panel.IsAssistantHidden.Should().BeTrue();
        tray.Restore();

        var selector = (ComboBox)panel.FindName("CloseBehaviorSelector");
        selector.SelectedIndex = (int)MainWindowCloseBehavior.Exit;
        WaitUntil(() => selector.IsEnabled);
        Await(settings.LoadMainWindowCloseBehaviorAsync()).Should().Be(MainWindowCloseBehavior.Exit);
        selector.SelectedIndex = (int)MainWindowCloseBehavior.Ask;
        WaitUntil(() => selector.IsEnabled);
        Await(settings.LoadMainWindowCloseBehaviorAsync()).Should().Be(MainWindowCloseBehavior.Ask);
        Request("CancelButton", false); // Settings restored prompting.
        panel.IsAssistantHidden.Should().BeFalse();
    }

    private static void WaitUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        done().Should().BeTrue();
    }
    private static void Await(Task task) { WaitUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Await<T>(Task<T> task) { Await((Task)task); return task.GetAwaiter().GetResult(); }
}
