using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class CoreBanViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        var folder = Path.Combine(Path.GetTempPath(), "core-ban-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Operator[] operators = [
            new() { Name = "单主盟约", Tier = 1, CoreCovenants = ["炎"], AdditionalCovenants = ["精准"] },
            new() { Name = "双主盟约", Tier = 2, CoreCovenants = ["炎", "谢拉格"], AdditionalCovenants = ["突袭"] },
            new() { Name = "无主盟约", Tier = 3, AdditionalCovenants = ["精准", "奥术"] },
            new() { Name = "手动禁用", Tier = 4, CoreCovenants = ["谢拉格"], AdditionalCovenants = ["精准"] },
            new() { Name = "自选单主", Tier = 5, IsCustom = true, IsSelectedCustom = true, CoreCovenants = ["炎"] },
            new() { Name = "未选自选", Tier = 5, IsCustom = true, CoreCovenants = ["炎"] }];
        CovenantInfo[] covenants = [
            new() { Name = "炎", BondType = "SEASON", ActivateCount = 3 },
            new() { Name = "谢拉格", BondType = "SEASON", ActivateCount = 3 },
            new() { Name = "精准", BondType = "REGULAR", ActivateCount = 2 },
            new() { Name = "突袭", BondType = "REGULAR", ActivateCount = 2 },
            new() { Name = "奥术", BondType = "REGULAR", ActivateCount = 2 }];
        var vm = new OperatorBrowseViewModel { SelectedOperatorSource = OperatorSourceFilter.Default };
        vm.LoadOperators(operators.ToList());
        var equipment = new EquipmentBrowseViewModel();
        equipment.LoadEquipment([], [], [], new Dictionary<string, string>());
        var bans = new BanViewModel();
        vm.SetCovenantBanChecker(bans.IsCovenantBanned);
        var calculator = new CovenantCalculator(operators, covenants, new Dictionary<string, string>());
        var lineups = new LineupViewModel(new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules), calculator);
        lineups.LoadLineups([new() { Id = "a", Name = "含单主", Slots = [new() { OperatorName = "单主盟约" }] },
            new() { Id = "b", Name = "含双主", Slots = [new() { OperatorName = "双主盟约" }] }]);
        var panel = new SidePanelWindow(vm, equipment, new TrackingViewModel(), new GameStateViewModel(), bans,
            new EnemyViewModel(), lineups, new LineupShareService(calculator.EquipmentRules, Path.Combine(folder, "share")),
            operators, [], [], new SettingsManager(folder), theme, new SidePanelPositionSettings { FreeMovement = true }, covenants: covenants)
        { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Width = 360, Height = 900 };
        panel.BanToggleRequested += (name, _) => bans.ToggleBan(name);
        try
        {
            panel.Show(); ThemeManager.Apply(theme, night); Pump();
            var view = (OperatorBrowseView)panel.FindName("OperatorView");
            foreach (var width in new[] { 280, 360, 400 })
            {
                panel.Width = width; Pump();
                view.FindName("OnlyCurrentRunToggle").Should().BeNull();
                var filter = (ToggleButton)view.FindName("FilterToggle");
                var availability = (ListBox)view.FindName("AvailabilityFilterList");
                var source = (ListBox)view.FindName("OperatorSourceList");
                var filterBounds = filter.TransformToAncestor(view).TransformBounds(new Rect(0, 0, filter.ActualWidth, filter.ActualHeight));
                var availabilityBounds = availability.TransformToAncestor(view).TransformBounds(new Rect(0, 0, availability.ActualWidth, availability.ActualHeight));
                var sourceBounds = source.TransformToAncestor(view).TransformBounds(new Rect(0, 0, source.ActualWidth, source.ActualHeight));
                sourceBounds.Right.Should().BeLessThanOrEqualTo(view.ActualWidth);
                availabilityBounds.Right.Should().BeLessThanOrEqualTo(filterBounds.Left);
                foreach (var label in Desc<TextBlock>(availability))
                {
                    var textBounds = label.TransformToAncestor(view).TransformBounds(new Rect(0, 0, label.ActualWidth, label.ActualHeight));
                    textBounds.Right.Should().BeLessThanOrEqualTo(availabilityBounds.Right + 0.5, "availability labels must not be clipped");
                    var measured = new TextBlock { Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize, FontWeight = label.FontWeight };
                    measured.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    label.ActualWidth.Should().BeGreaterThanOrEqualTo(measured.DesiredSize.Width - 0.5);
                }
                Save(view, theme, night, $"layout-{width}");
            }
            panel.Width = 360; Pump();
            OperatorCard Card(string name) => Desc<OperatorCard>(view).Single(c => ((Operator)c.DataContext).Name == name);
            void ClickBan(string name) => ((Button)Card(name).FindName("BanButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ClickBan("手动禁用");
            bans.SetCovenantBans(["炎"]); Pump();
            Card("单主盟约").IsBanned.Should().BeTrue();
            Card("双主盟约").IsBanned.Should().BeFalse();
            Card("手动禁用").IsBanned.Should().BeTrue();
            var blocked = Card("单主盟约");
            ((Button)blocked.FindName("BanButton")).IsEnabled.Should().BeFalse();
            ((TextBlock)blocked.FindName("NameText")).TextDecorations.Should().NotBeNull();
            ((Border)blocked.FindName("CardBorder")).Opacity.Should().Be(0.5);
            ClickBan("单主盟约");
            Card("单主盟约").IsBanned.Should().BeTrue();
            bans.BannedNames.Should().Equal("手动禁用");
            ((ItemsControl)((RunBanView)panel.FindName("RunBanView")).FindName("BannedOperators")).Items.Cast<Operator>()
                .Select(op => op.Name).Should().BeEquivalentTo("单主盟约", "自选单主", "未选自选", "手动禁用");
            vm.CoreChips.Single(c => c.Value == "炎").Count.Should().Be(1);
            vm.SetAvailableCovenants(["谢拉格", "突袭", "奥术"]);
            panel.EnableCurrentRunFilters();
            ((ToggleButton)view.FindName("FilterToggle")).IsChecked = true; Pump();
            var fireChip = vm.CoreChips.Single(c => c.Value == "炎");
            var fireToggle = Desc<ToggleButton>(view).Single(t => ReferenceEquals(t.DataContext, fireChip));
            fireToggle.IsEnabled.Should().BeTrue();
            fireChip.CountText.Should().Be("剩 1");
            fireToggle.IsChecked = true;
            ((ListBox)view.FindName("AvailabilityFilterList")).SelectedIndex = 1; Pump();
            vm.FilteredOperators.Should().ContainSingle(o => o.Name == "双主盟约");
            Save(view, theme, night, "banned-core-filter");
            bans.SetCovenantBans(["炎", "谢拉格"]); Pump();
            fireToggle.IsEnabled.Should().BeTrue();
            fireToggle.IsChecked.Should().BeTrue();
            fireChip.CountText.Should().Be("剩 0");
            vm.FilteredOperators.Should().BeEmpty();
            bans.SetCovenantBans(["炎"]); Pump();
            fireToggle.IsChecked.Should().BeTrue();
            vm.FilteredOperators.Should().ContainSingle(o => o.Name == "双主盟约");
            fireToggle.IsChecked = false;
            ((ListBox)view.FindName("AvailabilityFilterList")).SelectedIndex = 0; Pump();
            lineups.SelectedAvailabilityFilter = LineupAvailabilityFilter.Available;
            lineups.VisibleLineups.Select(l => l.Name).Should().Equal("含双主");
            Save(view, theme, night, "core-ban");

            ((ListBox)view.FindName("AvailabilityFilterList")).SelectedIndex = 2; Pump();
            vm.FilteredOperators.Select(o => o.Name).Should().BeEquivalentTo("单主盟约", "手动禁用");
            ((ListBox)view.FindName("AvailabilityFilterList")).SelectedIndex = 0;
            bans.SetCovenantBans(["炎", "谢拉格"]); Pump();
            Card("双主盟约").IsBanned.Should().BeTrue();
            ((ListBox)view.FindName("OperatorSourceList")).SelectedValue = OperatorSourceFilter.Custom; Pump();
            vm.FilteredOperators.Select(o => o.Name).Should().Equal("自选单主");
            Card("自选单主").IsBanned.Should().BeTrue();
            ((Button)Card("自选单主").FindName("BanButton")).IsEnabled.Should().BeFalse();
            Save(view, theme, night, "custom-ban");

            ((ListBox)view.FindName("OperatorSourceList")).SelectedValue = OperatorSourceFilter.Default;
            bans.SetCovenantBans(["精准"]); Pump();
            Card("单主盟约").IsBanned.Should().BeFalse();
            Card("无主盟约").IsBanned.Should().BeFalse();
            bans.SetCovenantBans(["精准", "奥术"]); Pump();
            Card("无主盟约").IsBanned.Should().BeTrue();
            Card("单主盟约").IsBanned.Should().BeFalse();
            bans.ClearRunBans(); Pump();
            Card("单主盟约").IsBanned.Should().BeFalse();
            ((Button)Card("单主盟约").FindName("BanButton")).IsEnabled.Should().BeTrue();
            Card("手动禁用").IsBanned.Should().BeTrue();
            ClickBan("手动禁用");
            bans.BannedNames.Should().BeEmpty();
            bans.SetBans(["单主盟约"]); bans.SetCovenantBans(["炎"]); Pump();
            Card("单主盟约").IsBanned.Should().BeTrue();
            bans.ClearRunBans(); Pump();
            vm.SelectedAvailabilityFilter = AvailabilityFilter.Available;
            vm.FilteredOperators.Should().HaveCount(4);
            Save(view, theme, night, "cleared");
        }
        finally { panel.Close(); Directory.Delete(folder, true); }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<T> Desc<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var nested in Desc<T>(child)) yield return nested;
        }
    }

    private static void Save(FrameworkElement view, GameShopTheme theme, bool night, string suffix)
    {
        var directory = Path.Combine(TestOutput.Directory, "core-ban"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"{theme}-{night}-{suffix}.png")); png.Save(file);
    }
}
