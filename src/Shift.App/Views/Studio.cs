using Shift.App.Studio;
using Microsoft.UI.Xaml.Automation;
using Windows.Storage.Pickers;

namespace Shift.App;

public sealed partial class MainWindow
{
    private StudioWidgetRuntime? studioRuntime;
    private DesktopProfileService? studioEngine;
    private IStudioStore StudioData => store;
    private IStudioImageLibrary? studioImages;
    private IMonitorTopology? studioTopology;
    private StudioSettings studioSettings = new();
    private IReadOnlyList<DesktopProfile> studioProfiles = [];
    private IReadOnlyList<WidgetDefinition> studioDefinitions = [];
    private IReadOnlyList<StudioRecoveryJournal> studioJournals = [];
    private IReadOnlyList<MonitorIdentity> studioMonitors = [];
    private string studioPage = "Overview";
    private string studioProblem = "";
    private string studioProfileSearch = "";
    private bool studioReady;
    private string T(string ru, string en) => s.Language == "ru" ? ru : en;
    private StackPanel StudioField(string label, FrameworkElement input, string id)
    {
        var panel = Field(label, input);
        AutomationProperties.SetAutomationId(input, id);
        return panel;
    }
    private string KindName(WidgetKind kind) => s.WidgetKindName(kind);
    private async Task InitializeStudioAsync()
    {
        try
        {
            studioImages = new WindowsStudioImageLibrary(Path.Combine(paths.DataDirectory, "Studio", "Images"));
            studioTopology = new WindowsMonitorTopology();
            var runtime = new StudioWidgetRuntime(StudioData, studioTopology, new WindowsSystemMetricsProvider(), log) { Language = s.Language };
            studioRuntime = runtime;
            studioRuntime.EditRequested += id => DispatcherQueue.TryEnqueue(() => _ = SafeAsync(async () =>
            {
                var instance = studioProfiles.SelectMany(p => p.Widgets).FirstOrDefault(w => w.Id == id);
                var definition = studioDefinitions.FirstOrDefault(d => d.Id == instance?.DefinitionId);
                if (definition is not null) await EditStudioWidgetAsync(definition, id);
            }));
            studioRuntime.NotificationRequested += text => DispatcherQueue.TryEnqueue(() => { if (vm.Settings.Notifications) desktop.Notify("SHIFT Studio", text); });
            studioRuntime.RuntimeFaulted += ex => DispatcherQueue.TryEnqueue(() => _ = SafeAsync(async () =>
            {
                if (!ReferenceEquals(studioRuntime, runtime)) return;
                log.Error("Studio runtime fault", ex);
                studioSettings = studioSettings with { Enabled = false, ConsecutiveRuntimeFailures = studioSettings.ConsecutiveRuntimeFailures + 1 };
                await StudioData.SaveStudioSettingsAsync(studioSettings);
                await studioRuntime.StopAsync();
                ShowNotice("SHIFT Studio", T("Виджеты отключены после ошибки. Рабочие пространства доступны.", "Widgets were disabled after an error. Workspaces remain available."), true);
            }));
            studioEngine = new DesktopProfileService(StudioData, new WindowsWallpaperService(), studioImages, studioRuntime, log, new StudioOperationGate());
            await RefreshStudioAsync();
            if (studioSettings.RuntimeWasRunning)
                studioSettings = studioSettings with { ConsecutiveRuntimeFailures = studioSettings.ConsecutiveRuntimeFailures + 1, RuntimeWasRunning = false,
                    Enabled = studioSettings.Enabled && studioSettings.ConsecutiveRuntimeFailures < 2 };
            await StudioData.SaveStudioSettingsAsync(studioSettings);
            studioReady = true;
            if (studioSettings.Enabled && studioSettings.ActiveProfileId is Guid active && studioProfiles.FirstOrDefault(p => p.Id == active && p.Enabled) is { } profile)
            {
                await StudioData.SaveStudioSettingsAsync(studioSettings = studioSettings with { RuntimeWasRunning = true });
                await studioRuntime.ApplyAsync(profile, studioDefinitions); // Startup restores owned windows only, never Windows wallpaper.
            }
            if ((await studioEngine.GetPendingAsync()).Count > 0)
                ShowNotice("SHIFT Studio", T("Обнаружено незавершённое изменение оформления. Откройте Studio → Восстановление. Автоповтора нет.", "An unfinished desktop change was found. Open Studio → Recovery. It was not retried."), true);
        }
        catch (Exception ex) { log.Error("Studio initialization failed", ex); studioProblem = s.ErrorDetails(ex); studioReady = false; }
        finally { if (vm.Section == "Studio") RenderScreen(); }
    }
    private async Task RefreshStudioAsync()
    {
        studioSettings = await StudioData.LoadStudioSettingsAsync();
        studioProfiles = await StudioData.ListProfilesAsync();
        studioDefinitions = await StudioData.ListDefinitionsAsync();
        studioJournals = await StudioData.ListRecoveryJournalsAsync();
        if (studioTopology is not null) studioMonitors = await studioTopology.GetMonitorsAsync();
        if (studioRuntime is not null) studioRuntime.Language = s.Language;
    }
    private async Task StopStudioAsync()
    {
        if (studioRuntime is null) return;
        await studioRuntime.StopAsync(); // Do not close the application over an unsaved note.
        if (studioReady)
            await StudioData.SaveStudioSettingsAsync(studioSettings = studioSettings with { RuntimeWasRunning = false, ConsecutiveRuntimeFailures = 0 });
        await studioRuntime.DisposeAsync();
        studioRuntime = null;
    }
    private async Task SetStudioEnabledAsync(bool enabled)
    {
        if (!studioReady || studioRuntime is null) throw new InvalidOperationException(T("Studio недоступен. Рабочие пространства продолжают работать.", "Studio is unavailable. Workspaces remain available."));
        if (!enabled) await studioRuntime.StopAsync();
        studioSettings = studioSettings with { Enabled = enabled, RuntimeWasRunning = false, ConsecutiveRuntimeFailures = 0 };
        await StudioData.SaveStudioSettingsAsync(studioSettings);
        if (enabled && studioProfiles.FirstOrDefault(p => p.Id == studioSettings.ActiveProfileId && p.Enabled) is { } active)
        {
            await StudioData.SaveStudioSettingsAsync(studioSettings = studioSettings with { RuntimeWasRunning = true });
            await studioRuntime.ApplyAsync(active, studioDefinitions);
        }
        await RefreshStudioAsync(); RenderScreen();
    }
    private void RenderStudio()
    {
        var tabs = new FlowPanel();
        foreach (var (key, ru, en) in new[] { ("Overview", "Обзор", "Overview"), ("Profiles", "Профили", "Profiles"), ("Widgets", "Виджеты", "Widgets"), ("Settings", "Настройки / восстановление", "Settings / recovery") })
        {
            var selected = key;
            tabs.Children.Add(Button(T(ru, en), () => { studioPage = selected; RenderScreen(); }, studioPage == key, "Studio.Tab." + key));
        }
        tabs.Children.Add(StudioAction("↻", T("Обновить профили и экраны", "Refresh profiles and displays"), async () => { await RefreshStudioAsync(); RenderScreen(); }, "Studio.Refresh"));
        screen.Children.Add(tabs);
        var safety = new FlowPanel();
        safety.Children.Add(StudioAction(T("Отключить", "Disable"), T("Отключить Studio", "Disable Studio"), () => SetStudioEnabledAsync(false), "Studio.Disable"));
        safety.Children.Add(StudioAction(T("Восстановить", "Restore"), T("Вернуть оформление Windows", "Restore Windows appearance"), RestoreStudioAsync, "Studio.Restore"));
        safety.Children.Add(StudioAction(T("Собрать", "Gather"), T("Собрать виджеты на основном экране", "Gather widgets on primary display"), async () => { if (studioRuntime is not null) await studioRuntime.GatherAsync(); }, "Studio.Gather"));
        safety.Children.Add(StudioAction(T("Закрепить", "Lock"), s["Studio.DesktopMode"], async () => { if (studioRuntime is not null) await studioRuntime.SetInteractionAsync(WidgetInteraction.Desktop); }, "Studio.DesktopMode"));
        safety.Children.Add(StudioAction(T("Редактировать", "Edit mode"), T("Режим редактирования виджетов", "Edit widget positions and sizes"), async () => { if (studioRuntime is not null) await studioRuntime.SetInteractionAsync(WidgetInteraction.Edit); }, "Studio.EditMode"));
        screen.Children.Add(safety);
        if (!studioReady)
        {
            screen.Children.Add(Text(studioProblem.Length == 0 ? T("Загрузка локальных профилей Studio…", "Loading local Studio profiles…") : T("Studio недоступен. Существующие пространства доступны независимо.", "Studio is unavailable. Existing workspaces remain independent."), 18));
            if (studioProblem.Length > 0) screen.Children.Add(Text(studioProblem, 13, true));
            return;
        }
        if (studioPage == "Profiles") { RenderStudioProfiles(); return; }
        if (studioPage == "Widgets") { RenderStudioGallery(); return; }
        if (studioPage == "Settings") { RenderStudioSettings(); return; }
        var active = studioProfiles.FirstOrDefault(p => p.Id == studioSettings.ActiveProfileId);
        var hero = Stack(10); hero.Children.Add(StudioBadge(studioSettings.Enabled ? T("STUDIO ВКЛЮЧЁН", "STUDIO ENABLED") : T("STUDIO ВЫКЛЮЧЕН", "STUDIO DISABLED")));
        var heading = Text(active?.Name ?? T("Соберите своё окружение", "Build your environment"), 30, weight: Microsoft.UI.Text.FontWeights.SemiBold);
        heading.MaxLines = 2; heading.TextTrimming = TextTrimming.CharacterEllipsis; if (active is not null) ToolTipService.SetToolTip(heading, active.Name); hero.Children.Add(heading);
        hero.Children.Add(Text(T("Виджеты, инструменты и оформление для вашей следующей задачи. Обои меняются только после подтверждения.", "Widgets, tools and appearance for your next task. Wallpaper changes only after confirmation."), 14, true));
        if (active?.WorkspaceId is Guid workspaceId) hero.Children.Add(Text(T("Пространство: ", "Workspace: ") + vm.Workspaces.FirstOrDefault(w => w.Id == workspaceId)?.Name, 14));
        hero.Children.Add(Text($"{T("Окна виджетов", "Widget windows")}: {studioRuntime?.WindowCount ?? 0} · {T("Мониторы", "Monitors")}: {studioMonitors.Count}", 13, true));
        var heroActions = new FlowPanel();
        if (!studioSettings.Enabled) heroActions.Children.Add(AsyncButton(T("Включить Studio", "Enable Studio"), () => SetStudioEnabledAsync(true), true, "Studio.Enable"));
        else if (active?.Enabled == true) heroActions.Children.Add(AsyncButton(T("Применить профиль", "Apply profile"), () => ApplyStudioProfileAsync(active), true, "Studio.Active.Apply"));
        heroActions.Children.Add(AsyncButton(T("Создать профиль", "Create profile"), () => EditStudioProfileAsync(null), false, "Studio.Profile.New"));
        heroActions.Children.Add(Button(T("Все профили", "All profiles"), () => { studioPage = "Profiles"; RenderScreen(); })); hero.Children.Add(heroActions);
        var heroCard = Card(hero, 24); heroCard.Background = Brush(palette.Selected); screen.Children.Add(heroCard);
        RenderStudioStarters();
        screen.Children.Add(Text(T("Собственные окна, без внедрения в Explorer. Режим «Поверх всех окон» выключен по умолчанию. Восстановление проверяет более поздние изменения пользователя.", "Owned windows without Explorer injection. Always on Top is off by default. Recovery checks for later user changes."), 13, true));
        foreach (var monitor in studioMonitors) screen.Children.Add(Text($"{monitor.Name} · {monitor.Width}×{monitor.Height} · {monitor.DpiScale:P0}", 13, true));
    }
    private void RenderStudioProfiles()
    {
        var toolbar = new FlowPanel();
        toolbar.Children.Add(AsyncButton("+ " + T("Создать профиль", "Create profile"), () => EditStudioProfileAsync(null), true, "Studio.Profile.New"));
        toolbar.Children.Add(AsyncButton(T("Импорт профиля", "Import profile"), ImportStudioProfileAsync, false, "Studio.Profile.Import"));
        screen.Children.Add(toolbar);
        if (studioProfiles.Count == 0) screen.Children.Add(Text(T("Создайте первый профиль. Сохранение не изменяет Windows.", "Create your first profile. Saving does not change Windows."), 17, true));
        var search = new TextBox { Text = studioProfileSearch, PlaceholderText = T("Найти профиль или пространство", "Find a profile or workspace"), MaxLength = 200 };
        AutomationProperties.SetAutomationId(search, "Studio.Profile.Search"); AutomationProperties.SetName(search, search.PlaceholderText);
        screen.Children.Add(search);
        var results = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch }; screen.Children.Add(results);
        void RefreshProfiles()
        {
        var cards = new List<FrameworkElement>();
        var terms = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var profile in studioProfiles.Where(p => terms.All(term => p.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || p.Description.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || (vm.Workspaces.FirstOrDefault(w => w.Id == p.WorkspaceId)?.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)))
            .OrderByDescending(p => p.Id == studioSettings.ActiveProfileId))
        {
            var body = Stack(12); body.Children.Add(Text(profile.Name, 22));
            body.Children.Add(Text(profile.Description, 13, true));
            if (profile.Id == studioSettings.ActiveProfileId) body.Children.Add(StudioBadge(T("Активное окружение", "Active environment")));
            if (profile.WorkspaceId is Guid linked) body.Children.Add(Text(T("Пространство: ", "Workspace: ") + (vm.Workspaces.FirstOrDefault(w => w.Id == linked)?.Name ?? "—"), 13, true));
            body.Children.Add(Text($"{T("Обои", "Wallpapers")}: {profile.Wallpapers.Count} · {T("Виджеты", "Widgets")}: {profile.Widgets.Count} · {T("Последнее применение", "Last applied")}: {s.FormatDate(profile.LastAppliedAt)}", 12, true));
            foreach (var wallpaper in profile.Wallpapers.Take(1))
                if (File.Exists(wallpaper.ImagePath)) body.Children.Add(new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(wallpaper.ImagePath)), Height = 120, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Stretch });
            var actions = new FlowPanel();
            actions.Children.Add(AsyncButton(T("Применить профиль", "Apply profile"), () => ApplyStudioProfileAsync(profile), true, "Studio.Profile.Apply." + profile.Id));
            actions.Children.Add(AsyncButton(s["Edit"], () => EditStudioProfileAsync(profile), false, "Studio.Profile.Edit." + profile.Id));
            actions.Children.Add(AsyncButton(s["Studio.Arrange"], () => ArrangeStudioProfileAsync(profile), false, "Studio.Profile.Arrange." + profile.Id));
            var export = AsyncButton(s["Export"], () => ExportStudioProfileAsync(profile), false, "Studio.Profile.Export." + profile.Id);
            export.IsEnabled = profile.Widgets.Count > 0;
            ToolTipService.SetToolTip(export, T("Перенести оформление и виджеты без текущих заметок и обоев", "Transfer appearance and widgets without current notes or wallpaper"));
            actions.Children.Add(export);
            if (profile.Widgets.Any(w => studioDefinitions.Any(d => d.Id == w.DefinitionId && d.Kind == WidgetKind.Notes)))
                actions.Children.Add(AsyncButton(T("Сохранить заметки", "Export notes"), () => ExportStudioNotesAsync(profile), false, "Studio.Profile.Notes." + profile.Id));
            actions.Children.Add(AsyncButton(s["Duplicate"], async () => { await StudioData.SaveProfileAsync(profile.Duplicate(profile.Name[..Math.Min(profile.Name.Length, 85)] + T(" — копия", " — copy"))); await RefreshStudioAsync(); RenderScreen(); }));
            actions.Children.Add(AsyncButton(s["Delete"], async () =>
            {
                if (!await ConfirmAsync(s["Delete"], T("Удалить профиль и данные его экземпляров (заметки, таймеры)? Сначала сохраните нужные заметки. Текущие обои и журнал восстановления сохранятся.", "Delete this profile and its instance data (notes, timers)? Export needed notes first. Current wallpaper and recovery history stay."), s["Delete"])) return;
                if (profile.Id == studioSettings.ActiveProfileId)
                {
                    await studioRuntime!.StopAsync();
                    await StudioData.SaveStudioSettingsAsync(studioSettings = studioSettings with { ActiveProfileId = null, RuntimeWasRunning = false });
                }
                await StudioData.DeleteProfileAsync(profile.Id); await RefreshStudioAsync(); RenderScreen();
            }));
            body.Children.Add(actions); cards.Add(Card(body));
        }
        results.Content = cards.Count == 0 && studioProfiles.Count > 0 ? Empty(s["NoResults"], s["NoResultsNote"]) : StudioCards(cards);
        }
        search.TextChanged += (_, _) => { studioProfileSearch = search.Text; RefreshProfiles(); }; RefreshProfiles();
    }
    private async Task EditStudioProfileAsync(DesktopProfile? existing)
    {
        if (studioRuntime is not null) await studioRuntime.FlushPendingAsync();
        await RefreshStudioAsync();
        if (existing is not null) existing = studioProfiles.FirstOrDefault(p => p.Id == existing.Id) ?? throw new InvalidOperationException("Desktop profile was not found.");
        var source = existing ?? new DesktopProfile { Name = T("Новое окружение", "New environment") };
        var name = new TextBox { Text = source.Name, MaxLength = 100 };
        var description = new TextBox { Text = source.Description, MaxLength = 2000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var workspace = new ComboBox { ItemsSource = new[] { T("Не привязан", "Not linked") }.Concat(vm.Workspaces.Select(w => w.Name)).ToArray(), SelectedIndex = source.WorkspaceId is Guid id ? Math.Max(0, vm.Workspaces.ToList().FindIndex(w => w.Id == id) + 1) : 0 };
        var automatic = new CheckBox { Content = T("Автоприменение при запуске (профили без обоев)", "Apply on dispatch (profiles without wallpaper)"), IsChecked = source.ApplyOnWorkspaceLaunch };
        var enabled = new CheckBox { Content = T("Профиль включён", "Profile enabled"), IsChecked = source.Enabled };
        var changePosition = new CheckBox { Content = T("Изменить общий режим размещения обоев", "Change the global wallpaper positioning mode"), IsChecked = source.ChangeWallpaperPosition };
        var position = new ComboBox { ItemsSource = new[] { T("По центру", "Center"), T("Плитка", "Tile"), T("Растянуть", "Stretch"), T("Вписать", "Fit"), T("Заполнить", "Fill"), T("Растянуть на экраны", "Span") }, SelectedIndex = (int)source.WallpaperPosition };
        var fields = Stack(14);
        fields.Children.Add(Text(T("Сохранение не меняет Windows. Режим размещения общий для всех экранов; слайд-шоу не изменяется.", "Saving does not change Windows. Position mode is global across displays; slideshow is not changed."), 13, true));
        fields.Children.Add(StudioField(s["Name"], name, "Studio.Profile.Name")); fields.Children.Add(StudioField(s["Description"], description, "Studio.Profile.Description"));
        fields.Children.Add(StudioField(T("Пространство", "Workspace"), workspace, "Studio.Profile.Workspace")); fields.Children.Add(automatic); fields.Children.Add(enabled);
        var wallpapers = source.Wallpapers.ToDictionary(w => w.MonitorId, w => w.ImagePath, StringComparer.OrdinalIgnoreCase);
        var wallpaperRows = Stack(10);
        foreach (var monitor in studioMonitors)
        {
            var row = Stack(6); row.Children.Add(Text(monitor.Name, 14));
            var path = new TextBlock { Text = wallpapers.GetValueOrDefault(monitor.DeviceId) ?? T("Без изменения", "Leave unchanged"), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            var actions = new FlowPanel();
            actions.Children.Add(AsyncButton(T("Выбрать обои", "Choose wallpaper"), async () =>
            {
                var picker = new FileOpenPicker(); foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp" }) picker.FileTypeFilter.Add(extension);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
                var file = await picker.PickSingleFileAsync(); if (file is null) return;
                wallpapers[monitor.DeviceId] = await studioImages!.ImportAsync(file.Path); path.Text = wallpapers[monitor.DeviceId];
            }, false, "Studio.Wallpaper.Choose." + monitor.DeviceId));
            actions.Children.Add(Button(T("Ко всем мониторам", "Use on all monitors"), () =>
            {
                if (!wallpapers.TryGetValue(monitor.DeviceId, out var image)) return;
                foreach (var display in studioMonitors) wallpapers[display.DeviceId] = image;
                path.Text = T("Выбрано для всех мониторов", "Selected for all monitors");
            }));
            actions.Children.Add(Button(T("Не менять", "Leave unchanged"), () => { wallpapers.Remove(monitor.DeviceId); path.Text = T("Без изменения", "Leave unchanged"); }));
            row.Children.Add(path); row.Children.Add(actions); wallpaperRows.Children.Add(Card(row, 12));
        }
        fields.Children.Add(wallpaperRows); fields.Children.Add(changePosition); fields.Children.Add(position);
        fields.Children.Add(Text(T("Виджеты профиля", "Profile widgets"), 18));
        fields.Children.Add(Text(s["Studio.Profile.RemoveNote"], 12, true));
        var choices = new Dictionary<Guid, CheckBox>();
        var visibility = new Dictionary<Guid, CheckBox>();
        foreach (var definition in studioDefinitions)
        {
            var check = new CheckBox { Content = definition.Title, IsChecked = source.Widgets.Any(w => w.DefinitionId == definition.Id) };
            choices[definition.Id] = check; fields.Children.Add(check);
            foreach (var instance in source.Widgets.Where(w => w.DefinitionId == definition.Id))
            {
                var visible = new CheckBox { Content = T("Показывать", "Show") + " · " + definition.Title + " · " + instance.Id.ToString("N")[..6], IsChecked = instance.Visible, Margin = new Thickness(20, 0, 0, 0) };
                visibility[instance.Id] = visible; fields.Children.Add(visible);
            }
        }
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false }; fields.Children.Add(error);
        var form = new ContentControl { Content = fields, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var dialog = Dialog(T("Визуальный профиль", "Desktop profile"), new ScrollViewer { Content = form, MaxHeight = 510 }, s["Save"], s["Cancel"]);
        var busy = false; dialog.Closing += (_, args) => { if (busy) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            busy = true; form.IsEnabled = false; var deferral = args.GetDeferral(); dialog.IsPrimaryButtonEnabled = false;
            try
            {
                var linked = workspace.SelectedIndex > 0 ? vm.Workspaces[workspace.SelectedIndex - 1].Id : (Guid?)null;
                var candidate = source with { Name = name.Text.Trim(), Description = description.Text, WorkspaceId = linked,
                    ApplyOnWorkspaceLaunch = linked is not null && automatic.IsChecked == true, Enabled = enabled.IsChecked == true,
                    ChangeWallpaperPosition = changePosition.IsChecked == true, WallpaperPosition = (WallpaperPlacement)Math.Max(0, position.SelectedIndex),
                    Wallpapers = wallpapers.Select(pair => new WallpaperAssignment(pair.Key, pair.Value)).ToArray(),
                    Widgets = choices.Where(pair => pair.Value.IsChecked == true).SelectMany(pair =>
                    {
                        var retained = source.Widgets.Where(w => w.DefinitionId == pair.Key).ToArray();
                        return retained.Length == 0 ? new[] { new WidgetInstance { DefinitionId = pair.Key } } : retained.Select(w => w with { Visible = visibility[w.Id].IsChecked == true }).ToArray();
                    }).ToArray() };
                await StudioData.SaveProfileAsync(candidate);
                if (!candidate.Enabled && studioSettings.ActiveProfileId == candidate.Id)
                {
                    await studioRuntime!.StopAsync();
                    await StudioData.SaveStudioSettingsAsync(studioSettings = studioSettings with { RuntimeWasRunning = false });
                }
                await RefreshStudioAsync();
            }
            catch (Exception ex) { args.Cancel = true; error.Title = T("Профиль не сохранён", "Profile was not saved"); error.Message = s.ErrorDetails(ex); error.IsOpen = true; DispatcherQueue.TryEnqueue(() => error.StartBringIntoView()); }
            finally { busy = false; form.IsEnabled = true; dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        await ShowDialogAsync(dialog); RenderScreen();
    }
    private async Task ApplyStudioProfileAsync(DesktopProfile profile)
    {
        if (studioEngine is null) return;
        if (!studioSettings.Enabled) { ShowNotice("SHIFT Studio", T("Сначала включите Studio. Применение не выполнено.", "Enable Studio first. Nothing was applied."), true); return; }
        var confirmedWallpaper = false;
        if (profile.Wallpapers.Count > 0 || profile.ChangeWallpaperPosition)
        {
            if (!await ConfirmAsync("SHIFT Studio", T("Подтвердите, что сейчас используются статичные обои, а не Windows Spotlight или слайд-шоу. SHIFT сохраняет пути изображений и режим, но не настройки провайдеров. При Spotlight отмените действие. Команды пространства этим не разрешаются.", "Confirm that Windows currently uses static images, not Spotlight or a slideshow. SHIFT saves image paths and placement, not provider configuration. Cancel if Spotlight is active. This does not approve workspace commands."), T("Разрешить изменение обоев", "Allow wallpaper changes"))) return;
            confirmedWallpaper = true;
            await StudioData.SaveStudioSettingsAsync(studioSettings = studioSettings with { WallpaperConsentGranted = true });
        }
        var result = await studioEngine.ApplyAsync(profile.Id, confirmedWallpaper);
        await RefreshStudioAsync();
        await StudioData.SaveStudioSettingsAsync(studioSettings = studioSettings with { RuntimeWasRunning = studioSettings.Enabled && studioRuntime!.WindowCount > 0 });
        ShowNotice("SHIFT Studio · " + ProfileStatus(result.Status), string.Join("\n", result.Errors.Select(s.LocalizeMessage)), result.Status != StudioApplyStatus.Applied);
        RenderScreen();
    }
    private string ProfileStatus(StudioApplyStatus status) => status switch
    {
        StudioApplyStatus.Applied => T("Профиль применён", "Profile applied"), StudioApplyStatus.PartiallyApplied => T("Профиль применён частично", "Profile partially applied"),
        StudioApplyStatus.Restored => T("Оформление восстановлено", "Appearance restored"), StudioApplyStatus.Kept => T("Текущее оформление сохранено", "Current appearance kept"),
        StudioApplyStatus.Preparing => T("Подготовка", "Preparing"), StudioApplyStatus.Applying => T("Применение", "Applying"), StudioApplyStatus.Interrupted => T("Прервано", "Interrupted"), _ => T("Ошибка профиля", "Profile failed")
    };
    private async Task RestoreStudioAsync()
    {
        if (studioEngine is null) return;
        await RefreshStudioAsync();
        var journal = studioJournals.OrderByDescending(j => j.CreatedAt).FirstOrDefault(j => j.Status is not (StudioApplyStatus.Restored or StudioApplyStatus.Kept));
        if (journal is null) { ShowNotice("SHIFT Studio", T("Нет изменений для восстановления.", "No changes to restore.")); return; }
        var result = await studioEngine.RestoreAsync(journal.Id);
        if (result.Conflicts.Count > 0 && await ConfirmAsync(T("Обои изменены после SHIFT", "Wallpaper changed after SHIFT"),
            T("Обнаружены более поздние изменения. Продолжайте только при статичных обоях; при Spotlight или слайд-шоу отмените действие. Перезаписать изменения исходным оформлением?\n", "Later changes were detected. Continue only with static wallpapers; cancel for Spotlight or a slideshow. Overwrite the changes with the original appearance?\n") + string.Join("\n", result.Conflicts.Select(s.LocalizeMessage)), T("Перезаписать явно", "Explicitly overwrite")))
            result = await studioEngine.RestoreAsync(journal.Id, overwriteConflicts: true);
        await RefreshStudioAsync(); RenderScreen();
        ShowNotice("SHIFT Studio", result.Restored ? T("Исходное оформление восстановлено.", "Original appearance restored.") : string.Join("\n", result.Errors.Concat(result.Conflicts).Select(s.LocalizeMessage)), !result.Restored);
    }
    private void RenderStudioSettings()
    {
        screen.Children.Add(Text(T("Безопасность и восстановление", "Safety and recovery"), 24));
        screen.Children.Add(AsyncButton(studioSettings.Enabled ? T("Выключить Studio", "Turn Studio off") : T("Включить Studio", "Enable Studio"), () => SetStudioEnabledAsync(!studioSettings.Enabled), true, "Studio.Toggle"));
        screen.Children.Add(Text(T("Отключение закрывает виджеты и запрещает новые изменения. Текущие обои восстанавливаются отдельным действием. После сбоя незавершённая операция не повторяется.", "Disabling closes widgets and prevents new changes. Wallpaper restoration is a separate action. Interrupted operations are never replayed."), 14, true));
        screen.Children.Add(Text(T("Acrylic и Mica используют явно обозначенный резервный вариант оформления в этой версии. Solid/Translucent — собственные окна; прозрачность всего окна проверяется отдельно.", "Acrylic and Mica use an explicit fallback in this version. Solid/Translucent are owned windows; whole-window opacity is validated separately."), 13, true));
        foreach (var journal in studioJournals.OrderByDescending(j => j.CreatedAt).Take(12))
        {
            var body = Stack(8); body.Children.Add(Text($"{s.FormatDate(journal.CreatedAt)} · {ProfileStatus(journal.Status)}", 15));
            foreach (var error in journal.Errors) body.Children.Add(Text(s.LocalizeMessage(error), 12, true));
            if (journal.Status is not (StudioApplyStatus.Restored or StudioApplyStatus.Kept))
                body.Children.Add(AsyncButton(T("Оставить текущее оформление", "Keep current appearance"), async () =>
                {
                    if (await ConfirmAsync("SHIFT Studio", T("Завершить этот журнал без отката? Windows не будет изменена.", "Complete this journal without rollback? Windows will not be changed."), T("Оставить", "Keep")))
                    { await studioEngine!.KeepCurrentAsync(journal.Id); await RefreshStudioAsync(); RenderScreen(); }
                }));
            screen.Children.Add(Card(body, 16));
        }
    }
    private string studioWidgetSearch = "";
    private int studioWidgetKindFilter;
    private void RenderStudioGallery()
    {
        var actions = new FlowPanel();
        actions.Children.Add(AsyncButton("+ " + s["Studio.Lab"], () => EditStudioWidgetAsync(null), true, "Studio.Widget.New"));
        actions.Children.Add(AsyncButton(T("Импорт виджета JSON", "Import widget JSON"), ImportStudioWidgetAsync, false, "Studio.Widget.Import"));
        screen.Children.Add(actions);
        screen.Children.Add(Text(T("Выберите тип и сделайте его своим", "Choose a widget and make it yours"), 25, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        var kinds = new FlowPanel();
        foreach (var kind in Enum.GetValues<WidgetKind>())
        {
            var selected = kind;
            kinds.Children.Add(AsyncButton("+ " + KindName(kind), () => EditStudioWidgetAsync(new WidgetDefinition { Title = KindName(selected), Kind = selected }), false, "Studio.Widget.Create." + kind));
        }
        screen.Children.Add(kinds);
        var search = new TextBox { PlaceholderText = s["Studio.SearchWidgets"], Text = studioWidgetSearch, MaxLength = 200 };
        AutomationProperties.SetAutomationId(search, "Studio.Widget.Search"); AutomationProperties.SetName(search, s["Studio.SearchWidgets"]);
        var filter = new ComboBox { ItemsSource = new[] { s["Studio.AllKinds"] }.Concat(Enum.GetValues<WidgetKind>().Select(KindName)).ToArray(), SelectedIndex = studioWidgetKindFilter, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(filter, "Studio.Widget.Filter"); AutomationProperties.SetName(filter, T("Тип виджета", "Widget type"));
        var filters = new Grid { ColumnSpacing = 12 }; filters.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        filters.Children.Add(search); Grid.SetColumn(filter, 1); filters.Children.Add(filter); screen.Children.Add(filters);
        var list = Stack(16); screen.Children.Add(list);
        void UpdateCards()
        {
            studioWidgetSearch = search.Text; studioWidgetKindFilter = Math.Max(0, filter.SelectedIndex); list.Children.Clear();
            var definitions = studioDefinitions.Where(d => (studioWidgetKindFilter == 0 || (int)d.Kind == studioWidgetKindFilter - 1)
                && (d.Title.Contains(studioWidgetSearch, StringComparison.OrdinalIgnoreCase) || KindName(d.Kind).Contains(studioWidgetSearch, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (definitions.Length == 0)
            {
                list.Children.Add(Empty(studioDefinitions.Count == 0 ? s["Studio.DefinitionsEmpty"] : s["NoResults"], studioDefinitions.Count == 0 ? s["Studio.DefinitionsEmpty.Note"] : s["NoResultsNote"])); return;
            }
            list.Children.Add(StudioCards(definitions.Select(definition =>
            {
                var body = Stack(12); body.Children.Add(StudioDefinitionPreview(definition));
                body.Children.Add(Text(definition.Title, 21, weight: Microsoft.UI.Text.FontWeights.SemiBold));
                body.Children.Add(Text(KindName(definition.Kind) + " · " + s.TemplateName(definition.Style.TemplateId), 13, true));
                var used = studioProfiles.Count(p => p.Widgets.Any(w => w.DefinitionId == definition.Id));
                body.Children.Add(Text(T("Профилей: ", "Profiles: ") + used.ToString(s.Culture), 12, true));
                var buttons = new FlowPanel();
                buttons.Children.Add(AsyncButton(s["Edit"], () => EditStudioWidgetAsync(definition), false, "Studio.Widget.Edit." + definition.Id));
                buttons.Children.Add(AsyncButton(s["Duplicate"], async () => { await StudioData.SaveDefinitionAsync(definition with { Id = Guid.NewGuid(), Revision = 0, Title = definition.Title[..Math.Min(85, definition.Title.Length)] + s["Copy"] }); await RefreshStudioAsync(); RenderScreen(); }));
                buttons.Children.Add(AsyncButton(s["Export"], () => ExportStudioWidgetAsync(definition)));
                buttons.Children.Add(AsyncButton(s["Delete"], async () => { if (await ConfirmAsync(s["Delete"], T("Удалить определение? Сначала уберите его из профилей.", "Delete this definition? Remove it from profiles first."), s["Delete"])) { await StudioData.DeleteDefinitionAsync(definition.Id); await RefreshStudioAsync(); RenderScreen(); } }));
                body.Children.Add(buttons); return (FrameworkElement)Card(body);
            })));
        }
        search.TextChanged += (_, _) => UpdateCards(); filter.SelectionChanged += (_, _) => UpdateCards(); UpdateCards();
    }
    private async Task ImportStudioWidgetAsync()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json"); WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        var file = await picker.PickSingleFileAsync(); if (file is null) return;
        var properties = await file.GetBasicPropertiesAsync(); if (properties.Size > 262144) throw new InvalidDataException(T("Лимит файла — 256 КиБ.", "File limit is 256 KiB."));
        byte[] bytes;
        await using (var input = File.OpenRead(file.Path))
        {
            var bounded = new byte[262145]; var count = 0;
            while (count < bounded.Length)
            {
                var read = await input.ReadAsync(bounded.AsMemory(count)); if (read == 0) break; count += read;
            }
            if (count > 262144) throw new InvalidDataException(T("Лимит файла — 256 КиБ.", "File limit is256 KiB."));
            bytes = bounded[..count];
        }
        var definition = WidgetTransfer.Import(bytes);
        await StudioData.SaveDefinitionAsync(definition); await RefreshStudioAsync(); RenderScreen();
    }
    private async Task ExportStudioWidgetAsync(WidgetDefinition definition)
    {
        var picker = new FileSavePicker { SuggestedFileName = "SHIFT-widget" }; picker.FileTypeChoices.Add("SHIFT widget", new[] { ".json" }); WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        var temporary = file.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, WidgetTransfer.Export(definition)); File.Move(temporary, file.Path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
