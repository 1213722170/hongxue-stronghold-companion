using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;
using System.Diagnostics;
using Xunit.Abstractions;

namespace ASPAssistant.Tests.ViewModels;

public class UpgradeReminderTests(ITestOutputHelper output)
{
    private static Lineup Plan(string id = "a", int start = 5, int end = 6) => new()
    {
        Id = id, Name = id, Slots = [new() { OperatorName = "角峰" }], UpgradeNotificationsEnabled = true,
        UpgradeReminders = [new(3, start, end, "经济允许时升本"), new(4, 8, 9)]
    };

    private static ShopEconomy Hud(int round, bool preparation = true, int money = 0)
        => new(round, money, 6, false, "终极模拟", preparation);

    [Fact]
    public void RangeRemindsOnEntryAndLastRoundWithoutRepeatingOnFundsOrBackwardReads()
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 2 };
        var notifications = new List<UpgradeReminderDisplay>();
        vm.UpgradeReminderRequested += notifications.Add;
        vm.TrackLineup(Plan());
        vm.UpgradeReminder!.Status.Should().Be(UpgradeReminderStatus.Waiting);
        vm.Economy = Hud(4);
        vm.UpgradeReminder.Status.Should().Be(UpgradeReminderStatus.Upcoming);
        vm.Economy = Hud(5);
        vm.UpgradeReminder.Status.Should().Be(UpgradeReminderStatus.Due);
        var sameDisplay = vm.UpgradeReminder;
        for (int funds = 1; funds < 30; funds++) vm.Economy = Hud(5, money: funds);
        vm.UpgradeReminder.Should().BeSameAs(sameDisplay);
        notifications.Should().HaveCount(1);
        vm.Economy = Hud(6);
        vm.UpgradeReminder.Status.Should().Be(UpgradeReminderStatus.LastRound);
        vm.Economy = Hud(7);
        vm.UpgradeReminder.Status.Should().Be(UpgradeReminderStatus.Overdue);
        vm.Economy = Hud(5);
        vm.Economy = Hud(6);
        notifications.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(5, 5, 5)]
    [InlineData(5, 6, 6)]
    [InlineData(5, 6, 7)]
    public void SingleRoundAndMissedEntryOnlyEmitOneReminder(int start, int end, int seen)
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 2 };
        int notifications = 0;
        vm.UpgradeReminderRequested += _ => notifications++;
        vm.TrackLineup(Plan(start: start, end: end));
        vm.Economy = Hud(seen);
        vm.Economy = Hud(seen, money: 3);
        notifications.Should().Be(1);
    }

    [Fact]
    public void EarlyUpgradeMovesToNextPlanAndCompletionStopsReminders()
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 3, Economy = Hud(4) };
        vm.TrackLineup(Plan());
        vm.UpgradeReminder!.Reminder!.TargetLevel.Should().Be(4);
        vm.ManualTerminalLevel = 4;
        vm.UpgradeReminder.Status.Should().Be(UpgradeReminderStatus.Completed);
        vm.Economy = Hud(20);
        vm.CaptureState().UpgradeReminderProgress.Should().BeEmpty();
    }

    [Fact]
    public void RestoredLevelCannotCompleteAPlanUntilConfirmed()
    {
        var vm = new TrackingViewModel { IsTerminalLevelRestored = true, DetectedTerminalLevel = 6, Economy = Hud(5) };
        int notifications = 0;
        vm.UpgradeReminderRequested += _ => notifications++;
        vm.TrackLineup(Plan());
        vm.UpgradeReminder!.Status.Should().Be(UpgradeReminderStatus.Waiting);
        vm.DetectedTerminalLevel = 2;
        notifications.Should().Be(0);
        vm.IsTerminalLevelRestored = false;
        vm.UpgradeReminder.Status.Should().Be(UpgradeReminderStatus.Due);
        notifications.Should().Be(1);
    }

    [Fact]
    public void PreparationTransitionUsesExistingHudAndDoesNotAlertDuringBattle()
    {
        var tracker = new ShopEconomyTracker();
        var vm = new TrackingViewModel { DetectedTerminalLevel = 2 };
        vm.TrackLineup(Plan());
        int notifications = 0;
        vm.UpgradeReminderRequested += _ => notifications++;
        tracker.Observe(Hud(5, false));
        tracker.Observe(Hud(5, false));
        vm.Economy = tracker.Current;
        notifications.Should().Be(0);
        tracker.Observe(Hud(5)).Should().BeTrue();
        vm.Economy = tracker.Current;
        notifications.Should().Be(1);
    }

    [Fact]
    public void IgnoredAndNotifiedStateSurviveRestartButResetForANewRun()
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 2 };
        vm.TrackLineup(Plan());
        vm.Economy = Hud(5);
        vm.IgnoreUpgradeReminder();
        vm.UpgradeReminder!.Reminder!.TargetLevel.Should().Be(4);
        var json = JsonSerializer.Serialize(vm.CaptureState());
        var restored = new TrackingViewModel { DetectedTerminalLevel = 2 };
        int notifications = 0;
        restored.UpgradeReminderRequested += _ => notifications++;
        restored.LoadState(JsonSerializer.Deserialize<TrackingState>(json)!);
        restored.Economy = Hud(6);
        notifications.Should().Be(0);
        restored.UpgradeReminder!.Reminder!.TargetLevel.Should().Be(4);
        restored.StartNewRun();
        restored.Economy.Should().BeNull();
        restored.CaptureState().UpgradeReminderProgress.Should().BeEmpty();
        restored.DetectedTerminalLevel = 2;
        restored.Economy = Hud(5);
        notifications.Should().Be(1);
        restored.UpgradeReminder.Reminder!.TargetLevel.Should().Be(3);
    }

    [Fact]
    public void NotificationFlagsSurviveRestartWithoutSuppressingLastRound()
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 2 };
        vm.TrackLineup(Plan());
        vm.Economy = Hud(5);
        var restored = new TrackingViewModel { DetectedTerminalLevel = 2 };
        restored.LoadState(vm.CaptureState());
        int notifications = 0;
        restored.UpgradeReminderRequested += _ => notifications++;
        restored.Economy = Hud(5);
        notifications.Should().Be(0);
        restored.Economy = Hud(6);
        notifications.Should().Be(1);
    }

    [Fact]
    public void MultipleLineupsUseTheSelectedSourceAndTrackIndependentProgress()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true, DetectedTerminalLevel = 2 };
        vm.TrackLineup(Plan());
        vm.TrackLineup(Plan("b", 7, 8));
        vm.UpgradeReminderLineupId.Should().Be("a");
        vm.Economy = Hud(5);
        vm.IgnoreUpgradeReminder();
        vm.UpgradeReminderLineupId = "b";
        vm.UpgradeReminder!.Reminder!.TargetLevel.Should().Be(3);
        vm.UpgradeReminder.Status.Should().Be(UpgradeReminderStatus.Upcoming);
        vm.UpgradeReminderLineupId = "a";
        vm.UpgradeReminder.Reminder!.TargetLevel.Should().Be(4);
        var restored = new TrackingViewModel();
        restored.LoadState(vm.CaptureState());
        restored.UpgradeReminderLineupId.Should().Be("a");
        vm.ClearAllTracking();
        vm.UpgradeReminder.Should().BeNull();
        vm.CaptureState().UpgradeReminderProgress.Should().BeEmpty();
    }

    [Fact]
    public void EditingTheSelectedLineupUpdatesItsPlanWithoutResettingUnchangedRules()
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 2 };
        vm.TrackLineup(Plan());
        vm.Economy = Hud(5);
        vm.IgnoreUpgradeReminder();
        vm.ApplyTrackedLineup(Plan());
        vm.UpgradeReminder!.Reminder!.TargetLevel.Should().Be(4);
        vm.ApplyTrackedLineup(Plan(start: 6, end: 7));
        vm.UpgradeReminder.Reminder!.TargetLevel.Should().Be(3);
        vm.UpgradeReminder.Status.Should().Be(UpgradeReminderStatus.Upcoming);
    }

    [Fact]
    public void AllIgnoredCanBeRestoredAndReminderOnlyLineupsRemainAssociated()
    {
        var vm = new TrackingViewModel();
        var lineup = Plan();
        lineup.Slots.Clear();
        vm.TrackLineup(lineup);
        vm.IsLineupTracked(lineup.Id).Should().BeTrue();
        vm.IgnoreUpgradeReminder();
        vm.IgnoreUpgradeReminder();
        vm.UpgradeReminder!.Status.Should().Be(UpgradeReminderStatus.Ignored);
        vm.RestoreUpgradeReminders();
        vm.UpgradeReminder.Reminder!.TargetLevel.Should().Be(3);
        vm.TrackLineup(new Lineup());
        vm.UpgradeReminder.Should().BeNull();
    }

    [Fact]
    public void FundsUpdatesDoNotRebuildDisplayOrSaveReminderProgress()
    {
        foreach (bool planned in new[] { false, true })
        {
            var vm = new TrackingViewModel { DetectedTerminalLevel = 1, Economy = Hud(5) };
            if (planned) vm.TrackLineup(new Lineup { UpgradeNotificationsEnabled = true,
                UpgradeReminders = [.. Enumerable.Range(2, 5).Select(l => new TerminalUpgradeReminder(l, 5, 6))] });
            var left = Hud(5, money: 1);
            var right = Hud(5, money: 2);
            for (int i = 0; i < 1000; i++) vm.Economy = (i & 1) == 0 ? left : right;
            int displayChanges = 0, writes = 0;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.UpgradeReminder)) displayChanges++; };
            vm.UpgradeReminderStateChanged += () => writes++;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 25000; i++) vm.Economy = (i & 1) == 0 ? left : right;
            stopwatch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            output.WriteLine($"HUD_UPDATES planned={planned} count=25000 elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3} allocatedBytes={allocated} displayChanges={displayChanges} stateWrites={writes}");
            displayChanges.Should().Be(0);
            writes.Should().Be(0);
        }
    }

    [Fact]
    public void NotificationsRequireOptInAndDisablingHidesTheCardWithoutDeletingPlans()
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 2, Economy = Hud(5) };
        var lineup = Plan();
        lineup.UpgradeNotificationsEnabled = false;
        int notifications = 0;
        vm.UpgradeReminderRequested += _ => notifications++;
        vm.TrackLineup(lineup);
        vm.UpgradeReminder.Should().BeNull();
        vm.UpgradeReminderSources.Should().BeEmpty();
        vm.Economy = Hud(6);
        notifications.Should().Be(0);
        vm.CaptureState().Lineups.Single().UpgradeReminders.Should().HaveCount(2);
        lineup.UpgradeNotificationsEnabled = true;
        vm.ApplyTrackedLineup(lineup);
        vm.UpgradeReminder!.Status.Should().Be(UpgradeReminderStatus.LastRound);
        notifications.Should().Be(1);
        lineup.UpgradeNotificationsEnabled = false;
        vm.ApplyTrackedLineup(lineup);
        vm.UpgradeReminder.Should().BeNull();
        vm.Economy = Hud(7);
        notifications.Should().Be(1);
        var restarted = new TrackingViewModel();
        restarted.LoadState(vm.CaptureState());
        restarted.UpgradeReminder.Should().BeNull();
        restarted.CaptureState().Lineups.Single().UpgradeNotificationsEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(5, 15)]
    [InlineData(9, 10)]
    public void OldOutOfRangeOrBackwardPlansDoNotNotifyEvenWhenEnabled(int start, int end)
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 2, Economy = Hud(8) };
        int notifications = 0;
        vm.UpgradeReminderRequested += _ => notifications++;
        vm.TrackLineup(Plan(start: start, end: end));
        vm.UpgradeReminder.Should().BeNull();
        notifications.Should().Be(0);
        vm.CaptureState().Lineups.Single().UpgradeReminders[0].StartRound.Should().Be(start);
    }
}
