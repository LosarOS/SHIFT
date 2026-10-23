using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.ViewManagement;
namespace Shift.App;

public sealed partial class MainWindow
{
    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
            presenter.Restore();
        AppWindow.Show();
        Activate();
    }
    private void AddKeyboardShortcuts()
    {
        void Shortcut(VirtualKey key, Action action)
        {
            var shortcut = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
            shortcut.Invoked += (_, args) => { if (!trayReady || quitRequested || dialogOpen) return; action(); args.Handled = true; };
            root.KeyboardAccelerators.Add(shortcut);
        }
        Shortcut(VirtualKey.K, OpenQuickLauncher);
        Shortcut(VirtualKey.F, () => searchBox?.Focus(FocusState.Keyboard));
        Shortcut(VirtualKey.N, () => { if (compact) ExitQuickLauncher(); _ = SafeAsync(() => EditWorkspaceAsync(null)); });
        root.KeyDown += (_, args) => { if (args.Key == VirtualKey.Escape && compact && !dialogOpen) { ExitQuickLauncher(); args.Handled = true; } };
    }
    private void OpenQuickLauncher()
    {
        if (!trayReady || quitRequested)
            return;
        ShowAndActivate();
        if (dialogOpen)
            return;
        if (!compact)
        {
            fullSize = AppWindow.Size;
            compact = true;
            windowCommands.Compact = true;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(680, 660));
        }
        vm.Selected = null;
        vm.Section = "Quick";
        vm.Query = "";
        BuildShell();
        DispatcherQueue.TryEnqueue(() => searchBox?.Focus(FocusState.Keyboard));
    }
    private void ExitQuickLauncher()
    {
        if (!compact)
            return;
        compact = false;
        windowCommands.Compact = false;
        AppWindow.Resize(fullSize);
        vm.Section = "Workspaces";
        BuildShell();
    }
    private void RenderQuick()
    {
        var header = new FlowPanel();
        header.Children.Add(Text("↗ SHIFT", 26, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        header.Children.Add(Button(s["FullMode"] + "  ↗", ExitQuickLauncher, false, "Quick.Close"));
        screen.Children.Add(header);
        screen.Children.Add(Text(s["QuickHint"], 14, true));
        var search = new TextBox { PlaceholderText = s["Search"], Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(10) };
        searchBox = search;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, s["Search"]);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(search, "Quick.Search");
        screen.Children.Add(search);
        screen.Children.Add(Text(s["QuickKeys"], 11, true));
        var results = Stack(10);
        screen.Children.Add(results);
        Workspace[] matches = [];
        var selected = 0;
        var cards = new List<Border>();
        void Select(int index)
        {
            if (matches.Length == 0)
                return;
            selected = Math.Clamp(index, 0, matches.Length - 1);
            for (var row = 0; row < cards.Count; row++)
                cards[row].BorderBrush = Brush(row == selected ? palette.Accent : palette.Border);
            cards[selected].StartBringIntoView(new BringIntoViewOptions { AnimationDesired = systemUi.AnimationsEnabled });
        }
        void Refresh()
        {
            results.Children.Clear();
            cards.Clear();
            selected = 0;
            matches = vm.Workspaces.Where(w => w.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) || w.Tags.Any(t => t.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)))
                .OrderByDescending(w => w.IsFavorite).ThenByDescending(w => vm.Usage.Workspaces.FirstOrDefault(u => u.WorkspaceId == w.Id)?.LastLaunchedAt).ToArray();
            for (var index = 0; index < matches.Length; index++)
            {
                var workspace = matches[index];
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new()
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
                row.ColumnDefinitions.Add(new()
                {
                    Width = GridLength.Auto
                });
                var words = Stack(4);
                words.Children.Add(Text((workspace.IsFavorite ? "★  " : "↗  ") + workspace.Name, 17, weight: Microsoft.UI.Text.FontWeights.SemiBold));
                words.Children.Add(Text($"{workspace.Items.Count(i => i.Enabled)} {s["Items"]}", 12, true));
                row.Children.Add(words);
                var launch = AsyncButton(s["Launch"], async () => { ExitQuickLauncher(); await StartLaunchAsync(workspace); }, true);
                launch.IsEnabled = !vm.IsLaunching;
                Grid.SetColumn(launch, 1);
                row.Children.Add(launch);
                var card = Card(row, 16);
                card.BorderThickness = new Thickness(2);
                card.BorderBrush = Brush(index == 0 ? palette.Accent : palette.Border);
                cards.Add(card);
                results.Children.Add(card);
            }
            if (matches.Length == 0)
                results.Children.Add(Text(s["NoResults"], 16, true));
        }
        search.TextChanged += (_, _) => Refresh();
        search.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(async (_, args) =>
        {
            if (args.Key == VirtualKey.Down)
            {
                Select(selected + 1);
                args.Handled = true;
                return;
            }
            if (args.Key == VirtualKey.Up)
            {
                Select(selected - 1);
                args.Handled = true;
                return;
            }
            if (args.Key != VirtualKey.Enter || vm.IsLaunching || matches.Length == 0)
                return;
            args.Handled = true;
            var workspace = matches[selected];
            ExitQuickLauncher();
            await SafeAsync(() => StartLaunchAsync(workspace));
        }), true);
        Refresh();
    }
    private Palette HighContrastPalette()
    {
        static string Hex(Windows.UI.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var background = Hex(systemUi.GetColorValue(UIColorType.Background));
        var foreground = Hex(systemUi.GetColorValue(UIColorType.Foreground));
        return new(background, background, background, foreground, foreground, foreground, foreground, background);
    }
    private void OnSystemSettingsChanged() => DispatcherQueue.TryEnqueue(() => { if (!trayReady) return; ApplyTheme(); BuildShell(); });
}
