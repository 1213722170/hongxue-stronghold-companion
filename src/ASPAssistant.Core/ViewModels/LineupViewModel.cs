// Modified for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

/// <summary>
/// 已保存阵容列表 + 增删改 + 激活盟约统计访问。
/// </summary>
public partial class LineupViewModel : ObservableObject
{
    private readonly LineupStore _store;
    private Func<string, bool> _isBanned = _ => false;
    public CovenantCalculator Calculator { get; }

    public ObservableCollection<Lineup> Lineups { get; } = [];

    [ObservableProperty]
    private AvailabilityFilter _selectedAvailabilityFilter;

    public IReadOnlyList<Lineup> VisibleLineups => [.. Lineups.Where(lineup => SelectedAvailabilityFilter == AvailabilityFilter.All
        || lineup.Slots.All(slot => !_isBanned(slot.OperatorName)) == (SelectedAvailabilityFilter == AvailabilityFilter.Available))];

    public LineupViewModel(LineupStore store, CovenantCalculator calculator)
    {
        _store = store;
        Calculator = calculator;
        Lineups.CollectionChanged += (_, _) => { RefreshAvailability(); Refresh(); };
    }

    public void LoadLineups(IEnumerable<Lineup> lineups)
    {
        Lineups.Clear();
        foreach (var lineup in lineups) Lineups.Add(lineup);
    }

    public void SetBanChecker(Func<string, bool> checker)
    {
        _isBanned = checker;
        RefreshAvailability();
    }

    public void RefreshAvailability() => OnPropertyChanged(nameof(VisibleLineups));

    partial void OnSelectedAvailabilityFilterChanged(AvailabilityFilter value) => RefreshAvailability();

    public bool CanMove(Lineup lineup, int direction)
    {
        var index = VisibleLineups.ToList().IndexOf(lineup);
        return index >= 0 && (direction == 0 ? Lineups.IndexOf(lineup) > 0
            : direction < 0 ? index > 0 : index < VisibleLineups.Count - 1);
    }

    public async Task MoveAsync(Lineup lineup, int direction)
    {
        if (!CanMove(lineup, direction)) return;
        var original = Lineups.ToList();
        var index = Lineups.IndexOf(lineup);
        if (direction == 0) Lineups.Move(index, 0);
        else
        {
            var visible = VisibleLineups.ToList();
            var other = visible[visible.IndexOf(lineup) + direction];
            var otherIndex = Lineups.IndexOf(other);
            Lineups[index] = other;
            Lineups[otherIndex] = lineup;
        }
        try { await _store.SaveOrderAsync(Lineups.Select(l => l.Id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadLineups(original);
            throw;
        }
    }

    // ── 搜索与目标盟约筛选 ──────────────────────────────────────────────

    /// <summary>Lineups left by the search and the target-covenant filter, newest first.</summary>
    public ObservableCollection<Lineup> FilteredLineups { get; } = [];

    /// <summary>Target covenants picked in the 筛选 panel.</summary>
    public ObservableCollection<string> FilterCovenants { get; } = [];

    [ObservableProperty] private string _searchText = "";
    /// <summary>0 = any tier, 1 / 2 = only targets at that tier.</summary>
    [ObservableProperty] private int _filterTier;
    /// <summary>True: every picked covenant must be a target; false: any one.</summary>
    [ObservableProperty] private bool _filterMatchAll = true;

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnFilterTierChanged(int value) => Refresh();
    partial void OnFilterMatchAllChanged(bool value) => Refresh();

    public int ActiveFilterCount => FilterCovenants.Count + (FilterTier > 0 ? 1 : 0);

    public void ToggleFilterCovenant(string name)
    {
        if (!FilterCovenants.Remove(name)) FilterCovenants.Add(name);
        Refresh();
    }

    public void ClearFilters()
    {
        FilterCovenants.Clear();
        FilterTier = 0;
        FilterMatchAll = true;
        Refresh();
    }

    /// <summary>How many saved lineups have <paramref name="covenant"/> as a target (0 = the chip is disabled).</summary>
    public int TargetCount(string covenant) => Lineups.Count(l => LineupTargets.Of(l, Calculator).Any(t => t.Name == covenant));

    public IReadOnlyList<TargetCovenant> TargetsOf(Lineup lineup) => LineupTargets.Of(lineup, Calculator);

    private bool MatchesSearch(Lineup lineup)
    {
        var q = SearchText.Trim();
        if (q.Length == 0) return true;
        return lineup.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || lineup.Slots.Concat(lineup.EarlySlots).Concat(lineup.MidSlots).Concat(lineup.SupportSlots)
                .Any(s => s.OperatorName.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    // ── 本局推荐 ──────────────────────────────────────────────────────────

    private Func<string, bool>? _unavailable;
    private IReadOnlySet<string> _bannedCovenants = new HashSet<string>();

    /// <summary>Saved lineups ranked against this run's bans; empty until the opening information is recorded.</summary>
    public ObservableCollection<LineupRecommendation> Recommendations { get; } = [];

    [ObservableProperty] private bool _hasRunInfo;

    /// <summary>
    /// This run's bans: <paramref name="unavailable"/> tells whether an operator is out (banned, or every covenant of
    /// theirs is banned). Null clears the run (no recommendations).
    /// </summary>
    public void SetRunInfo(Func<string, bool>? unavailable, IReadOnlySet<string>? bannedCovenants)
    {
        _unavailable = unavailable;
        _bannedCovenants = bannedCovenants ?? new HashSet<string>();
        HasRunInfo = unavailable is not null;
        Refresh();
    }

    public LineupRecommendation? RecommendationFor(Lineup lineup) => Recommendations.FirstOrDefault(r => r.Lineup.Id == lineup.Id);

    private void Refresh()
    {
        FilteredLineups.Clear();
        foreach (var l in Lineups.Where(MatchesSearch)
                     .Where(l => LineupTargets.Matches(l, Calculator, FilterCovenants, FilterTier, FilterMatchAll)))
            FilteredLineups.Add(l);
        Recommendations.Clear();
        if (_unavailable is { } unavailable)
            foreach (var r in LineupTargets.Rank(Lineups, Calculator, unavailable, _bannedCovenants))
                Recommendations.Add(r);
        OnPropertyChanged(nameof(ActiveFilterCount));
    }

    /// <summary>新增或更新阵容，并落盘。</summary>
    public async Task AddOrUpdateAsync(Lineup lineup)
    {
        if (string.IsNullOrEmpty(lineup.Id))
            lineup.Id = Guid.NewGuid().ToString("N");
        lineup.UpdatedAt = DateTime.UtcNow;

        await _store.SaveAsync(lineup);

        var existing = Lineups.FirstOrDefault(l => l.Id == lineup.Id);
        if (existing is not null)
        {
            int idx = Lineups.IndexOf(existing);
            Lineups[idx] = lineup;
        }
        else
        {
            Lineups.Insert(0, lineup);
            await _store.SaveOrderAsync(Lineups.Select(l => l.Id));
        }
    }

    public async Task DeleteAsync(Lineup lineup)
    {
        await _store.DeleteAsync(lineup.Id);
        Lineups.Remove(lineup);
        await _store.SaveOrderAsync(Lineups.Select(l => l.Id));
    }

    public int CountActivated(Lineup lineup) => Calculator.CountActivated(lineup);

    public IReadOnlyList<CovenantStat> ComputeStats(Lineup lineup) => Calculator.Compute(lineup);
}
