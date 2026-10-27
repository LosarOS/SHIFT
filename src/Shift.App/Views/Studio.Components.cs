using Microsoft.UI.Xaml.Automation;
namespace Shift.App;

public sealed partial class MainWindow
{
    private Grid StudioCards(IEnumerable<FrameworkElement> cards)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        for (var c = 0; c < cardColumns; c++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var index = 0;
        foreach (var card in cards)
        {
            if (index % cardColumns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(card, index / cardColumns); Grid.SetColumn(card, index % cardColumns); grid.Children.Add(card); index++;
        }
        return grid;
    }
    private Border StudioBadge(string text)
    {
        return new Border { Child = Text(text, 12, color: vm.Settings.Theme == AppTheme.Light ? palette.Text : palette.Accent), Background = Brush(palette.Selected), Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(6) };
    }
    private Button StudioAction(string label, string help, Func<Task> action, string id)
    {
        var button = AsyncButton(label, action, false, id); ToolTipService.SetToolTip(button, help); AutomationProperties.SetName(button, help); return button;
    }
    private static string StudioKindGlyph(WidgetKind kind) => kind switch
    { WidgetKind.Clock => "\uE823", WidgetKind.SystemMonitor => "\uE9D9", WidgetKind.FocusTimer => "\uE916", _ => "\uE70B" };
    private void RenderStudioStarters()
    {
        screen.Children.Add(Text(s["Studio.Starter"], 23, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        screen.Children.Add(Text(s["Studio.Starter.Note"], 14, true));
        screen.Children.Add(StudioCards(StudioPresetCatalog.All.Select(recipe =>
        {
            var body = Stack(12); var title = Row(10);
            title.Children.Add(Glyph(recipe.Id == "Development" ? "\uE943" : recipe.Id == "Streaming" ? "\uE714" : "\uE916", 24));
            title.Children.Add(Text(s[recipe.NameKey], 21, weight: Microsoft.UI.Text.FontWeights.SemiBold)); body.Children.Add(title);
            body.Children.Add(Text(s[recipe.DescriptionKey], 14, true));
            body.Children.Add(Text(string.Join(" · ", recipe.WidgetKinds.Select(KindName)), 12, color: palette.Accent));
            body.Children.Add(AsyncButton(s["Studio.CreatePreset"], () => CreateStudioPresetAsync(recipe), false, "Studio.Preset.Create." + recipe.Id));
            return (FrameworkElement)Card(body);
        })));
    }
    private async Task CreateStudioPresetAsync(StudioPresetDescriptor recipe)
    {
        var name = new TextBox { Text = s[recipe.NameKey], MaxLength = 100 };
        var style = new ComboBox { ItemsSource = StudioTemplates.All.Select(t => s.TemplateName(t.TemplateId)).ToArray(), SelectedIndex = StudioTemplates.All.ToList().FindIndex(t => t.TemplateId == recipe.StyleTemplateId) };
        var monitor = new ComboBox { ItemsSource = new[] { T("Основной экран", "Primary display") }.Concat(studioMonitors.Select(m => m.Name)).ToArray(), SelectedIndex = 0 };
        var fields = Stack(14); fields.Children.Add(Text(s[recipe.DescriptionKey], 15, true));
        fields.Children.Add(StudioField(s["Name"], name, "Studio.Preset.Name"));
        fields.Children.Add(StudioField(T("Оформление", "Style"), style, "Studio.Preset.Style"));
        fields.Children.Add(StudioField(T("Экран", "Display"), monitor, "Studio.Preset.Monitor"));
        foreach (var kind in recipe.WidgetKinds)
        { var row = Row(12); row.Children.Add(Glyph(StudioKindGlyph(kind), 21)); row.Children.Add(Text(KindName(kind), 15)); fields.Children.Add(row); }
        fields.Children.Add(Text(s["Studio.Preset.Scope"], 13, true));
        var error = new InfoBar { IsClosable = false, Severity = InfoBarSeverity.Error }; fields.Children.Add(error);
        var form = new ContentControl { Content = fields, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var dialog = Dialog(s["Studio.CreatePreset"], new ScrollViewer { Content = form, MaxHeight = 480 }, s["Save"], s["Cancel"]);
        var saving = false; dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
        DesktopProfile? created = null; IReadOnlyList<string> warnings = [];
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral(); saving = true; form.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
            try
            {
                var titles = Enum.GetValues<WidgetKind>().ToDictionary(k => k, KindName);
                var draft = StudioPresetCatalog.CreateDraft(recipe.Id, name.Text.Trim(), titles, studioMonitors,
                    monitor.SelectedIndex > 0 ? studioMonitors[monitor.SelectedIndex - 1].DeviceId : null,
                    StudioTemplates.All[Math.Max(0, style.SelectedIndex)].TemplateId);
                draft = draft with { Profile = draft.Profile with { Description = s[recipe.DescriptionKey] } };
                if (StudioData is not IStudioPresetStore presets) throw new NotSupportedException("Atomic Studio preset storage is unavailable.");
                created = await presets.SavePresetAsync(draft); warnings = draft.Warnings;
            }
            catch (Exception ex) { args.Cancel = true; error.Title = T("Профиль не создан", "Profile was not created"); error.Message = s.ErrorDetails(ex); error.IsOpen = true; }
            finally { saving = false; form.IsEnabled = true; dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        await ShowDialogAsync(dialog);
        if (created is not null)
        {
            await RefreshStudioAsync(); studioPage = "Profiles"; RenderScreen();
            ShowNotice(s["Studio.Preset.Saved"], string.Join("\n", warnings.Select(key => s[key])));
        }
    }
    private async Task ArrangeStudioProfileAsync(DesktopProfile profile)
    {
        var active = studioSettings.Enabled && studioSettings.ActiveProfileId == profile.Id;
        var message = T("Видимые виджеты будут разложены по сетке на своих доступных экранах. Отключённый экран заменится основным. Скрытые виджеты сохранят своё расположение.", "Visible widgets will be arranged on their available displays. Missing displays fall back to primary. Hidden widgets keep their layout.");
        message += active ? T("\nАктивные окна будут сохранены и перезапущены; обои не изменятся.", "\nActive windows will be saved and restarted; wallpaper stays unchanged.") : "\n" + s["Studio.Arrange.Note"];
        if (!await ConfirmAsync(s["Studio.Arrange"], message, s["Save"])) return;
        // Flush old HWND geometry BEFORE writing the new layout, so a later host flush cannot overwrite the grid.
        if (active) await SetStudioEnabledAsync(false);
        var latest = (await StudioData.ListProfilesAsync()).FirstOrDefault(p => p.Id == profile.Id);
        if (latest is null) return;
        var layouts = new Dictionary<Guid, WidgetLayout>(); var warnings = new List<string>();
        foreach (var group in latest.Widgets.Where(w => w.Visible).GroupBy(w => w.Layout.MonitorId))
        {
            var visible = group.ToArray(); var arranged = StudioPresetLayout.Arrange(visible.Length, studioMonitors, group.Key); warnings.AddRange(arranged.Warnings);
            for (var i = 0; i < visible.Length; i++) layouts[visible[i].Id] = arranged.Layouts[i] with { AlwaysOnTop = visible[i].Layout.AlwaysOnTop };
        }
        await StudioData.SaveProfileAsync(latest with { Widgets = latest.Widgets.Select(w => layouts.TryGetValue(w.Id, out var layout) ? w with { Layout = layout } : w).ToArray() });
        await RefreshStudioAsync();
        if (active) await SetStudioEnabledAsync(true); else RenderScreen();
        ShowNotice(s["Studio.Arrange"], warnings.Count > 0 ? string.Join("\n", warnings.Distinct().Select(key => s[key])) : T("Расположение сохранено.", "Layout saved."));
    }
    private void RenderWorkspaceStudioActions(Workspace workspace)
    {
        var profiles = studioProfiles.Where(p => p.WorkspaceId == workspace.Id && p.Enabled).ToArray();
        if (profiles.Length == 0) return;
        var body = Stack(10); body.Children.Add(Text(T("Окружение пространства", "Workspace environment"), 19));
        body.Children.Add(Text(T("Оформление применяется отдельно от приложений. Команды сохраняют своё подтверждение.", "Appearance applies separately from apps. Commands keep their own consent."), 13, true));
        var actions = new FlowPanel();
        foreach (var profile in profiles)
            actions.Children.Add(AsyncButton(profile.Name + " · " + T("применить", "apply"), () => ApplyStudioProfileAsync(profile), false, "Workspace.Studio.Apply." + profile.Id));
        body.Children.Add(actions); screen.Children.Add(Card(body, 18));
    }
    private Border StudioDefinitionPreview(WidgetDefinition definition)
    {
        var preview = Stack(8); preview.Children.Add(Text(T("ПРЕДПРОСМОТР ОФОРМЛЕНИЯ", "STYLE PREVIEW"), 10, color: accessibility.HighContrast ? palette.Text : definition.Style.Foreground));
        var line = new Grid { ColumnSpacing = 12 }; line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); line.Children.Add(Glyph(StudioKindGlyph(definition.Kind), 28));
        var text = definition.Kind switch
        {
            WidgetKind.Clock => DateTime.Now.ToString(definition.Content.Use24Hour ? "HH:mm" : "h:mm tt", s.Culture),
            WidgetKind.FocusTimer => definition.Content.FocusMinutes.ToString(s.Culture) + ":00",
            WidgetKind.Notes => definition.Content.InitialNotes.Length > 0 ? definition.Content.InitialNotes[..Math.Min(80, definition.Content.InitialNotes.Length)] : T("Ваши мысли. Только локально.", "Your thoughts. Kept local."),
            _ => T("ЦП  /  ОЗУ", "CPU  /  RAM")
        };
        var words = Text(text, 24, color: accessibility.HighContrast ? palette.Text : definition.Style.Foreground); words.MaxLines = 3; words.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(words, 1); line.Children.Add(words); preview.Children.Add(line);
        return new Border { Child = preview, Background = Brush(accessibility.HighContrast ? palette.Surface : definition.Style.Background),
            BorderBrush = Brush(accessibility.HighContrast ? palette.Border : definition.Style.Accent), BorderThickness = new Thickness(1), Padding = new Thickness(16), CornerRadius = new CornerRadius(12) };
    }
}
