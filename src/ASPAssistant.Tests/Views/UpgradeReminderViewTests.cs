using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class UpgradeReminderViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        ThemeManager.Apply(theme, night);
        var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
        var vm = new TrackingViewModel { TrackMultipleLineups = true, DetectedTerminalLevel = 2 };
        var view = new TrackingView();
        view.SetCovenantCalculator(calculator);
        view.DataContext = vm;
        var host = new Window { Content = view, Width = 374, Height = 650, ShowActivated = false,
            ShowInTaskbar = false, Opacity = 0, Left = -20000, Top = -20000 };
        host.Show();
        try
        {
            Pump();
            ((FrameworkElement)view.FindName("UpgradeReminderArea")).Visibility.Should().Be(Visibility.Collapsed);
            var lineup = new Lineup { Id = "a", Name = "本局升本计划", UpgradeNotificationsEnabled = true, Slots = [new() { OperatorName = "角峰" }],
                UpgradeReminders = [new(3, 5, 6, "经济允许时升本，可按局势调整")] };
            vm.TrackLineup(lineup);
            vm.Economy = new(5, 0, 6, false, "终极模拟", true);
            foreach (int width in new[] { 280, 374, 400 })
            {
                host.Width = width;
                Pump();
                var card = (FrameworkElement)view.FindName("UpgradeReminderCard");
                card.ActualWidth.Should().BeGreaterThan(200).And.BeLessThan(width);
                var title = (TextBlock)view.FindName("UpgradeReminderTitle");
                title.Text.Should().Contain("升至3级");
                title.DesiredSize.Width.Should().BeLessThanOrEqualTo(title.ActualWidth + 0.1);
                var row = (FrameworkElement)view.FindName("EconomyRow");
                card.TranslatePoint(new Point(), view).Y.Should().BeGreaterThan(row.TranslatePoint(new Point(), view).Y + row.ActualHeight);
            }
            Save((FrameworkElement)view.FindName("TrackingStatusCard"), $"card-{theme}-{night}-due");
            CheckReminderContrast(view, $"contrast-{theme}-{night}-due");
            vm.Economy = vm.Economy with { Round = 6 };
            ((TextBlock)view.FindName("UpgradeReminderRange")).Text.Should().Be("区间末回合");
            Save((FrameworkElement)view.FindName("TrackingStatusCard"), $"card-{theme}-{night}-last");
            CheckReminderContrast(view, $"contrast-{theme}-{night}-last");
            Click((Button)view.FindName("IgnoreUpgradeReminderButton"));
            vm.UpgradeReminder!.Status.Should().Be(UpgradeReminderStatus.Ignored);
            Click((Button)view.FindName("IgnoreUpgradeReminderButton"));
            vm.UpgradeReminder.Status.Should().Be(UpgradeReminderStatus.LastRound);
            vm.TrackLineup(new Lineup { Id = "b", Name = "另一套升本计划", UpgradeNotificationsEnabled = true, UpgradeReminders = [new(4, 8, 9)] });
            Pump();
            ((FrameworkElement)view.FindName("UpgradeReminderSourceRow")).Visibility.Should().Be(Visibility.Visible);
            ((ComboBox)view.FindName("UpgradeReminderSourceBox")).SelectedValue = "b";
            vm.UpgradeReminderLineupId.Should().Be("b");
            vm.ManualTerminalLevel = 4;
            vm.UpgradeReminder.Status.Should().Be(UpgradeReminderStatus.Completed);
            vm.ClearAllTracking();
            ((FrameworkElement)view.FindName("UpgradeReminderArea")).Visibility.Should().Be(Visibility.Collapsed);

            var editorVm = new LineupEditorViewModel(lineup, [], [], calculator);
            var editor = new LineupEditorWindow(editorVm, []) { ShowActivated = false, ShowInTaskbar = false,
                Opacity = 0, Left = -20000, Top = -20000, Width = 760 };
            editor.Show();
            try
            {
                Pump();
                var toggle = (CheckBox)editor.FindName("UpgradeNotificationsToggle");
                toggle.IsChecked.Should().BeTrue();
                toggle.SetCurrentValue(CheckBox.IsCheckedProperty, false);
                Pump();
                editorVm.UpgradeNotificationsEnabled.Should().BeFalse();
                var chips = (Panel)editor.FindName("UpgradeReminderChips");
                Click(chips.Children.OfType<Button>().Last());
                ((ComboBox)editor.FindName("UpgradeLevelBox")).SelectedItem = 4;
                var start = (ComboBox)editor.FindName("UpgradeStartBox");
                var end = (ComboBox)editor.FindName("UpgradeEndBox");
                start.IsEditable.Should().BeFalse();
                end.IsEditable.Should().BeFalse();
                start.Items.Cast<int>().Should().Equal(Enumerable.Range(2, 13));
                end.Items.Cast<int>().Should().Equal(Enumerable.Range(2, 13));
                start.SelectedItem = 9;
                end.SelectedItem = 8;
                Click((Button)editor.FindName("SaveButton"));
                ((TextBlock)editor.FindName("UpgradeReminderError")).Text.Should().Contain("开始回合");
                editor.Result.Should().BeNull();
                start.SelectedItem = 4;
                end.SelectedItem = 9;
                Click((Button)editor.FindName("SaveButton"));
                ((TextBlock)editor.FindName("UpgradeReminderError")).Text.Should().Contain("对应回合");
                start.SelectedItem = 7;
                end.SelectedItem = 9;
                ((TextBox)editor.FindName("UpgradeNoteBox")).Text = "经济允许时升本";
                CheckEditorContrast(editor, $"contrast-editor-{theme}-{night}");
                foreach (int width in new[] { 760, 980 })
                {
                    editor.Width = width;
                    Pump();
                    var level = (ComboBox)editor.FindName("UpgradeLevelBox");
                    double center = level.TranslatePoint(new Point(0, level.ActualHeight / 2), editor).Y;
                    foreach (string name in new[] { "UpgradeLevelLabel", "UpgradeRoundLabel", "UpgradeRoundSeparator", "UpgradeRoundSuffix", "UpgradeStartBox", "UpgradeEndBox" })
                    {
                        var element = (FrameworkElement)editor.FindName(name);
                        element.TranslatePoint(new Point(0, element.ActualHeight / 2), editor).Y.Should().BeApproximately(center, 0.6, name);
                    }
                    start.ActualHeight.Should().Be(level.ActualHeight);
                    end.ActualHeight.Should().Be(level.ActualHeight);
                    var label = (TextBlock)editor.FindName("UpgradeReminderLabel");
                    label.TranslatePoint(new Point(0, label.ActualHeight / 2), editor).Y.Should().BeApproximately(
                        toggle.TranslatePoint(new Point(0, toggle.ActualHeight / 2), editor).Y, 0.6);
                    chips.TranslatePoint(new Point(chips.ActualWidth, 0), editor).X.Should().BeLessThanOrEqualTo(
                        toggle.TranslatePoint(new Point(), editor).X);
                    Save((FrameworkElement)editor.Content, $"editor-{theme}-{night}-{width}");
                }
                Click((Button)editor.FindName("SaveButton"));
                editor.Result!.UpgradeReminders.Should().Equal(new TerminalUpgradeReminder(3, 5, 6, "经济允许时升本，可按局势调整"),
                    new TerminalUpgradeReminder(4, 7, 9, "经济允许时升本"));
                lineup.UpgradeReminders.Should().ContainSingle();
                editor.Result.UpgradeNotificationsEnabled.Should().BeFalse();
                lineup.UpgradeNotificationsEnabled.Should().BeTrue();
                vm.TrackLineup(editor.Result);
                ((FrameworkElement)view.FindName("UpgradeReminderArea")).Visibility.Should().Be(Visibility.Collapsed);
            }
            finally { editor.Close(); }

            var legacy = new Lineup { UpgradeNotificationsEnabled = true, UpgradeReminders = [new(3, 1, 15)] };
            var legacyEditor = new LineupEditorWindow(new LineupEditorViewModel(legacy, [], [], calculator), [])
                { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
            legacyEditor.Show();
            try
            {
                Pump();
                ((TextBlock)legacyEditor.FindName("UpgradePlanWarning")).Text.Should().Contain("2–14");
                Click(((Panel)legacyEditor.FindName("UpgradeReminderChips")).Children.OfType<Button>().First());
                ((ComboBox)legacyEditor.FindName("UpgradeStartBox")).SelectedItem.Should().BeNull();
                ((ComboBox)legacyEditor.FindName("UpgradeEndBox")).SelectedItem.Should().BeNull();
                ((TextBlock)legacyEditor.FindName("UpgradeReminderError")).Text.Should().Contain("第1–15回合");
                legacy.UpgradeReminders.Should().Equal(new TerminalUpgradeReminder(3, 1, 15));
                ((ComboBox)legacyEditor.FindName("UpgradeStartBox")).SelectedItem = 2;
                ((ComboBox)legacyEditor.FindName("UpgradeEndBox")).SelectedItem = 14;
                Click((Button)legacyEditor.FindName("SaveButton"));
                legacyEditor.Result!.UpgradeReminders.Should().Equal(new TerminalUpgradeReminder(3, 2, 14));
            }
            finally { legacyEditor.Close(); }

            var fullPlan = new Lineup { UpgradeReminders = [.. Enumerable.Range(2, 5).Select(l => new TerminalUpgradeReminder(l, 2, 3))] };
            var fullEditor = new LineupEditorWindow(new LineupEditorViewModel(fullPlan, [], [], calculator), [])
                { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Width = 760 };
            fullEditor.Show();
            try
            {
                Pump();
                var chips = (Panel)fullEditor.FindName("UpgradeReminderChips");
                chips.Children.OfType<Button>().Last().IsEnabled.Should().BeFalse();
                Click(chips.Children.OfType<Button>().ElementAt(3));
                foreach (int width in new[] { 760, 980 })
                {
                    fullEditor.Width = width;
                    Pump();
                    var toggle = (CheckBox)fullEditor.FindName("UpgradeNotificationsToggle");
                    toggle.IsChecked.Should().BeFalse();
                    foreach (var chip in chips.Children.OfType<Button>())
                        chip.TranslatePoint(new Point(chip.ActualWidth, 0), fullEditor).X.Should().BeLessThanOrEqualTo(toggle.TranslatePoint(new Point(), fullEditor).X);
                    Save((FrameworkElement)fullEditor.Content, $"editor-full-plan-{theme}-{night}-{width}");
                }
            }
            finally { fullEditor.Close(); }
        }
        finally { host.Close(); }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Save(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(TestOutput.Directory, "upgrade-reminders");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }

    private static void CheckReminderContrast(TrackingView view, string name)
    {
        var surface = ((SolidColorBrush)((Border)view.FindName("TrackingStatusCard")).Background).Color;
        var tint = ((SolidColorBrush)((Border)view.FindName("UpgradeReminderCard")).Background).Color;
        double alpha = tint.A / 255.0;
        var background = Color.FromRgb((byte)(tint.R * alpha + surface.R * (1 - alpha)),
            (byte)(tint.G * alpha + surface.G * (1 - alpha)), (byte)(tint.B * alpha + surface.B * (1 - alpha)));
        var ratios = new Dictionary<string, double>();
        foreach (string field in new[] { "UpgradeReminderTitle", "UpgradeReminderRange", "UpgradeReminderDetail", "IgnoreUpgradeReminderButton" })
        {
            var element = (FrameworkElement)view.FindName(field);
            var brush = element is TextBlock text ? text.Foreground : ((Button)element).Foreground;
            double foreground = Luminance(((SolidColorBrush)brush).Color), bg = Luminance(background);
            double ratio = (Math.Max(foreground, bg) + 0.05) / (Math.Min(foreground, bg) + 0.05);
            ratios[field] = ratio;
            ratio.Should().BeGreaterThanOrEqualTo(4.5, $"{name}: {field} must remain readable on the composited card");
        }
        File.WriteAllText(Path.Combine(TestOutput.Directory, "upgrade-reminders", name + ".json"),
            System.Text.Json.JsonSerializer.Serialize(ratios));

    }

    private static void CheckEditorContrast(LineupEditorWindow editor, string name)
    {
        var ratios = new Dictionary<string, double>();
        foreach (var (field, backgroundKey) in new[]
        {
            ("UpgradeReminderLabel", "CardBackgroundBrush"), ("UpgradeNotificationsToggle", "CardBackgroundBrush"),
            ("UpgradeLevelLabel", "SectionBackgroundBrush"), ("UpgradeRoundLabel", "SectionBackgroundBrush"),
            ("UpgradeNoteLabel", "SectionBackgroundBrush"), ("UpgradeLevelBox", "SectionBackgroundBrush"),
            ("UpgradeStartBox", "SectionBackgroundBrush"), ("UpgradeEndBox", "SectionBackgroundBrush"),
            ("UpgradeNoteBox", "InputBackgroundBrush"), ("CancelUpgradeReminderButton", "SectionBackgroundBrush"),
            ("DeleteUpgradeButton", "SectionBackgroundBrush"),
            ("SaveUpgradeReminderButton", "PrimaryActionBrush"),
        })
        {
            var element = (FrameworkElement)editor.FindName(field);
            var foreground = (SolidColorBrush)(element is TextBlock text ? text.Foreground : ((Control)element).Foreground);
            var background = (SolidColorBrush)editor.FindResource(backgroundKey);
            double a = Luminance(foreground.Color), b = Luminance(background.Color);
            double ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
            ratios[field] = ratio;
            ratio.Should().BeGreaterThanOrEqualTo(4.5, $"{name}: {field}");
        }
        File.WriteAllText(Path.Combine(TestOutput.Directory, "upgrade-reminders", name + ".json"),
            System.Text.Json.JsonSerializer.Serialize(ratios));
    }

    private static double Luminance(Color c)
    {
        static double Linear(byte v) => v / 255.0 <= 0.04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }
}
