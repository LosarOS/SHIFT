namespace Shift.App;

public sealed partial class MainWindow
{
    private void RenderOverview()
    {
        var hero = Stack(18);
        hero.Children.Add(Text("↗  " + s["Ready"], 11, color: palette.Accent));
        hero.Children.Add(Text(s["Welcome"], 36, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        hero.Children.Add(Text(s["WelcomeNote"], 15, true));
        hero.Children.Add(AsyncButton(s["Setup.Create"], OpenQuickSetupAsync, true, "Workspace.QuickSetup"));
        var heroCard = Card(hero, 28);
        heroCard.Background = Brush(palette.Selected);
        screen.Children.Add(heroCard);
        var stats = new Grid { ColumnSpacing = 14 };
        for (var i = 0; i < 3; i++)
            stats.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        (long, string)[] values = [(vm.Workspaces.Count, s["SpacesCount"]), (vm.Workspaces.Count(w => w.IsFavorite), s["FavoritesCount"]), (vm.Usage.TotalRuns, s["RunsCount"])];
        for (var i = 0; i < values.Length; i++)
        {
            var stat = Stack(5);
            stat.Children.Add(Text(values[i].Item1.ToString(), 26, weight: Microsoft.UI.Text.FontWeights.SemiBold));
            stat.Children.Add(Text(values[i].Item2, 12, true));
            var card = Card(stat, 18);
            Grid.SetColumn(card, i);
            stats.Children.Add(card);
        }
        screen.Children.Add(stats);
        screen.Children.Add(SectionHeading(s["YourSpaces"], () => Navigate("Workspaces")));
        if (vm.Workspaces.Count == 0)
            screen.Children.Add(Empty(s["EmptyTitle"], s["EmptyNote"], () => EditWorkspaceAsync(null)));
        else
            screen.Children.Add(WorkspaceCards(vm.Workspaces.OrderByDescending(w => w.IsFavorite).Take(4).ToArray()));
        if (vm.RecentRuns.Count > 0)
        {
            screen.Children.Add(SectionHeading(s["Recent"], () => Navigate("History")));
            foreach (var run in vm.RecentRuns)
                screen.Children.Add(RunCard(run));
        }
    }
    private void Navigate(string section)
    {
        vm.Selected = null;
        vm.Section = section;
        vm.Query = "";
        RenderNavigation();
        RenderScreen();
    }
    private Grid SectionHeading(string title, Action action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        grid.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        grid.Children.Add(Text(title, 19, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        var button = Button(s["All"] + "  →", action);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }
    private void RenderWorkspaces()
    {
        var query = new TextBox { PlaceholderText = s["Search"], Text = vm.Query, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(9) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(query, s["Search"]);
        screen.Children.Add(query);
        searchBox = query;
        var filters = new FlowPanel();
        var tags = new[] { s["AllTags"] }.Concat(vm.Workspaces.SelectMany(w => w.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)).ToArray();
        var tag = new ComboBox { ItemsSource = tags, SelectedIndex = Math.Max(0, Array.FindIndex(tags, t => t == vm.TagFilter)), MinWidth = 150 };
        var sort = new ComboBox { ItemsSource = new[] { s["SortRecent"], s["SortName"], s["SortUsed"] }, SelectedIndex = (int)vm.Sort, MinWidth = 180 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tag, s["AllTags"]);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(sort, s["Sort"]);
        filters.Children.Add(tag);
        filters.Children.Add(sort);
        filters.Children.Add(AsyncButton(s["Setup.Create"], OpenQuickSetupAsync, false, "Workspace.QuickSetup"));
        screen.Children.Add(filters);
        var results = Stack(12);
        screen.Children.Add(results);
        void Refresh()
        {
            results.Children.Clear();
            var spaces = vm.Filtered.ToArray();
            if (spaces.Length > 0)
                results.Children.Add(WorkspaceCards(spaces));
            else if (vm.Query.Length > 0 || vm.TagFilter.Length > 0)
                results.Children.Add(Empty(s["NoResults"], s["NoResultsNote"]));
            else if (vm.Section == "Favorites")
                results.Children.Add(Empty(s["EmptyFavorites"], s["EmptyFavoritesNote"]));
            else
                results.Children.Add(Empty(s["EmptyTitle"], s["EmptyNote"], () => EditWorkspaceAsync(null)));
        }
        query.TextChanged += (_, _) => { vm.Query = query.Text; Refresh(); };
        tag.SelectionChanged += (_, _) => { vm.TagFilter = tag.SelectedIndex <= 0 ? "" : tags[tag.SelectedIndex]; Refresh(); };
        sort.SelectionChanged += (_, _) => { vm.Sort = (WorkspaceSort)Math.Max(0, sort.SelectedIndex); Refresh(); };
        Refresh();
    }
    private Grid WorkspaceCards(IReadOnlyList<Workspace> spaces)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        var columns = cardColumns;
        for (var i = 0; i < columns; i++)
            grid.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        for (var i = 0; i < (spaces.Count + columns - 1) / columns; i++)
            grid.RowDefinitions.Add(new()
            {
                Height = GridLength.Auto
            });
        for (var i = 0; i < spaces.Count; i++)
        {
            var card = WorkspaceCard(spaces[i]);
            Grid.SetRow(card, i / columns);
            Grid.SetColumn(card, i % columns);
            grid.Children.Add(card);
        }
        return grid;
    }
    private Border WorkspaceCard(Workspace workspace)
    {
        var content = Stack(16);
        var top = new Grid();
        top.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        top.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var title = new Grid { ColumnSpacing = 12 };
        title.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        title.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        title.Children.Add(new Border { Background = Brush(palette.Selected), Padding = new Thickness(12), CornerRadius = new CornerRadius(10), Child = Text("↗", 22, color: workspace.Color) });
        var titleText = Text(workspace.Name, 19, weight: Microsoft.UI.Text.FontWeights.SemiBold);
        titleText.MaxLines = 1;
        titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(titleText, 1);
        title.Children.Add(titleText);
        top.Children.Add(title);
        var star = AsyncButton(workspace.IsFavorite ? "★" : "☆", async () => { await vm.SaveAsync(workspace with { IsFavorite = !workspace.IsFavorite }); RenderScreen(); });
        ToolTipService.SetToolTip(star, s[workspace.IsFavorite ? "Unfavorite" : "Favorite"]);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(star, s[workspace.IsFavorite ? "Unfavorite" : "Favorite"]);
        Grid.SetColumn(star, 1);
        top.Children.Add(star);
        content.Children.Add(top);
        var description = Text(string.IsNullOrWhiteSpace(workspace.Description) ? s["EmptyNote"] : workspace.Description, 13, true);
        description.MaxLines = 2;
        description.TextTrimming = TextTrimming.CharacterEllipsis;
        description.MinHeight = 36;
        content.Children.Add(description);
        if (workspace.Tags.Count > 0)
            content.Children.Add(Text(string.Join("   ·   ", workspace.Tags.Take(4)), 11, color: palette.Accent));
        var usage = vm.Usage.Workspaces.FirstOrDefault(u => u.WorkspaceId == workspace.Id);
        content.Children.Add(Text(usage is null ? s["NeverUsed"] : $"{usage.LaunchCount} {s["LaunchCount"]}  ·  {usage.LastLaunchedAt?.ToLocalTime():g}", 11, true));
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        footer.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        footer.Children.Add(Text($"{workspace.Items.Count(i => i.Enabled)} / {workspace.Items.Count} {s["Items"]}", 11, true));
        var actions = new FlowPanel();
        actions.Children.Add(Button("↗", () => OpenWorkspace(workspace)));
        ToolTipService.SetToolTip(actions.Children[0], s["Review"]);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(actions.Children[0], s["Review"]);
        var launch = AsyncButton(s["Launch"], () => StartLaunchAsync(workspace), true);
        launch.IsEnabled = !vm.IsLaunching;
        actions.Children.Add(launch);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        content.Children.Add(footer);
        return Card(content);
    }
    private Border Empty(string title, string note, Func<Task>? action = null)
    {
        var content = Stack(14);
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.Margin = new Thickness(12, 28, 12, 28);
        content.Children.Add(Text("↗", 38, color: palette.Accent));
        content.Children.Add(Text(title, 23, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        content.Children.Add(Text(note, 14, true));
        if (action is not null)
            content.Children.Add(AsyncButton("+  " + s["New"], action, true));
        return Card(content, 24);
    }
    private void RenderDetails(Workspace workspace)
    {
        screen.Children.Add(Button("←  " + s["Back"], () => Navigate("Workspaces")));
        var header = Stack(12);
        header.Children.Add(Text(workspace.Name, 32, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        if (workspace.Description.Length > 0)
            header.Children.Add(Text(workspace.Description, 15, true));
        if (workspace.Tags.Count > 0)
            header.Children.Add(Text(string.Join("   ·   ", workspace.Tags), 12, color: palette.Accent));
        var actions = new FlowPanel();
        var launch = AsyncButton("↗  " + s["Launch"], () => StartLaunchAsync(workspace), true, "Workspace.Launch");
        launch.IsEnabled = !vm.IsLaunching;
        actions.Children.Add(launch);
        actions.Children.Add(AsyncButton(s["Edit"], () => EditWorkspaceAsync(workspace)));
        actions.Children.Add(AsyncButton(s["Doctor.Check"], () => OpenDoctorAsync(workspace), false, "Workspace.Doctor"));
        var more = new DropDownButton { Content = "···", Padding = new Thickness(15, 9, 15, 9) };
        var menu = new MenuFlyout();
        void Menu(string label, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += async (_, _) => await SafeAsync(action);
            menu.Items.Add(item);
        }
        Menu(s[workspace.IsFavorite ? "Unfavorite" : "Favorite"], async () => { await vm.SaveAsync(workspace with { IsFavorite = !workspace.IsFavorite }); RenderScreen(); });
        Menu(s["Duplicate"], async () => { var copy = workspace.Duplicate(workspace.Name.Length > 90 ? workspace.Name[..90] + s["Copy"] : workspace.Name + s["Copy"]); await vm.SaveAsync(copy); OpenWorkspace(copy); });
        Menu(s["Export"], () => ExportAsync(workspace));
        menu.Items.Add(new MenuFlyoutSeparator());
        Menu(s["Delete"], async () => { if (await ConfirmAsync(s["ConfirmDelete"], workspace.Name + "\n\n" + s["DeleteNote"], s["Delete"])) { await vm.DeleteAsync(workspace); Navigate("Workspaces"); } });
        more.Flyout = menu;
        actions.Children.Add(more);
        header.Children.Add(actions);
        screen.Children.Add(header);
        var itemsHeading = new Grid();
        itemsHeading.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        itemsHeading.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        itemsHeading.Children.Add(Text($"{workspace.Items.Count} {s["Items"]}", 19, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        var add = AsyncButton("+  " + s["AddItem"], () => EditItemAsync(workspace, null), false, "Item.New");
        Grid.SetColumn(add, 1);
        itemsHeading.Children.Add(add);
        screen.Children.Add(itemsHeading);
        if (workspace.Items.Count == 0)
            screen.Children.Add(Empty(s["NoItems"], s["NoItemsNote"]));
        var ordered = workspace.Items.OrderBy(i => i.Order).ThenBy(i => i.Id).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            var position = index;
            var content = Stack(10);
            var row = new Grid { ColumnSpacing = 14 };
            row.ColumnDefinitions.Add(new()
            {
                Width = GridLength.Auto
            });
            row.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new()
            {
                Width = GridLength.Auto
            });
            row.Children.Add(Text((index + 1).ToString("00"), 16, true));
            var words = Stack(5);
            words.Children.Add(Text(item.Name, 16, weight: Microsoft.UI.Text.FontWeights.SemiBold));
            var target = Text(item.Target, 12, true);
            target.MaxLines = 2;
            target.TextTrimming = TextTrimming.CharacterEllipsis;
            words.Children.Add(target);
            words.Children.Add(Text($"{s[item.Kind.ToString()]}  ·  {item.DelayMs} ms  ·  {s[item.Enabled ? "Enabled" : "Disabled"]}", 11, color: item.Kind == ItemKind.Command ? "#DEAF72" : palette.Muted));
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            var edit = AsyncButton(s["Edit"], () => EditItemAsync(workspace, item));
            Grid.SetColumn(edit, 2);
            row.Children.Add(edit);
            content.Children.Add(row);
            if (item.Arguments.Length > 0)
                content.Children.Add(Text(s["Arguments"] + ": " + item.Arguments, 12, true));
            if (item.WorkingDirectory.Length > 0)
                content.Children.Add(Text(s["WorkingDirectory"] + ": " + item.WorkingDirectory, 12, true));
            if (item.Note.Length > 0)
                content.Children.Add(Text(item.Note, 12, true));
            var controls = Row(6);
            var up = AsyncButton("↑", () => MoveItemAsync(workspace, item, position - 1));
            up.IsEnabled = index > 0;
            ToolTipService.SetToolTip(up, s["MoveUp"]);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(up, s["MoveUp"]);
            controls.Children.Add(up);
            var down = AsyncButton("↓", () => MoveItemAsync(workspace, item, position + 1));
            down.IsEnabled = index < ordered.Length - 1;
            ToolTipService.SetToolTip(down, s["MoveDown"]);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(down, s["MoveDown"]);
            controls.Children.Add(down);
            controls.Children.Add(AsyncButton(s[item.Enabled ? "Disabled" : "Enabled"], async () => { await vm.SaveAsync(workspace with { Items = workspace.Items.Select(i => i.Id == item.Id ? i with { Enabled = !i.Enabled } : i).ToArray() }); RenderScreen(); }));
            controls.Children.Add(AsyncButton(s["Delete"], async () => { if (await ConfirmAsync(s["DeleteItem"], item.Name, s["Delete"])) { await vm.SaveAsync(workspace with { Items = workspace.Items.Where(i => i.Id != item.Id).ToArray() }); RenderScreen(); } }));
            content.Children.Add(controls);
            var card = Card(content, 18);
            if (!item.Enabled)
                card.Background = Brush(palette.Sidebar);
            screen.Children.Add(card);
        }
        screen.Children.Add(Text(s["DispatchNote"], 12, true));
    }
    private async Task MoveItemAsync(Workspace workspace, WorkspaceItem item, int to)
    {
        var items = workspace.Items.OrderBy(i => i.Order).ThenBy(i => i.Id).ToList();
        items.RemoveAll(i => i.Id == item.Id);
        items.Insert(Math.Clamp(to, 0, items.Count), item);
        await vm.SaveAsync(workspace with
        {
            Items = items.Select((i, index) => i with { Order = index }).ToArray()
        });
        RenderScreen();
    }
    private void RenderHistory()
    {
        var query = new TextBox { Text = vm.HistoryFilter.Query, PlaceholderText = s["HistorySearch"], MinWidth = 240 };
        searchBox = query;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(query, s["HistorySearch"]);
        var onlyFailures = new CheckBox { Content = s["OnlyFailures"], IsChecked = vm.HistoryFilter.OnlyFailures };
        async Task Apply()
        {
            vm.HistoryFilter = vm.HistoryFilter with
            {
                Query = query.Text.Trim(),
                OnlyFailures = onlyFailures.IsChecked == true,
                Offset = 0
            };
            await vm.RefreshHistoryAsync();
            if (vm.Section == "History")
                RenderScreen();
        }
        var filters = new FlowPanel();
        filters.Children.Add(query);
        filters.Children.Add(onlyFailures);
        filters.Children.Add(AsyncButton(s["Apply"], Apply));
        var clear = AsyncButton(s["ClearHistory"], async () =>
        {
            if (vm.IsLaunching)
                throw new ValidationException([s["WaitForLaunch"]]);
            if (!await ConfirmAsync(s["ClearHistory"], s["ClearHistoryNote"], s["ClearHistory"]))
                return;
            await store.ClearHistoryAsync();
            vm.HistoryFilter = vm.HistoryFilter with
            {
                Offset = 0
            };
            await vm.RefreshAsync();
            RenderScreen();
        });
        clear.IsEnabled = !vm.IsLaunching && vm.Usage.TotalRuns > 0;
        filters.Children.Add(clear);
        screen.Children.Add(filters);
        query.KeyDown += async (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; await SafeAsync(Apply); } };
        screen.Children.Add(Text(s["LocalTime"] + "  ·  " + vm.HistoryPage.TotalCount + " " + s["RunsCount"], 12, true));
        if (vm.History.Count == 0)
            screen.Children.Add(Empty(vm.HistoryFilter.Query.Length > 0 || vm.HistoryFilter.OnlyFailures ? s["NoResults"] : s["NoHistory"], s[vm.HistoryFilter.Query.Length>0||vm.HistoryFilter.OnlyFailures?"NoResultsNote":"NoHistoryNote"]));
        else
            foreach (var run in vm.History)
                screen.Children.Add(RunCard(run));
        var pages = new FlowPanel();
        var previous = AsyncButton("← " + s["Previous"], async () => { vm.HistoryFilter = vm.HistoryFilter with { Offset = Math.Max(0, vm.HistoryFilter.Offset - vm.HistoryFilter.PageSize) }; await vm.RefreshHistoryAsync(); RenderScreen(); });
        previous.IsEnabled = vm.HistoryPage.HasPrevious;
        var next = AsyncButton(s["Next"] + " →", async () => { vm.HistoryFilter = vm.HistoryFilter with { Offset = vm.HistoryFilter.Offset + vm.HistoryFilter.PageSize }; await vm.RefreshHistoryAsync(); RenderScreen(); });
        next.IsEnabled = vm.HistoryPage.HasNext;
        pages.Children.Add(previous);
        pages.Children.Add(next);
        screen.Children.Add(pages);
    }
    private Expander RunCard(LaunchRun run)
    {
        var header = Stack(6);
        header.Children.Add(Text(run.WorkspaceName, 16, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        header.Children.Add(Text($"{run.StartedAt.ToLocalTime():g}  ·  {s[run.Status.ToString()]}", 12, true));
        var content = RunItems(run);
        var workspace = vm.Workspaces.FirstOrDefault(w => w.Id == run.WorkspaceId);
        if (workspace is not null)
        {
            var retry = AsyncButton(s["Retry"], () => StartLaunchAsync(workspace));
            retry.IsEnabled = !vm.IsLaunching;
            content.Children.Add(retry);
        }
        return new Expander { Header = header, Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    private StackPanel RunItems(LaunchRun run)
    {
        var list = Stack(12);
        foreach (var item in run.Items)
        {
            var row = Stack(5);
            var color = item.Status switch
            {
                ItemStatus.Failed => vm.Settings.Theme == AppTheme.Light ? "#A12F24" : "#F2A69A",
                ItemStatus.Success => palette.Accent,
                _ => palette.Muted
            };
            row.Children.Add(Text($"{s[item.Status.ToString()]}  ·  {item.Name}", 14, color: color));
            if (item.Error is not null)
            {
                var error = Text(item.Error, 12, true);
                error.IsTextSelectionEnabled = true;
                row.Children.Add(error);
            }
            list.Children.Add(row);
        }
        return list;
    }
    private async Task StartLaunchAsync(Workspace workspace)
    {
        if (vm.IsLaunching)
        {
            vm.Selected = null;
            vm.Section = "Launch";
            RenderNavigation();
            RenderScreen();
            return;
        }
        if (!workspace.Items.Any(i => i.Enabled))
        {
            ShowNotice(s["EmptyLaunch"], error: true);
            return;
        }
        vm.Selected = null;
        vm.Section = "Launch";
        var task = vm.LaunchAsync(workspace, this, run => { if (vm.Section == "Launch") RenderScreen(); });
        launchTask = task;
        RenderNavigation();
        RenderScreen();
        try
        {
            var run = await task;
            if (vm.Settings.Notifications)
                desktop.Notify("SHIFT · " + run.WorkspaceName, s[run.Status.ToString()]);
        }
        finally { launchTask = null; RenderNavigation(); RenderScreen(); }
    }
    private void RenderLaunch()
    {
        screen.Children.Add(Text(s["LaunchQueue"], 30, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        if (vm.ActiveRun is { } run)
        {
            screen.Children.Add(Text(run.WorkspaceName, 22));
            screen.Children.Add(Text(s[run.Status.ToString()], 14, color: palette.Accent));
            var count = run.Items.Count(i => i.Status is ItemStatus.Success or ItemStatus.Failed or ItemStatus.Skipped);
            screen.Children.Add(new ProgressBar { Minimum = 0, Maximum = Math.Max(1, run.Items.Count), Value = count, Height = 5 });
            screen.Children.Add(Card(RunItems(run)));
        }
        else
            screen.Children.Add(new ProgressRing { IsActive = true, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left });
        if (vm.IsLaunching)
        {
            var stop = Button(s["CancelLaunch"], () => vm.LaunchCancellation?.Cancel());
            screen.Children.Add(stop);
        }
        else if (vm.ActiveRun is { } finished && vm.Workspaces.FirstOrDefault(w => w.Id == finished.WorkspaceId) is { } workspace)
            screen.Children.Add(AsyncButton(s["Retry"], () => StartLaunchAsync(workspace), true, "Launch.Retry"));
        screen.Children.Add(Text(s["DispatchNote"], 13, true));
        screen.Children.Add(Button(s["Back"], () => Navigate("Workspaces")));
    }
    private void RenderSettings()
    {
        var appearance = Stack(16);
        appearance.Children.Add(Text(s["Appearance"], 20, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        var theme = new ComboBox { ItemsSource = Enum.GetValues<AppTheme>().Select(t => s[t.ToString()]).ToArray(), SelectedIndex = (int)vm.Settings.Theme, HorizontalAlignment = HorizontalAlignment.Stretch };
        appearance.Children.Add(Field(s["Theme"], theme));
        var language = new ComboBox { ItemsSource = new[] { "Русский", "English" }, SelectedIndex = vm.Settings.Language == "ru" ? 0 : 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        appearance.Children.Add(Field(s["Language"], language));
        screen.Children.Add(Card(appearance));
        var behavior = Stack(18);
        behavior.Children.Add(Text(s["Behavior"], 20, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        var startup = new ToggleSwitch { Header = s["Startup"], IsOn = vm.Settings.StartWithWindows, IsEnabled = !paths.Portable, OnContent = s["Enabled"], OffContent = s["Disabled"] };
        behavior.Children.Add(startup);
        behavior.Children.Add(Text(s[paths.Portable ? "PortableStartup" : "StartupMismatch"], 12, true));
        var tray = new ToggleSwitch { Header = s["Tray"], IsOn = vm.Settings.MinimizeToTray, OnContent = s["Enabled"], OffContent = s["Disabled"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tray, "Settings.MinimizeToTray");
        behavior.Children.Add(tray);
        var notifications = new ToggleSwitch { Header = s["Notifications"], IsOn = vm.Settings.Notifications, OnContent = s["Enabled"], OffContent = s["Disabled"] };
        behavior.Children.Add(notifications);
        var hotkey = new ToggleSwitch { Header = s["Hotkey"], IsOn = vm.Settings.GlobalHotkeyEnabled, OnContent = s["Enabled"], OffContent = s["Disabled"] };
        behavior.Children.Add(hotkey);
        behavior.Children.Add(Text(s["HotkeyHint"], 12, true));
        screen.Children.Add(Card(behavior));
        var data = Stack(16);
        data.Children.Add(Text(s["Data"], 20, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        var path = new TextBox { Text = vm.Settings.BackupDirectory, PlaceholderText = paths.BackupDirectory };
        data.Children.Add(Field(s["BackupPath"], path));
        data.Children.Add(AsyncButton(s["Browse"], async () => { var folder = await PickFolderAsync(); if (folder is not null) path.Text = folder; }));
        var buttons = new FlowPanel();
        buttons.Children.Add(AsyncButton(s["Backup"], async () => { var backup = await store.BackupAsync(BackupDirectory); ShowNotice(s["BackupCreated"], backup); }));
        buttons.Children.Add(Button(s["OpenData"], () => DesktopIntegration.OpenFolder(paths.DataDirectory)));
        buttons.Children.Add(Button(s["OpenLogs"], () => DesktopIntegration.OpenFolder(paths.LogDirectory)));
        var restore = AsyncButton(s["Restore"], RestoreBackupAsync);
        restore.IsEnabled = !vm.IsLaunching;
        buttons.Children.Add(restore);
        data.Children.Add(buttons);
        data.Children.Add(Text(s["Privacy"], 12, true));
        screen.Children.Add(Card(data));
        screen.Children.Add(AsyncButton(s["Save"], async () =>
        {
            var backup = path.Text.Trim();
            if (backup.Length > 0 && !Path.IsPathFullyQualified(backup))
                throw new ValidationException([s["BackupPath"] + ": absolute path required."]);
            if (backup.Length > 0)
                await Task.Run(() => Directory.CreateDirectory(backup));
            var updated = vm.Settings with
            {
                Theme = (AppTheme)Math.Max(0, theme.SelectedIndex),
                Language = language.SelectedIndex == 0 ? "ru" : "en",
                StartWithWindows = !paths.Portable && startup.IsOn,
                MinimizeToTray = tray.IsOn,
                Notifications = notifications.IsOn,
                GlobalHotkeyEnabled = hotkey.IsOn,
                BackupDirectory = backup
            };
            var previousStartup = desktop.IsStartupEnabled();
            try
            {
                windowCommands.ConfigureHotkey(updated.GlobalHotkeyEnabled);
                if (!paths.Portable)
                    desktop.SetStartup(updated.StartWithWindows);
                await store.SaveSettingsAsync(updated);
            }
            catch
            {
                try
                {
                    windowCommands.ConfigureHotkey(vm.Settings.GlobalHotkeyEnabled);
                    if (!paths.Portable)
                        desktop.SetStartup(previousStartup);
                }
                catch (Exception rollbackError) { log.Error("Settings rollback failed", rollbackError); }
                throw;
            }
            vm.Settings = updated;
            s.Language = updated.Language;
            ApplyTheme();
            ConfigureTray();
            BuildShell();
            ShowNotice(s["Saved"]);
        }, true));
        screen.Children.Add(Text(s["About"], 12, true));
    }
    private StackPanel Field(string name, FrameworkElement input)
    {
        var field = Stack(7);
        field.Children.Add(Text(name, 12, true));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, name);
        foreach (var key in new[] { "Name", "Description", "Tags", "Color", "Kind", "Target", "Arguments", "WorkingDirectory", "Delay", "Note", "Theme", "Language", "BackupPath" })
            if (name == s[key])
            {
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(input, "Field." + key);
                break;
            }
        field.Children.Add(input);
        return field;
    }
}
