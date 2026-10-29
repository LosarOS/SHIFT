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
        screen.Children.Add(Text(T("↑ ↓ — выбор · Enter — запуск · Tab — действия · Esc — полный режим",
            "↑ ↓ to select · Enter to launch · Tab for actions · Esc for full mode"), 11, true));
        var resultCount = Text("", 12, true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(resultCount, "Quick.ResultCount");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(resultCount, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        screen.Children.Add(resultCount);
        // A real selection control exposes SelectionPattern and keyboard focus to accessibility tools.
        // A colored border alone cannot tell Narrator which workspace Enter will dispatch.
        var results = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MaxHeight = 320,
            IsItemClickEnabled = false
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(results, T("Найденные пространства", "Matching workspaces"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(results, "Quick.Results");
        screen.Children.Add(results);
        var empty = Text(s["NoResults"], 16, true);
        screen.Children.Add(empty);
        Workspace[] matches = [];
        Workspace? Selected() => results.SelectedIndex >= 0 && results.SelectedIndex < matches.Length ? matches[results.SelectedIndex] : null;
        async Task LaunchSelected()
        {
            if (vm.IsLaunching || Selected() is not { } workspace) return;
            ExitQuickLauncher();
            await StartLaunchAsync(workspace);
        }
        var actions = new FlowPanel();
        var launch = AsyncButton(s["Launch"], LaunchSelected, true, "Quick.Launch");
        var review = Button(s["Review"], () =>
        {
            if (Selected() is not { } workspace) return;
            ExitQuickLauncher();
            OpenWorkspace(workspace);
        }, false, "Quick.Review");
        actions.Children.Add(launch);
        actions.Children.Add(review);
        screen.Children.Add(actions);
        void UpdateActions()
        {
            var workspace = Selected();
            launch.IsEnabled = workspace is not null && !vm.IsLaunching;
            review.IsEnabled = workspace is not null;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(launch,
                workspace is null ? s["Launch"] : s["Launch"] + ": " + workspace.Name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(review,
                workspace is null ? s["Review"] : s["Review"] + ": " + workspace.Name);
        }
        void Select(int index)
        {
            if (matches.Length == 0) return;
            results.SelectedIndex = Math.Clamp(index, 0, matches.Length - 1);
            results.ScrollIntoView(results.SelectedItem);
        }
        void Refresh()
        {
            // Match words independently, just like the full workspace view. Whitespace-only input
            // must not hide every workspace, and a description is useful searchable context.
            var terms = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            matches = vm.Workspaces.Where(w => terms.All(term =>
                w.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || w.Description.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || w.Tags.Any(t => t.Contains(term, StringComparison.CurrentCultureIgnoreCase))))
                .OrderByDescending(w => w.IsFavorite)
                .ThenByDescending(w => vm.Usage.Workspaces.FirstOrDefault(u => u.WorkspaceId == w.Id)?.LastLaunchedAt)
                .ThenBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            results.Items.Clear();
            foreach (var workspace in matches)
            {
                var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(4, 8, 4, 8) };
                row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var words = Stack(4);
                var title = Text((workspace.IsFavorite ? "★  " : "↗  ") + workspace.Name, 17, weight: Microsoft.UI.Text.FontWeights.SemiBold);
                title.MaxLines = 1;
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                ToolTipService.SetToolTip(title, workspace.Name);
                words.Children.Add(title);
                var detail = Text(workspace.Tags.Count > 0 ? string.Join(" · ", workspace.Tags) : workspace.Description, 12, true);
                detail.MaxLines = 1;
                detail.TextTrimming = TextTrimming.CharacterEllipsis;
                words.Children.Add(detail);
                row.Children.Add(words);
                var enabled = workspace.Items.Count(i => i.Enabled);
                words.Children.Add(Text($"{s["ItemsLabel"]}: {enabled}", 12, true));
                var rowLaunch = AsyncButton(s["Launch"], async () =>
                {
                    if (vm.IsLaunching) return;
                    ExitQuickLauncher();
                    await StartLaunchAsync(workspace);
                }, false, "Quick.Dispatch." + workspace.Id.ToString("N"));
                rowLaunch.IsEnabled = !vm.IsLaunching;
                rowLaunch.VerticalAlignment = VerticalAlignment.Center;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(rowLaunch, s["Launch"] + ": " + workspace.Name);
                Grid.SetColumn(rowLaunch, 1);
                row.Children.Add(rowLaunch);
                var entry = new ListViewItem { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(entry,
                    workspace.Name + ". " + s["ItemsLabel"] + ": " + enabled + (workspace.IsFavorite ? ". " + s["Favorite"] : ""));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(entry, "Quick.Workspace." + workspace.Id.ToString("N"));
                results.Items.Add(entry);
            }
            results.Visibility = matches.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            resultCount.Text = T("Найдено пространств: ", "Matching workspaces: ") + matches.Length;
            results.SelectedIndex = matches.Length > 0 ? 0 : -1;
            UpdateActions();
        }
        results.SelectionChanged += (_, _) => UpdateActions();
        results.KeyDown += async (_, args) =>
        {
            if (args.Key != VirtualKey.Enter || vm.IsLaunching || Selected() is null) return;
            // The per-row button handles its own Enter; never dispatch the selected row as well.
            for (var origin = args.OriginalSource as DependencyObject; origin is not null && origin != results; origin = VisualTreeHelper.GetParent(origin))
                if (origin is Button) return;
            args.Handled = true;
            await SafeAsync(LaunchSelected);
        };
        search.TextChanged += (_, _) => Refresh();
        search.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(async (_, args) =>
        {
            if (args.Key == VirtualKey.Down || args.Key == VirtualKey.Up)
            {
                Select(results.SelectedIndex + (args.Key == VirtualKey.Down ? 1 : -1));
                args.Handled = true;
                return;
            }
            if (args.Key != VirtualKey.Enter || vm.IsLaunching || Selected() is null) return;
            args.Handled = true;
            await SafeAsync(LaunchSelected);
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
