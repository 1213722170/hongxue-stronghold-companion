using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.GameState;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;
using Moq;

namespace ASPAssistant.Tests.Services;

public class OcrScannerServiceTests
{
    [Fact]
    public async Task DifficultyIsReadOnTheOpeningEvenWhenTheShopScanIsSuppressed()
    {
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 480, 0, 960, 108))
            .ReturnsAsync([new("确认本局信息", (600, 20, 200, 30))]);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 38, 388, 1344, 367))
            .ReturnsAsync([new("附加盟约", (100, 440, 200, 30))]);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .ReturnsAsync([new("规程等级7", (160, 40, 150, 25))]);
        using var scanner = CreateScanner(() => _screenshot, new());
        await scanner.ScanNowAsync();
        scanner.RegulationLevel.Should().Be(7);
        _ocr.Verify(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DifficultySurvivesCaptureLossAndResetsBeforeTheNextRun()
    {
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .ReturnsAsync([new("规程等级7", (160, 40, 150, 25))]);
        byte[]? shot = _screenshot;
        using var scanner = CreateScanner(() => shot, new());
        var levels = new List<int?>();
        scanner.RegulationLevelChanged += () => levels.Add(scanner.RegulationLevel);
        await scanner.ScanNowAsync();
        shot = null;
        await scanner.ScanNowAsync();
        scanner.RegulationLevel.Should().Be(7);
        scanner.ResetRegulationLevel();
        scanner.RegulationLevel.Should().BeNull();
        shot = _screenshot;
        await scanner.ScanNowAsync();
        levels.Should().Equal(7, null, 7);
        _ocr.Verify(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129), Times.Exactly(2));
    }

    [Fact]
    public async Task PeriodicDifficultyCorrectsADifferentManualReadWithoutRecollapsingAnUnchangedLevel()
    {
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .ReturnsAsync([new("规程等级7", (160, 40, 150, 25))]);
        using var scanner = CreateScanner(() => _screenshot, new());
        var vm = new EnemyViewModel();
        vm.Update(new("假想敌：胄", [], []),
            new() { Hp = [new() { Name = "规程等级7", Group = "single", Hp = 400000 }] }, []);
        scanner.RegulationLevelChanged += () => vm.RegulationLevel = scanner.RegulationLevel;
        await scanner.ScanNowAsync();
        vm.OtherBossHpExpanded = true;
        await Task.Delay(TimeSpan.FromSeconds(3.1));
        await scanner.ScanNowAsync();
        vm.OtherBossHpExpanded.Should().BeTrue("an unchanged confirmed level must preserve manual expansion");
        vm.RegulationLevel = 8;
        await Task.Delay(TimeSpan.FromSeconds(3.1));
        await scanner.ScanNowAsync();
        vm.RegulationLevel.Should().Be(7);
        vm.HasCurrentBossHp.Should().BeTrue();
    }

    [Fact]
    public async Task NewRunRejectsADifficultyReadStartedBeforeTheReset()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        using var scanner = CreateScanner(() => _screenshot, new());
        var scan = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scanner.ResetRegulationLevel();
        }
        finally
        {
            release.TrySetResult([new("规程等级7", (160, 40, 150, 25))]);
            await scan;
        }
        scanner.RegulationLevel.Should().BeNull();
        await scanner.ScanNowAsync();
        scanner.RegulationLevel.Should().Be(7);
    }

    [Fact]
    public async Task NewRunRejectsTheFrameCapturedBeforeOpeningDetectionCompletes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 480, 0, 960, 108))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .ReturnsAsync([new("规程等级7", (160, 40, 150, 25))]);
        using var scanner = CreateScanner(() => _screenshot, new());
        var scan = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scanner.ResetRegulationLevel();
        }
        finally
        {
            release.TrySetResult([]);
            await scan;
        }
        scanner.RegulationLevel.Should().BeNull();
        _ocr.Verify(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129), Times.Never);
        await scanner.ScanNowAsync();
        scanner.RegulationLevel.Should().Be(7);
    }

    [Fact]
    public async Task SwitchingCaptureConnectionRejectsThePreviousDifficulty()
    {
        using var previous = new GameConnectionService();
        using var next = new GameConnectionService(new() { Source = GameCaptureSource.Adb });
        var connection = previous;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        using var scanner = CreateScanner(() => _screenshot, new(), () => connection);
        var scan = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            connection = next;
        }
        finally
        {
            release.TrySetResult([new("规程等级7", (160, 40, 150, 25))]);
            await scan;
        }
        scanner.RegulationLevel.Should().BeNull();
        await scanner.ScanNowAsync();
        scanner.RegulationLevel.Should().Be(7);
    }

    [Fact]
    public async Task FirstAutomaticScanKeepsTheRunAlreadyStartedByManualRecording()
    {
        using var connection = new GameConnectionService();
        using var scanner = CreateScanner(() => _screenshot, new(), () => connection);
        scanner.TryStartNewRun(false).Should().BeTrue();
        scanner.MarkInformationRecorded();
        scanner.SetBanScreenDetectionPaused(true);
        await scanner.ScanNowAsync();
        scanner.TryStartNewRun(false).Should().BeFalse("the first scan is still using the same capture connection");
    }

    private readonly byte[] _screenshot;
    private readonly string[] _names = ["角峰", "隐现", "坚守盾牌"];
    private readonly Mock<IOcrEngine> _ocr = new();
    private IReadOnlyList<OcrTextResult> _shopTexts =
    [
        new("角峰", (1000, 390, 100, 40)),
        new("隐现", (1500, 390, 100, 40)),
        new("坚守盾牌", (2000, 390, 200, 40)),
        new("收起", (3500, 370, 160, 60))
    ];

    public OcrScannerServiceTests()
    {
        using var bitmap = new Bitmap(1920, 1080);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        _screenshot = stream.ToArray();
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int _, int _) => Task.FromResult<IReadOnlyList<OcrTextResult>>(
                x == 0 && y == 0 ? _shopTexts : []));
    }

    [Fact]
    public async Task CachedShopCanSwitchLineupsAndEquipmentWithoutAnotherCapture()
    {
        var captures = 0;
        var state = new ASPAssistant.Core.GameState.GameState();
        var tracking = new TrackingViewModel();
        using var scanner = CreateScanner(() => { captures++; return _screenshot; }, state);
        await scanner.ScanNowAsync();
        state.ShopItems.Select(i => i.Name).Should().Equal(_names);
        state.ShopItems.Should().OnlyContain(i => !i.IsTracked);
        var positions = state.ShopItems.Select(i => i.OcrRegion).ToArray();

        tracking.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰" }] });
        GameStateUpdater.UpdateShopTracking(state.ShopItems, tracking.AllEntries.ToList());
        state.ShopItems.Where(i => i.IsTracked).Select(i => i.Name).Should().Equal("角峰");
        tracking.TrackLineup(new Lineup { Slots = [new() { OperatorName = "隐现", Equipments = ["坚守盾牌"] }] });
        GameStateUpdater.UpdateShopTracking(state.ShopItems, tracking.AllEntries.ToList());
        state.ShopItems.Where(i => i.IsTracked).Select(i => i.Name).Should().Equal("隐现", "坚守盾牌");
        tracking.ClearAllTracking();
        GameStateUpdater.UpdateShopTracking(state.ShopItems, tracking.AllEntries.ToList());

        state.ShopItems.Should().OnlyContain(i => !i.IsTracked);
        state.ShopItems.Select(i => i.OcrRegion).Should().Equal(positions);
        captures.Should().Be(1);
    }

    [Fact]
    public async Task TemporaryCaptureFailureKeepsCacheButActualClosedShopClearsIt()
    {
        byte[]? capture = _screenshot;
        var state = new ASPAssistant.Core.GameState.GameState();
        using var scanner = CreateScanner(() => capture, state);
        var updates = 0;
        scanner.GameStateUpdated += _ => updates++;
        await scanner.ScanNowAsync();
        updates.Should().Be(1);

        capture = null;
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        state.ShopItems.Select(i => i.Name).Should().Equal(_names);
        updates.Should().Be(1);

        capture = _screenshot;
        await scanner.ScanNowAsync();
        updates.Should().Be(2, "returning to the game must publish a fresh result even if its names are unchanged");
        _shopTexts = [];
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        state.ShopItems.Should().BeEmpty("a successfully captured closed shop must remove old highlights");
    }

    [Fact]
    public async Task RepeatedImmediateRequestsShareTheRunningScan()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, It.IsAny<int>(), It.IsAny<int>()))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        var captures = 0;
        var state = new ASPAssistant.Core.GameState.GameState();
        using var scanner = CreateScanner(() => { Interlocked.Increment(ref captures); return _screenshot; }, state);
        var tracking = new TrackingViewModel();
        scanner.GameStateUpdated += result => GameStateUpdater.UpdateShopTracking(result.ShopItems, tracking.AllEntries.ToList());
        var first = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            tracking.TrackLineup(new Lineup { Slots = [new() { OperatorName = "隐现", Equipments = ["坚守盾牌"] }] });
            await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => scanner.ScanNowAsync()));
            captures.Should().Be(1);
        }
        finally
        {
            release.TrySetResult(_shopTexts);
            await first;
        }
        state.ShopItems.Should().HaveCount(3);
        state.ShopItems.Where(i => i.IsTracked).Select(i => i.Name).Should().Equal("隐现", "坚守盾牌");
        await scanner.ScanNowAsync();
        captures.Should().Be(2, "the scan gate must be released after completion");
    }

    [Fact]
    public async Task InformationOverlaySuppressesCachedShopHighlights()
    {
        var state = new ASPAssistant.Core.GameState.GameState();
        using var scanner = CreateScanner(() => _screenshot, state);
        await scanner.ScanNowAsync();
        scanner.SetBanScreenDetectionPaused(true);
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        state.ShopItems.Should().BeEmpty();
    }

    [Fact]
    public async Task AdbCaptureFailureClearsCachedShop()
    {
        using var connection = new GameConnectionService(new() { Source = GameCaptureSource.Adb });
        byte[]? capture = _screenshot;
        var state = new ASPAssistant.Core.GameState.GameState();
        using var scanner = CreateScanner(() => capture, state, () => connection);
        await scanner.ScanNowAsync();
        state.ShopItems.Select(i => i.Name).Should().Equal(_names);

        capture = null;
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();

        state.ShopItems.Should().BeEmpty();
    }

    [Fact]
    public async Task SwitchingConnectionDiscardsInFlightShopResult()
    {
        using var previous = new GameConnectionService();
        using var next = new GameConnectionService(new() { Source = GameCaptureSource.Adb });
        var connection = previous;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, It.IsAny<int>(), It.IsAny<int>()))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        var state = new ASPAssistant.Core.GameState.GameState();
        using var scanner = CreateScanner(() => _screenshot, state, () => connection);
        var scan = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            connection = next;
        }
        finally
        {
            release.TrySetResult(_shopTexts);
            await scan;
        }

        state.ShopItems.Should().BeEmpty();
        await scanner.ScanNowAsync();
        state.ShopItems.Select(i => i.Name).Should().Equal(_names);
    }

    private OcrScannerService CreateScanner(Func<byte[]?> capture, ASPAssistant.Core.GameState.GameState state,
        Func<GameConnectionService>? getConnection = null) => new(
        capture, new GarrisonOcrStrategy(), new ShopReader(_ocr.Object, _names) { Theme = GameShopTheme.Closure },
        _ocr.Object, state, _names, getConnection: getConnection);
}
