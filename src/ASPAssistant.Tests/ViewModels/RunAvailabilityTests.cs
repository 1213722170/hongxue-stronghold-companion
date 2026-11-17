using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class RunAvailabilityTests
{
    [Fact]
    public void PlannerOnlyOffersTransformationsWithRealEquipment()
    {
        CovenantInfo[] covenants = [new() { Name = "精准", ActivateCount = 2 }, new() { Name = "助力", ActivateCount = 2 }];
        var planner = new LineupPlannerViewModel(new LineupPlanner([], covenants), [], new BanViewModel(), EquipmentTestData.Rules);
        planner.AllCovenants.Single(c => c.Name == "助力").CanTransformInto.Should().BeFalse();
        planner.AllCovenants.Single(c => c.Name == "精准").CanTransformInto.Should().BeTrue();
        planner.BuildRequest().TransformAllowed.Should().OnlyContain(c => EquipmentTestData.Rules.CanTransformInto(c));
    }

    [Fact]
    public void RunInfoChangesPreserveAllCovenantsAndSelectedFilters()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators([
            new() { Name = "甲", CoreCovenants = ["炎"], AdditionalCovenants = ["精准"] },
            new() { Name = "乙", CoreCovenants = ["维多利亚"], AdditionalCovenants = ["坚守"] }
        ]);
        vm.HasRunInfo.Should().BeFalse();
        vm.SetCoreCovenantFilters(["炎"]);
        vm.SetOtherCovenantFilters(["精准"]);
        vm.SetAvailableCovenants(["维多利亚", "坚守"]);
        vm.HasRunInfo.Should().BeTrue();
        vm.AvailableCoreCovenants.Should().BeEquivalentTo("", "炎", "维多利亚");
        vm.AvailableOtherCovenants.Should().BeEquivalentTo("", "精准", "坚守");
        vm.ActiveFilterTags.Should().Contain("炎").And.Contain("精准");
        vm.FilteredOperators.Should().ContainSingle(o => o.Name == "甲");
        vm.SetAvailableCovenants(["炎", "精准"]);
        vm.ClearCovenantHighlights();
        vm.HasRunInfo.Should().BeFalse();
        vm.AvailableCoreCovenants.Should().Contain("炎");
        vm.AvailableOtherCovenants.Should().Contain("精准");
        vm.ActiveChips.Select(c => c.Value).Should().BeEquivalentTo("炎", "精准");
        vm.FilteredOperators.Should().ContainSingle(o => o.Name == "甲");
    }

    [Fact]
    public void EveryBanCombinationKeepsAllCovenantsSelectableAndFiltersSurvivingHolders()
    {
        string[] names = ["炎", "谢拉格", "精准", "奥术"];
        List<Operator> operators = [
            new() { Name = "单核心", CoreCovenants = ["炎"], AdditionalCovenants = ["精准"] },
            new() { Name = "双核心", CoreCovenants = ["炎", "谢拉格"], AdditionalCovenants = ["奥术"] },
            new() { Name = "单附加", AdditionalCovenants = ["精准"] },
            new() { Name = "双附加", AdditionalCovenants = ["精准", "奥术"] },
            new() { Name = "手动禁用", CoreCovenants = ["谢拉格"], AdditionalCovenants = ["精准"] }
        ];
        var banned = new HashSet<string>();
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(operators);
        vm.SetBanChecker(name => name == "手动禁用" || CovenantBanRules.IsExcluded(operators.Single(o => o.Name == name), banned));
        vm.SetCovenantBanChecker(banned.Contains);
        var chips = vm.CoreChips.Concat(vm.OtherChips).ToArray();

        for (var mask = 0; mask < 16; mask++)
        {
            banned.Clear();
            for (var bit = 0; bit < names.Length; bit++)
                if ((mask & (1 << bit)) != 0) banned.Add(names[bit]);
            vm.SetAvailableCovenants(names.Except(banned));
            vm.NotifyBansChanged();
            vm.CoreChips.Concat(vm.OtherChips).Should().Equal(chips);
            foreach (var chip in chips)
            {
                vm.ResetFilters();
                chip.IsBlocked.Should().BeFalse();
                chip.IsSelected = true;
                bool HasCovenant(Operator op) => chip.Kind == FilterChipKind.CoreCovenant
                    ? op.CoreCovenants.Contains(chip.Value) : op.AdditionalCovenants.Contains(chip.Value);
                var holders = operators.Where(HasCovenant).ToArray();
                // Expected availability is explicit so this also guards the existing Ban rules.
                bool Usable(Operator op) => op.Name switch
                {
                    "单核心" => !banned.Contains("炎"),
                    "双核心" => !banned.Contains("炎") || !banned.Contains("谢拉格"),
                    "单附加" => !banned.Contains("精准"),
                    "双附加" => !banned.Contains("精准") || !banned.Contains("奥术"),
                    _ => false
                };
                vm.FilteredOperators.Should().BeEquivalentTo(holders);
                vm.SelectedAvailabilityFilter = AvailabilityFilter.Available;
                vm.FilteredOperators.Should().BeEquivalentTo(holders.Where(Usable));
                chip.Count.Should().Be(holders.Count(Usable));
                chip.IsReduced.Should().Be(banned.Contains(chip.Value));
                vm.SelectedAvailabilityFilter = AvailabilityFilter.Unavailable;
                vm.FilteredOperators.Should().BeEquivalentTo(holders.Where(o => !Usable(o)));
                vm.NotifyBansChanged();
                vm.ClearCovenantHighlights();
                chip.IsSelected.Should().BeTrue();
            }
        }
    }

    [Fact]
    public void ClearingRunLimitsPreservesManualGoalsLocksAndOperatorBans()
    {
        Operator[] operators = [new() { Name = "固定干员", CoreCovenants = ["维多利亚"] }, new() { Name = "手动禁用", CoreCovenants = ["炎"] }];
        CovenantInfo[] covenants = [new() { Name = "炎", BondType = "SEASON", ActivateCount = 3 }, new() { Name = "维多利亚", BondType = "SEASON", ActivateCount = 3 }];
        var bans = new BanViewModel();
        var planner = new LineupPlannerViewModel(new LineupPlanner(operators, covenants), operators, bans, EquipmentTestData.Rules);
        planner.AllCovenants.Single(c => c.Name == "维多利亚").ToggleLevel1Command.Execute(null);
        planner.AddLocked("固定干员");
        bans.ToggleBan("手动禁用");
        bans.SetCovenantBans(["炎"]);
        planner.SetAvailableCovenants(["维多利亚"]);
        planner.OnlyCurrentRun = true;
        bans.SetCovenantBans([]);
        planner.SetAvailableCovenants([]);
        planner.HasRunInfo.Should().BeFalse();
        planner.OnlyCurrentRun.Should().BeFalse();
        planner.AllCovenants.Should().OnlyContain(c => !c.IsBanned && c.IsVisible);
        planner.SelectedGoalCount.Should().Be(1);
        planner.LockedOperators.Should().ContainSingle().Which.Name.Should().Be("固定干员");
        bans.IsBanned("手动禁用").Should().BeTrue();
    }
}
