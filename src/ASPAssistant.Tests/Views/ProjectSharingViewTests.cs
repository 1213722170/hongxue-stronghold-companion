using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class ProjectSharingViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var operators = store.LoadOperatorsAsync().GetAwaiter().GetResult();
        var strategies = store.LoadStrategiesAsync().GetAwaiter().GetResult();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var directory = Path.Combine(Path.GetTempPath(), "project-sharing-" + Guid.NewGuid().ToString("N"));
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        using var handler = new GuideShareHandler();
        using var http = new HttpClient(handler);
        var share = new LineupShareService(calculator.EquipmentRules, Path.Combine(directory, "share"), http);
        var panel = new SidePanelWindow(new OperatorBrowseViewModel(), new EquipmentBrowseViewModel(),
            new TrackingViewModel(), new GameStateViewModel(), new BanViewModel(), new EnemyViewModel(),
            new LineupViewModel(new LineupStore(Path.Combine(directory, "lineups"), calculator.EquipmentRules), calculator),
            share, operators, [], strategies,
            new SettingsManager(directory), theme, new SidePanelPositionSettings { FreeMovement = true })
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Height = 650 };
        panel.Show();
        ThemeManager.Apply(theme, night);
        try
        {
            ((Button)panel.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var settings = (Border)panel.FindName("SettingsPanel");
            settings.Visibility.Should().Be(Visibility.Visible);
            WaitUntil(() => settings.Opacity >= 0.99999 && settings.RenderTransform is TranslateTransform move && Math.Abs(move.X) < 0.01);
            // Freeze the completed state: an invisible test window may retain an intermediate compositor frame.
            settings.BeginAnimation(UIElement.OpacityProperty, null);
            settings.Opacity = 1;
            var move = (TranslateTransform)settings.RenderTransform;
            move.BeginAnimation(TranslateTransform.XProperty, null);
            move.BeginAnimation(TranslateTransform.YProperty, null);
            move.X = move.Y = 0;
            var label = (TextBlock)panel.FindName("ProjectAddressText");
            var link = (Hyperlink)panel.FindName("ProjectAddressLink");
            new TextRange(link.ContentStart, link.ContentEnd).Text.Should().Be("项目地址，欢迎star");
            link.NavigateUri.AbsoluteUri.Should().Be("https://github.com/bingyunhe/hongxue-stronghold-companion");
            var scroll = Descendants<ScrollViewer>(settings).First();
            foreach (int width in new[] { 280, 374 })
            {
                panel.Width = width;
                Pump();
                scroll.ScrollToEnd();
                Pump();
                var update = (Button)panel.FindName("CheckUpdateButton");
                label.TranslatePoint(new Point(), settings).Y.Should().BeGreaterThan(update.TranslatePoint(new Point(0, update.ActualHeight), settings).Y);
                label.TranslatePoint(new Point(0, label.ActualHeight), scroll).Y.Should().BeLessThanOrEqualTo(scroll.ViewportHeight + 1);
                Contrast(((SolidColorBrush)link.Foreground).Color, ((SolidColorBrush)settings.Background).Color)
                    .Should().BeGreaterThanOrEqualTo(4.5, $"project address in {theme}, night={night}");
                Save((FrameworkElement)panel.Content, $"settings-{theme}-{night}-{width}");
            }

            var import = share.ImportOfficialGuideAsync(Services.SklandGuideImporterTests.Url, operators, strategies);
            WaitUntil(() => import.IsCompleted);
            var raw = import.GetAwaiter().GetResult().Lineup;
            var legacy = new LineupEditorViewModel(raw, operators, [], calculator).ToLineup();
            legacy.SourceContentHash.Should().BeNull();
            var originalLink = panel.PrepareLineupShareAsync(legacy);
            WaitUntil(() => originalLink.IsCompleted);
            originalLink.GetAwaiter().GetResult().Should().Be(Services.SklandGuideImporterTests.Url);
            handler.GuideCalls.Should().Be(2, "legacy content is compared with the original guide once");
            handler.Uploads.Should().Be(0);
            panel.PrepareLineupShareAsync(legacy).GetAwaiter().GetResult().Should().Be(Services.SklandGuideImporterTests.Url);
            handler.GuideCalls.Should().Be(2, "the checked baseline is reused during this session");
            var edited = JsonSerializer.Deserialize<Lineup>(JsonSerializer.Serialize(legacy))!;
            edited.SourceContentHash = null;
            edited.Name = "本地修改后的攻略阵容";
            var localLink = panel.PrepareLineupShareAsync(edited);
            WaitUntil(() => localLink.IsCompleted);
            localLink.GetAwaiter().GetResult().Should().StartWith("https://dpaste.com/");
            handler.GuideCalls.Should().Be(3);
            handler.Uploads.Should().Be(1);
            handler.UploadedContent.Should().Contain("sourceContentHash");
            var unavailableSource = JsonSerializer.Deserialize<Lineup>(JsonSerializer.Serialize(edited))!;
            unavailableSource.SourceContentHash = null;
            unavailableSource.Name = "原攻略不可用时仍保留本地修改";
            handler.FailGuide = true;
            var fallbackLink = panel.PrepareLineupShareAsync(unavailableSource);
            WaitUntil(() => fallbackLink.IsCompleted);
            fallbackLink.GetAwaiter().GetResult().Should().StartWith("https://dpaste.com/");
            handler.Uploads.Should().Be(2);
            unavailableSource.SourceContentHash.Should().BeNull();
        }
        finally { panel.Close(); }

        foreach (bool official in new[] { false, true })
        {
            string url = official ? Services.SklandGuideImporterTests.Url : "https://dpaste.com/TEST12345";
            string expected = official ? url : $"<本地阵容> {url}";
            string copied = "";
            int attempts = 0;
            var dialog = new ShareLinkWindow("本地阵容", url, value =>
            {
                attempts++;
                if (attempts < 3) throw new COMException("Clipboard busy", unchecked((int)0x800401D0));
                copied = value;
            }) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
            dialog.Show();
            try
            {
                WaitUntil(() => ((Button)dialog.FindName("CopyButton")).IsEnabled && copied.Length > 0);
                attempts.Should().Be(3);
                copied.Should().Be(expected);
                ((TextBox)dialog.FindName("LinkBox")).Text.Should().Be(expected);
                if (official)
                {
                    SklandGuideImporter.IsOfficialGuideUrl(copied).Should().BeTrue("the shared text can be pasted directly into guide import");
                    ((TextBlock)dialog.FindName("ShareTitleText")).Text.Should().Contain("森空岛原攻略");
                    ((TextBlock)dialog.FindName("ShareDescriptionText")).Text.Should().Contain("与导入时一致");
                }
                var ratios = new Dictionary<string, double>();
                foreach (string name in new[] { "ShareDescriptionText", "LinkBox", "CopyStatusText", "CopyButton" })
                {
                    var element = (FrameworkElement)dialog.FindName(name);
                    var foreground = (SolidColorBrush)(element is TextBlock text ? text.Foreground : ((Control)element).Foreground);
                    var background = (SolidColorBrush)(name is "CopyButton" or "LinkBox" ? ((Control)element).Background : dialog.Background);
                    double ratio = Contrast(foreground.Color, background.Color);
                    ratios[name] = ratio;
                    ratio.Should().BeGreaterThanOrEqualTo(4.5, $"{name} in {theme}, night={night}, originalGuide={official}");
                }
                Save((FrameworkElement)dialog.Content, $"share-{theme}-{night}-{official}");
                File.WriteAllText(Path.Combine(OutputDirectory, $"contrast-share-{theme}-{night}-{official}.json"),
                    System.Text.Json.JsonSerializer.Serialize(ratios));
            }
            finally { dialog.Close(); }
        }

        int failedAttempts = 0;
        bool busy = true;
        var fallback = new ShareLinkWindow("本地阵容", Services.SklandGuideImporterTests.Url, _ =>
        {
            failedAttempts++;
            if (busy) throw new COMException("Clipboard busy", unchecked((int)0x800401D0));
        }) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
        fallback.Show();
        try
        {
            WaitUntil(() => failedAttempts == 3 && ((Button)fallback.FindName("CopyButton")).IsEnabled);
            var status = (TextBlock)fallback.FindName("CopyStatusText");
            status.Text.Should().Contain("原攻略链接已准备").And.Contain("手动复制");
            var input = (TextBox)fallback.FindName("LinkBox");
            input.IsReadOnly.Should().BeTrue();
            input.Text.Should().Be(Services.SklandGuideImporterTests.Url);
            input.SelectionLength.Should().Be(input.Text.Length);
            busy = false;
            fallback.CopyLinkAsync().GetAwaiter().GetResult();
            failedAttempts.Should().Be(4);
            status.Text.Should().Contain("已复制");
        }
        finally { fallback.Close(); }
    }

    private static string OutputDirectory => Path.Combine(AppContext.BaseDirectory, "TestResults", "project-sharing");

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void WaitUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(5); }
        condition().Should().BeTrue("the UI operation must finish within five seconds");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Save(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(OutputDirectory);
        using var file = File.Create(Path.Combine(OutputDirectory, name + ".png"));
        encoder.Save(file);
    }

    private static double Contrast(Color a, Color b)
    {
        static double Luminance(Color c)
        {
            static double Linear(byte v) => v / 255.0 <= 0.04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4);
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }
        double left = Luminance(a), right = Luminance(b);
        return (Math.Max(left, right) + 0.05) / (Math.Min(left, right) + 0.05);
    }

    private sealed class GuideShareHandler : HttpMessageHandler
    {
        public int GuideCalls { get; private set; }
        public int Uploads { get; private set; }
        public bool FailGuide { get; set; }
        public string UploadedContent { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                GuideCalls++;
                if (FailGuide) return new(HttpStatusCode.ServiceUnavailable);
                return new(HttpStatusCode.OK) { Content = new StringContent(await File.ReadAllTextAsync(
                    Path.Combine(AppContext.BaseDirectory, "TestData", "skland-guide-import.json"), cancellationToken)) };
            }
            request.Method.Should().Be(HttpMethod.Post);
            Uploads++;
            UploadedContent = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new(HttpStatusCode.OK) { Content = new StringContent($"https://dpaste.com/LOCAL{Uploads}") };
        }
    }
}
