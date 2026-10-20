using System.Diagnostics;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.UI;
namespace Shift.App;

public sealed partial class MainWindow : Window, ICommandConsent
{
    private readonly AppPaths paths;
    private readonly LocalDataStore store;
    private readonly FileLog log;
    private readonly DesktopIntegration desktop = new();
    private readonly ShellViewModel vm;
    private readonly Strings s = new();
    private readonly Grid root = new();
    private readonly Grid shell = new();
    private readonly StackPanel navigation = new() { Spacing = 6 };
    private readonly StackPanel screen = new() { Spacing = 24 };
    private readonly InfoBar notice = new() { IsClosable = true, Margin = new Thickness(24, 8, 24, 12) };
    private ScrollViewer? contentScroll;
    private readonly WindowCommands windowCommands;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings accessibility = new();
    private readonly Windows.UI.ViewManagement.UISettings systemUi = new();
    private bool compact;
    private bool maintenance;
    private Windows.Graphics.SizeInt32 fullSize = new(1240, 840);
    private TextBox? searchBox;
    private int activeOperations;
    private TaskCompletionSource idleOperations = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int cardColumns = 2;
    private bool initialized;
    private bool exiting;
    private bool quitRequested;
    private bool trayReady;
    private bool dialogOpen;
    private Task? launchTask;
    private Palette palette = Palette.For(AppTheme.Dark);
    private nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public MainWindow(AppPaths paths)
    {
        this.paths = paths;
        store = new(paths.DatabasePath);
        log = new(paths.LogDirectory);
        vm = new(store, new LaunchCoordinator(store, new WindowsItemLauncher(), log));
        Title = "SHIFT";
        idleOperations.SetResult();
        windowCommands = new(Handle, () => DispatcherQueue.TryEnqueue(OpenQuickLauncher));
        AddKeyboardShortcuts();
        accessibility.HighContrastChanged += OnHighContrastChanged;
        AppWindow.Resize(new SizeInt32(1240, 840));
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "shift.ico");
        if (File.Exists(icon))
            AppWindow.SetIcon(icon);
        root.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        root.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        root.Children.Add(shell);
        Grid.SetRow(notice, 1);
        root.Children.Add(notice);
        Content = root;
        shell.Children.Add(new ProgressRing { IsActive = true, Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        AppWindow.Closing += OnClosing;
        Closed += (_, _) => { accessibility.HighContrastChanged -= OnHighContrastChanged; windowCommands.Dispose(); desktop.Dispose(); };
        root.SizeChanged += (_, _) =>
        {
            var columns = root.ActualWidth < 1080 ? 1 : 2;
            if (columns == cardColumns)
                return;
            cardColumns = columns;
            if (trayReady && vm.Selected is null && vm.Section is "Overview" or "Workspaces" or "Favorites")
                RenderScreen();
        };
        root.Loaded += async (_, _) => { if (!initialized) { initialized = true; SizeForDisplay(); await SafeAsync(InitializeAsync); } };
    }
    private void SizeForDisplay()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var scale = root.XamlRoot?.RasterizationScale ?? 1;
        var width = Math.Min((int)(1240 * scale), Math.Max(600, display.WorkArea.Width - 48));
        var height = Math.Min((int)(840 * scale), Math.Max(440, display.WorkArea.Height - 48));
        AppWindow.Resize(new(width, height));
        AppWindow.Move(new(display.WorkArea.X + (display.WorkArea.Width - width) / 2, display.WorkArea.Y + (display.WorkArea.Height - height) / 2));
    }
    private async Task InitializeAsync()
    {
        try
        {
            var existing = File.Exists(paths.DatabasePath);
            await store.InitializeAsync();
            await store.RecoverInterruptedAsync();
            vm.Settings = await store.LoadSettingsAsync();
            s.Language = vm.Settings.Language;
            vm.Settings = vm.Settings with
            {
                StartWithWindows = !paths.Portable && desktop.IsStartupEnabled()
            };
            await vm.RefreshAsync();
            ApplyTheme();
            BuildShell();
            ConfigureTray();
            try
            {
                windowCommands.ConfigureHotkey(vm.Settings.GlobalHotkeyEnabled);
            }
            catch (Exception ex) { log.Error("Global shortcut unavailable", ex); ShowNotice(s["Error"], ex.Message, true); }
            if (existing)
            {
                try
                {
                    // Once per UTC date; auto backups are bounded. Manual backups are never pruned.
                    var auto = Path.Combine(BackupDirectory, "automatic");
                    Directory.CreateDirectory(auto);
                    if (!Directory.EnumerateFiles(auto, "SHIFT-*.db").Any(f => File.GetLastWriteTimeUtc(f).Date == DateTime.UtcNow.Date))
                        await store.BackupAsync(auto);
                    foreach (var old in Directory.EnumerateFiles(auto, "SHIFT-*.db").OrderByDescending(File.GetLastWriteTimeUtc).Skip(10))
                        File.Delete(old);
                }
                catch (Exception ex) { log.Error("Automatic backup failed", ex); ShowNotice(s["Error"], ex.Message, true); }
            }
            if (!vm.Settings.OnboardingComplete && !quitRequested)
                await OnboardAsync();
            log.Info("SHIFT started.");
        }
        catch (Exception ex)
        {
            log.Error("Startup failed", ex);
            ApplyTheme();
            shell.Children.Clear();
            var failure = Stack(20);
            failure.Margin = new Thickness(64);
            failure.Children.Add(Text(s["StartupError"], 28));
            failure.Children.Add(Text(s["StartupErrorNote"], 16, true));
            failure.Children.Add(Text(ex.Message, 14, true));
            failure.Children.Add(Button(s["OpenData"], () => DesktopIntegration.OpenFolder(paths.DataDirectory)));
            failure.Children.Add(Button(s["OpenLogs"], () => DesktopIntegration.OpenFolder(paths.LogDirectory)));
            shell.Children.Add(failure);
        }
    }
    private string BackupDirectory => string.IsNullOrWhiteSpace(vm.Settings.BackupDirectory) ? paths.BackupDirectory : vm.Settings.BackupDirectory;
    private void ConfigureTray()
    {
        desktop.CreateTray(() => DispatcherQueue.TryEnqueue(ShowAndActivate), () => DispatcherQueue.TryEnqueue(() => { _ = QuitAsync(); }), s["OpenShift"], s["Quit"]);
        trayReady = true;
    }
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (exiting)
            return;
        if (quitRequested)
        {
            args.Cancel = true;
            return;
        }
        if (trayReady && vm.Settings.MinimizeToTray)
        {
            args.Cancel = true;
            AppWindow.Hide();
            return;
        }
        if (vm.IsLaunching || activeOperations > 0)
        {
            args.Cancel = true;
            _ = QuitAsync();
        }
        else
        {
            exiting = true;
            desktop.Dispose();
        }
    }
    private async Task QuitAsync()
    {
        if (dialogOpen)
        {
            ShowAndActivate();
            return;
        }
        if (quitRequested)
            return;
        quitRequested = true;
        try
        {
            await SafeAsync(async () =>
            {
                if (vm.IsLaunching)
                {
                    ShowAndActivate();
                    if (!await ConfirmAsync(s["QuitLaunch"], s["QuitLaunchNote"], s["Quit"]))
                        return;
                    vm.LaunchCancellation?.Cancel();
                    if (launchTask is not null)
                        await launchTask;
                }
                await idleOperations.Task;
                exiting = true;
                desktop.Dispose();
                Close();
            }, track: false);
        }
        finally { quitRequested = false; }
    }
    private void ApplyTheme()
    {
        palette = accessibility.HighContrast ? HighContrastPalette() : Palette.For(vm.Settings.Theme);
        root.RequestedTheme = vm.Settings.Theme == AppTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
        root.Background = Brush(palette.Background);
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var bar = AppWindow.TitleBar;
            bar.BackgroundColor = ColorOf(palette.Background);
            bar.ForegroundColor = ColorOf(palette.Text);
            bar.ButtonBackgroundColor = ColorOf(palette.Background);
            bar.ButtonForegroundColor = ColorOf(palette.Text);
            bar.InactiveBackgroundColor = ColorOf(palette.Background);
            bar.ButtonInactiveBackgroundColor = ColorOf(palette.Background);
        }
    }
    private void BuildShell()
    {
        // Persistent panels must be detached before a theme/language rebuild reparents them.
        if (navigation.Parent is Panel oldSide)
            oldSide.Children.Remove(navigation);
        if (contentScroll is not null)
            contentScroll.Content = null;
        shell.Children.Clear();
        shell.ColumnDefinitions.Clear();
        shell.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(224)
        });
        shell.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        var side = new Grid { Background = Brush(palette.Sidebar), Padding = new Thickness(20, 28, 20, 24), BorderBrush = Brush(palette.Border), BorderThickness = new Thickness(0, 0, 1, 0) };
        side.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        side.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        side.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        var brand = Stack(8);
        var mark = Row(10);
        mark.Children.Add(new Border { Background = Brush(palette.Accent), CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 4, 9, 4), Child = Text("↗", 22, color: palette.Background) });
        mark.Children.Add(Text("SHIFT", 25, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        brand.Children.Add(mark);
        brand.Children.Add(Text(s["Tagline"], 12, true));
        side.Children.Add(brand);
        navigation.Margin = new Thickness(0, 42, 0, 0);
        Grid.SetRow(navigation, 1);
        side.Children.Add(navigation);
        var foot = Stack(10);
        foot.Children.Add(Text("●  " + s["Local"], 11, color: palette.Accent));
        foot.Children.Add(Text(AppInfo.Version, 10, true));
        foot.Children.Add(Button(s["Quick"] + "  Ctrl+K", OpenQuickLauncher, false, "Quick.Open"));
        Grid.SetRow(foot, 2);
        side.Children.Add(foot);
        shell.Children.Add(side);
        contentScroll = new ScrollViewer { Content = screen, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        screen.Margin = new Thickness(36, 28, 36, 32);
        Grid.SetColumn(contentScroll, 1);
        shell.Children.Add(contentScroll);
        shell.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 224);
        side.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        screen.Margin = compact ? new Thickness(22) : new Thickness(36, 28, 36, 32);
        RenderNavigation();
        RenderScreen();
    }
    private void RenderNavigation()
    {
        navigation.Children.Clear();
        foreach (var (key, glyph) in new[] { ("Overview", "\uE80F"), ("Workspaces", "\uE8F1"), ("Favorites", "\uE734"), ("History", "\uE81C"), ("Settings", "\uE713") })
        {
            var target = key;
            var row = Row(12);
            row.Children.Add(Glyph(glyph, 18));
            row.Children.Add(Text(s[key], 14));
            var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 12, 12, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0), Background = Brush(vm.Section == key ? palette.Selected : palette.Sidebar) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, s[key]);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "Nav." + key);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(button, vm.Section == key ? s["CurrentSection"] : "");
            button.Click += (_, _) => { vm.Section = target; vm.Selected = null; vm.Query = ""; RenderNavigation(); RenderScreen(); };
            navigation.Children.Add(button);
        }
        if (vm.IsLaunching)
            navigation.Children.Add(Button("◌  " + s["LaunchQueue"], () => { vm.Section = "Launch"; RenderScreen(); }));
    }
    private void RenderScreen()
    {
        screen.Children.Clear();
        searchBox = null;
        screen.ChildrenTransitions = systemUi.AnimationsEnabled && vm.Section is not ("Launch" or "Quick")
            ? new Microsoft.UI.Xaml.Media.Animation.TransitionCollection { new Microsoft.UI.Xaml.Media.Animation.EntranceThemeTransition { FromVerticalOffset = 8, IsStaggeringEnabled = false } } : null;
        if (vm.Section == "Quick")
        {
            RenderQuick();
            return;
        }
        if (vm.Section == "Launch")
        {
            RenderLaunch();
            return;
        }
        if (vm.Selected is not null)
        {
            RenderDetails(vm.Selected);
            return;
        }
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        heading.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var words = Stack(7);
        words.Children.Add(Text("SHIFT  /  " + s[vm.Section].ToUpper(s.Language == "ru" ? System.Globalization.CultureInfo.GetCultureInfo("ru") : System.Globalization.CultureInfo.InvariantCulture), 10, true));
        words.Children.Add(Text(s[vm.Section], 30, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        heading.Children.Add(words);
        if (vm.Section is "Overview" or "Workspaces" or "Favorites")
        {
            var actions = new FlowPanel();
            actions.Children.Add(AsyncButton(s["Import"], ImportAsync));
            actions.Children.Add(AsyncButton("+  " + s["New"], () => EditWorkspaceAsync(null), true, "Workspace.New"));
            if (root.ActualWidth < 1120)
            {
                heading.RowDefinitions.Add(new()
                {
                    Height = GridLength.Auto
                });
                heading.RowDefinitions.Add(new()
                {
                    Height = GridLength.Auto
                });
                Grid.SetRow(actions, 1);
                actions.Margin = new Thickness(0, 16, 0, 0);
                Grid.SetColumnSpan(actions, 2);
            }
            else
                Grid.SetColumn(actions, 1);
            heading.Children.Add(actions);
        }
        screen.Children.Add(heading);
        switch (vm.Section)
        {
            case "Overview":
                RenderOverview();
                break;
            case "Workspaces":
            case "Favorites":
                RenderWorkspaces();
                break;
            case "History":
                RenderHistory();
                break;
            case "Settings":
                RenderSettings();
                break;
        }
    }
    private void OpenWorkspace(Workspace workspace)
    {
        vm.Selected = workspace;
        vm.Section = "Workspaces";
        RenderNavigation();
        RenderScreen();
    }
    private async Task SafeAsync(Func<Task> action, bool track = true)
    {
        if (track && (maintenance || quitRequested))
        {
            ShowNotice(s[maintenance ? "DataOperation" : "Closing"], error: true);
            return;
        }
        if (track && activeOperations++ == 0)
            idleOperations = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await action();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.Error("User action failed", ex); ShowNotice(s["Error"], (ex is ValidationException ? s["ValidationHint"] + "\n" : "") + s.ErrorDetails(ex), true); }
        finally { if (track && --activeOperations == 0) idleOperations.TrySetResult(); }
    }
    private void ShowNotice(string title, string message = "", bool error = false)
    {
        notice.Title = title;
        notice.Message = message.Length > 1800 ? message[..1800] : message;
        notice.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        notice.IsOpen = true;
    }
    private static StackPanel Stack(double spacing = 12) => new() { Spacing = spacing };
    private static StackPanel Row(double spacing = 12) => new() { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
    private TextBlock Text(string value, double size = 14, bool muted = false, string? color = null, Windows.UI.Text.FontWeight? weight = null) => new() { Text = value, FontSize = size, Foreground = Brush(accessibility.HighContrast ? palette.Text : color ?? (muted ? palette.Muted : palette.Text)), FontFamily = new FontFamily(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? "Segoe UI Variable Text" : "Segoe UI"), TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? Microsoft.UI.Text.FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
    private TextBlock Glyph(string value, double size = 18) => new() { Text = value, FontSize = size, FontFamily = new FontFamily(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? "Segoe Fluent Icons" : "Segoe MDL2 Assets"), Foreground = Brush(palette.Accent), VerticalAlignment = VerticalAlignment.Center };
    private Border Card(UIElement child, double padding = 22) => new() { Child = child, Padding = new Thickness(padding), Background = Brush(palette.Surface), BorderBrush = Brush(palette.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14) };
    private Button Button(string label, Action action, bool primary = false, string? automationId = null)
    {
        var button = new Button { Content = label, Padding = new Thickness(16, 10, 16, 10), CornerRadius = new CornerRadius(8) };
        if (automationId is not null)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, automationId);
        if (primary)
        {
            button.Background = Brush(palette.Accent);
            button.Foreground = Brush(accessibility.HighContrast ? palette.Background : "#17151F");
            button.BorderThickness = new Thickness(0);
        }
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { log.Error("Desktop action failed", ex); ShowNotice(s["Error"], ex.Message, true); } };
        return button;
    }
    private Button AsyncButton(string label, Func<Task> action, bool primary = false, string? automationId = null)
    {
        var button = Button(label, () => { }, primary, automationId);
        button.Click += async (_, _) => await SafeAsync(action);
        return button;
    }
    private static Color ColorOf(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16));
    }
    private static SolidColorBrush Brush(string hex) => new(ColorOf(hex));
    private sealed record Palette(string Background, string Sidebar, string Surface, string Border, string Text, string Muted, string Accent, string Selected)
    {
        public static Palette For(AppTheme theme) => theme switch
        {
            AppTheme.Light => new("#F5F5F8", "#EEEEF3", "#FFFFFF", "#DEDEE8", "#20212B", "#686879", "#B6A5FF", "#DEDAF1"),
            AppTheme.Midnight => new("#0D1423", "#0A101D", "#151F32", "#26324A", "#ECF1FF", "#95A4C0", "#98BEFF", "#233454"),
            AppTheme.Forest => new("#101B18", "#0D1714", "#192722", "#2A3A32", "#ECF3EE", "#9AAF9F", "#A9D4A5", "#2A4032"),
            _ => new("#17181E", "#121318", "#202128", "#30313B", "#F3F2F7", "#A1A1B3", "#BCADFF", "#302C43")
        };
    }
}
