using System.Net;
using System.Net.Http;
using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

public sealed class UpgradeReminderDataTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "upgrade-reminders-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(1, 5, 6)]
    [InlineData(7, 5, 6)]
    [InlineData(3, 0, 6)]
    [InlineData(3, 1, 6)]
    [InlineData(3, 5, 15)]
    [InlineData(3, 5, 100)]
    [InlineData(3, 7, 6)]
    public async Task InvalidImportedRangeIsRejected(int level, int start, int end)
    {
        var service = new LineupShareService(EquipmentTestData.Rules, _directory);
        string json = JsonSerializer.Serialize(new Lineup { UpgradeReminders = [new(level, start, end)] });
        await service.Invoking(s => s.ImportAsync(json)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task DuplicateLevelsNullPlansAndNullNotesAreRejectedAtImportBoundary()
    {
        var service = new LineupShareService(EquipmentTestData.Rules, _directory);
        foreach (var json in new[]
        {
            "{\"upgradeReminders\":null}",
            "{\"upgradeReminders\":[null]}",
            "{\"upgradeReminders\":[{\"targetLevel\":3,\"startRound\":5,\"endRound\":6,\"note\":null}]}",
            JsonSerializer.Serialize(new Lineup { UpgradeReminders = [new(3, 5, 6), new(3, 7, 8)] }),
            JsonSerializer.Serialize(new Lineup { UpgradeReminders = [new(3, 5, 6, new string('字', 81))] }),
        })
            await service.Invoking(s => s.ImportAsync(json)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task OldLineupsStayEmptyAndSavingLoadingAndSharingPreservePlans()
    {
        var service = new LineupShareService(EquipmentTestData.Rules, Path.Combine(_directory, "share"));
        var old = await service.ImportAsync("{\"name\":\"旧阵容\",\"slots\":[]}");
        old.UpgradeReminders.Should().BeEmpty();
        old.UpgradeNotificationsEnabled.Should().BeFalse();
        var lineup = new Lineup { Name = "有升本计划", UpgradeNotificationsEnabled = true,
            UpgradeReminders = [new(3, 5, 6, "经济允许时升本"), new(4, 8, 8)] };
        var store = new LineupStore(Path.Combine(_directory, "lineups"), EquipmentTestData.Rules);
        await store.SaveAsync(lineup);
        var loaded = (await store.LoadAllAsync()).Single();
        loaded.UpgradeReminders.Should().Equal(lineup.UpgradeReminders);
        var imported = await service.ImportAsync(JsonSerializer.Serialize(loaded));
        imported.UpgradeReminders.Should().Equal(lineup.UpgradeReminders);
        imported.UpgradeNotificationsEnabled.Should().BeTrue();
        imported.UpgradeReminders[1].RoundText.Should().Be("第8回合");
    }

    [Fact]
    public void EditorCancelResetPhaseSwitchAndExportKeepPlanChangesIsolated()
    {
        var source = new Lineup { UpgradeReminders = [new(3, 5, 6)] };
        var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
        var vm = new LineupEditorViewModel(source, [], [], calculator);
        vm.UpgradeReminders[0] = new(3, 4, 6, "提前准备");
        vm.UpgradeReminders.Add(new(4, 8, 9));
        vm.UpgradeNotificationsEnabled = true;
        vm.SelectPhase(LineupPhase.前期);
        vm.UpgradeReminders.Should().HaveCount(2);
        source.UpgradeReminders.Should().Equal(new TerminalUpgradeReminder(3, 5, 6));
        source.UpgradeNotificationsEnabled.Should().BeFalse();
        var saved = vm.ToLineup();
        saved.UpgradeReminders.Should().Equal(vm.UpgradeReminders);
        saved.UpgradeNotificationsEnabled.Should().BeTrue();
        saved.UpgradeReminders.Clear();
        vm.UpgradeReminders.Should().HaveCount(2);
        vm.Reset();
        vm.UpgradeReminders.Should().Equal(source.UpgradeReminders);
        vm.UpgradeNotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ShareCacheChangesWhenOnlyUpgradePlanChanges()
    {
        using var handler = new ShareHandler();
        using var client = new HttpClient(handler);
        var service = new LineupShareService(EquipmentTestData.Rules, _directory, client);
        var lineup = new Lineup();
        var oldLink = await service.ExportAsync(lineup);
        lineup.UpgradeReminders.Add(new(3, 5, 6));
        var plannedLink = await service.ExportAsync(lineup);
        plannedLink.Should().NotBe(oldLink);
        (await service.ExportAsync(lineup)).Should().Be(plannedLink);
        lineup.UpgradeReminders[0] = new(3, 5, 7, "经济允许时");
        (await service.ExportAsync(lineup)).Should().NotBe(plannedLink);
        lineup.UpgradeReminders.Clear();
        (await service.ExportAsync(lineup)).Should().Be(oldLink);
        handler.Calls.Should().Be(3);
        handler.LastContent.Should().Contain("upgradeReminders", "startRound", "endRound");
    }

    [Fact]
    public async Task TrackingPersistencePreservesPlanSourceAndRunFlags()
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 2 };
        vm.TrackLineup(new Lineup { Id = "planned", UpgradeNotificationsEnabled = true, UpgradeReminders = [new(3, 5, 6)] });
        vm.Economy = new(5, 0, 6, false, "终极模拟", true);
        vm.IgnoreUpgradeReminder();
        var settings = new SettingsManager(_directory);
        await settings.SaveTrackingStateAsync(vm.CaptureState());
        var loaded = await settings.LoadTrackingStateAsync(EquipmentTestData.Rules);
        loaded.UpgradeReminderLineupId.Should().Be("planned");
        loaded.UpgradeReminderProgress.Should().ContainSingle().Which.Ignored.Should().BeTrue();
        loaded.Lineups.Single().UpgradeReminders.Should().Equal(new TerminalUpgradeReminder(3, 5, 6));
        loaded.Lineups.Single().UpgradeNotificationsEnabled.Should().BeTrue();
        loaded.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(4, 6, 5, 7, true)]
    [InlineData(4, 6, 4, 6, true)]
    [InlineData(2, 2, 14, 14, true)]
    [InlineData(4, 6, 3, 7, false)]
    [InlineData(4, 6, 5, 5, false)]
    public void HigherLevelsCannotStartOrEndEarlierButCanOverlap(int firstStart, int firstEnd, int nextStart, int nextEnd, bool valid)
    {
        // Stored array order is not level order.
        var plan = new List<TerminalUpgradeReminder> { new(4, nextStart, nextEnd), new(3, firstStart, firstEnd) };
        if (valid) TerminalUpgradeReminder.ValidationError(plan).Should().BeEmpty();
        else TerminalUpgradeReminder.ValidationError(plan).Should().Contain("对应回合");
    }

    [Fact]
    public async Task NotificationSwitchChangesTheShareLinkAndRoundTrips()
    {
        using var handler = new ShareHandler();
        using var client = new HttpClient(handler);
        var service = new LineupShareService(EquipmentTestData.Rules, _directory, client);
        var lineup = new Lineup { UpgradeReminders = [new(3, 5, 6)] };
        var disabled = await service.ExportAsync(lineup);
        lineup.UpgradeNotificationsEnabled = true;
        (await service.ExportAsync(lineup)).Should().NotBe(disabled);
        handler.LastContent.Should().Contain("upgradeNotificationsEnabled");
        lineup.UpgradeNotificationsEnabled = false;
        (await service.ExportAsync(lineup)).Should().Be(disabled);
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task OldInvalidRoundsRemainVisibleAndUnchangedInLocalFilesAndTracking()
    {
        var lineup = new Lineup { Id = "legacy", UpgradeNotificationsEnabled = true,
            Slots = [new() { OperatorName = "角峰" }], UpgradeReminders = [new(3, 1, 15)] };
        var store = new LineupStore(Path.Combine(_directory, "lineups"), EquipmentTestData.Rules);
        await store.SaveAsync(lineup);
        string path = Path.Combine(_directory, "lineups", "legacy.json");
        string before = await File.ReadAllTextAsync(path);
        var loaded = (await store.LoadAllAsync()).Single();
        loaded.UpgradeReminders.Should().Equal(lineup.UpgradeReminders);
        (await File.ReadAllTextAsync(path)).Should().Be(before);
        var vm = new LineupEditorViewModel(loaded, [], [], new CovenantCalculator([], [], new Dictionary<string, string>()));
        vm.ValidationError.Should().Contain("2–14");
        vm.UpgradeReminders.Should().Equal(lineup.UpgradeReminders);
        var tracking = new TrackingViewModel();
        tracking.TrackLineup(loaded);
        var settings = new SettingsManager(Path.Combine(_directory, "settings"));
        await settings.SaveTrackingStateAsync(tracking.CaptureState());
        var state = await settings.LoadTrackingStateAsync(EquipmentTestData.Rules);
        state.Lineups.Single().UpgradeReminders.Should().Equal(lineup.UpgradeReminders);
        state.Entries.Should().ContainSingle().Which.Name.Should().Be("角峰");
    }

    private sealed class ShareHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string LastContent { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastContent = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new(HttpStatusCode.OK) { Content = new StringContent($"https://dpaste.com/PLAN{Calls}") };
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
