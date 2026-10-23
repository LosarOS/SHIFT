using Microsoft.UI.Xaml.Automation;
namespace Shift.App;

public sealed partial class MainWindow
{
    private async Task EditStudioWidgetAsync(WidgetDefinition? existing, Guid? instanceId = null, WidgetLayout? retainedLayout = null, Guid? retainedProfileId = null)
    {
        if (studioRuntime is null) return;
        var draft = existing ?? new WidgetDefinition { Title = T("Мой виджет", "My widget") };
        var title = new TextBox { Text = draft.Title, MaxLength = 100 };
        var kind = new ComboBox { ItemsSource = Enum.GetValues<WidgetKind>().Select(KindName).ToArray(), SelectedIndex = (int)draft.Kind, IsEnabled = existing is null || existing.Revision == 0 };
        var template = new ComboBox { ItemsSource = StudioTemplates.All.Select(t => t.TemplateId).ToArray(), SelectedIndex = Math.Max(0, StudioTemplates.All.ToList().FindIndex(t => t.TemplateId == draft.Style.TemplateId)) };
        var surface = new ComboBox { ItemsSource = new[] { T("Сплошной", "Solid"), T("Полупрозрачный", "Translucent"), "Acrylic · fallback", "Mica · fallback" }, SelectedIndex = (int)draft.Style.Surface };
        var background = new TextBox { Text = draft.Style.Background, MaxLength = 7 };
        var foreground = new TextBox { Text = draft.Style.Foreground, MaxLength = 7 };
        var accent = new TextBox { Text = draft.Style.Accent, MaxLength = 7 };
        var opacity = new Slider { Minimum = .25, Maximum = 1, Value = draft.Style.Opacity, StepFrequency = .05 };
        NumberBox Number(double value, double min, double max) => new() { Value = value, Minimum = min, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var width = Number(320, 160, 1280); var height = Number(200, 100, 1000);
        var x = Number(.7, 0, 1); var y = Number(.1, 0, 1);
        var monitor = new ComboBox { ItemsSource = new[] { T("Основной / автоматически", "Primary / automatic") }.Concat(studioMonitors.Select(m => m.Name)).ToArray(), SelectedIndex = 0 };
        var fontSize = Number(draft.Style.FontSize, 10, 96); var corners = Number(draft.Style.CornerRadius, 0, 48); var padding = Number(draft.Style.Padding, 0, 48);
        var fonts = StudioValidator.SafeFonts.ToArray();
        var font = new ComboBox { ItemsSource = fonts, SelectedIndex = Math.Max(0, Array.IndexOf(fonts, draft.Style.FontFamily)) };
        var alwaysOnTop = new CheckBox { Content = T("Поверх остальных окон — выключено по умолчанию", "Always on top — off by default") };
        var seconds = new CheckBox { Content = T("Показывать секунды", "Show seconds"), IsChecked = draft.Content.ShowSeconds };
        var clock24 = new CheckBox { Content = T("24-часовой формат", "24-hour clock"), IsChecked = draft.Content.Use24Hour };
        var date = new TextBox { Text = draft.Content.DateFormat, MaxLength = 80 };
        var focus = Number(draft.Content.FocusMinutes, 1, 180); var interval = Number(draft.Content.UpdateIntervalSeconds, 1, 60);
        var notes = new TextBox { Text = draft.Content.InitialNotes, MaxLength = 16000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        var profile = new ComboBox { ItemsSource = new[] { T("Только библиотека", "Library only") }.Concat(studioProfiles.Select(p => p.Name)).ToArray(), SelectedIndex = 0 };
        var instance = studioProfiles.SelectMany(p => p.Widgets).FirstOrDefault(w => instanceId is Guid id ? w.Id == id : w.DefinitionId == existing?.Id);
        if (instance is not null)
        {
            profile.SelectedIndex = studioProfiles.ToList().FindIndex(p => p.Widgets.Any(w => w.Id == instance.Id)) + 1;
            width.Value = instance.Layout.Width; height.Value = instance.Layout.Height; x.Value = instance.Layout.X; y.Value = instance.Layout.Y; alwaysOnTop.IsChecked = instance.Layout.AlwaysOnTop;
            monitor.SelectedIndex = Math.Max(0, studioMonitors.ToList().FindIndex(m => m.DeviceId == instance.Layout.MonitorId) + 1);
        }
        if (retainedLayout is not null)
        {
            width.Value = retainedLayout.Width; height.Value = retainedLayout.Height; x.Value = retainedLayout.X; y.Value = retainedLayout.Y; alwaysOnTop.IsChecked = retainedLayout.AlwaysOnTop;
            monitor.SelectedIndex = Math.Max(0, studioMonitors.ToList().FindIndex(m => m.DeviceId == retainedLayout.MonitorId) + 1);
        }
        if (retainedProfileId is Guid retained) profile.SelectedIndex = Math.Max(0, studioProfiles.ToList().FindIndex(p => p.Id == retained) + 1);
        var fields = Stack(12); var preview = new Border { Height = 150, CornerRadius = new CornerRadius(16), Padding = new Thickness(16) };
        var previewText = new TextBlock { TextWrapping = TextWrapping.Wrap }; preview.Child = previewText;
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(250); timer.IsRepeating = false;
        using var lifetime = new CancellationTokenSource(); var previewOpen = false; Task previewTask = Task.CompletedTask;
        WidgetDefinition Candidate() => draft with { Title = title.Text.Trim(), Kind = (WidgetKind)Math.Max(0, kind.SelectedIndex),
            Style = draft.Style with { TemplateId = StudioTemplates.All[Math.Max(0, template.SelectedIndex)].TemplateId, Surface = (WidgetSurface)Math.Max(0, surface.SelectedIndex),
                Background = background.Text, Foreground = foreground.Text, Accent = accent.Text, Opacity = opacity.Value, FontFamily = fonts[Math.Max(0, font.SelectedIndex)],
                FontSize = fontSize.Value, CornerRadius = corners.Value, Padding = padding.Value },
            Content = draft.Content with { Use24Hour = clock24.IsChecked == true, ShowSeconds = seconds.IsChecked == true, DateFormat = date.Text,
                FocusMinutes = (int)focus.Value, UpdateIntervalSeconds = (int)interval.Value, InitialNotes = notes.Text } };
        WidgetLayout Layout() => new() { Width = width.Value, Height = height.Value, X = x.Value, Y = y.Value,
            MonitorId = monitor.SelectedIndex > 0 ? studioMonitors[monitor.SelectedIndex - 1].DeviceId : "", AlwaysOnTop = alwaysOnTop.IsChecked == true };
        void UpdateMockup()
        {
            try
            {
                var candidate = Candidate(); StudioValidator.Validate(candidate); StudioValidator.Validate(Layout());
                preview.Background = Brush(candidate.Style.Background); preview.BorderBrush = Brush(candidate.Style.Accent); preview.BorderThickness = new Thickness(candidate.Style.BorderWidth);
                preview.CornerRadius = new CornerRadius(candidate.Style.CornerRadius); preview.Padding = new Thickness(candidate.Style.Padding);
                previewText.Foreground = Brush(candidate.Style.Foreground); previewText.FontSize = Math.Min(40, candidate.Style.FontSize); previewText.FontFamily = new FontFamily(candidate.Style.FontFamily);
                previewText.Text = candidate.Kind switch
                {
                    WidgetKind.Clock => DateTime.Now.ToString(candidate.Content.Use24Hour ? (candidate.Content.ShowSeconds ? "HH:mm:ss" : "HH:mm") : (candidate.Content.ShowSeconds ? "hh:mm:ss tt" : "hh:mm tt")) + "\n" + DateTime.Now.ToString(candidate.Content.DateFormat),
                    WidgetKind.Notes => candidate.Content.InitialNotes.Length > 0 ? candidate.Content.InitialNotes : T("Ваша локальная заметка", "Your local note"),
                    WidgetKind.FocusTimer => $"{candidate.Content.FocusMinutes}:00 · " + T("Готов к старту", "Ready to start"),
                    _ => T("Реальные показатели — во внешнем предпросмотре", "Live metrics appear in desktop preview")
                };
                error.IsOpen = false;
            }
            catch (Exception ex) { error.Title = T("Проверьте свойства", "Check properties"); error.Message = s.ErrorDetails(ex); error.IsOpen = true; }
        }
        async Task UpdateDesktopPreviewAsync()
        {
            try
            {
                if (!previewOpen || lifetime.IsCancellationRequested) return;
                var candidate = Candidate(); var layout = Layout(); StudioValidator.Validate(candidate); StudioValidator.Validate(layout);
                await studioRuntime.PreviewAsync(candidate, layout, lifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { error.Title = T("Предпросмотр недоступен", "Preview unavailable"); error.Message = s.ErrorDetails(ex); error.IsOpen = true; }
        }
        void Changed() { UpdateMockup(); if (previewOpen) timer.Start(); }
        timer.Tick += (_, _) => previewTask = UpdateDesktopPreviewAsync();
        foreach (var box in new[] { title, background, foreground, accent, date, notes }) box.TextChanged += (_, _) => Changed();
        foreach (var box in new[] { width, height, x, y, fontSize, corners, padding, focus, interval }) box.ValueChanged += (_, _) => Changed();
        foreach (var box in new[] { kind, surface, font, monitor }) box.SelectionChanged += (_, _) => Changed();
        opacity.ValueChanged += (_, _) => Changed();
        foreach (var box in new[] { alwaysOnTop, seconds, clock24 }) { box.Checked += (_, _) => Changed(); box.Unchecked += (_, _) => Changed(); }
        template.SelectionChanged += (_, _) =>
        {
            var style = StudioTemplates.For(StudioTemplates.All[Math.Max(0, template.SelectedIndex)].TemplateId);
            background.Text = style.Background; foreground.Text = style.Foreground; accent.Text = style.Accent; opacity.Value = style.Opacity; surface.SelectedIndex = (int)style.Surface;
            fontSize.Value = style.FontSize; corners.Value = style.CornerRadius; padding.Value = style.Padding; Changed();
        };
        fields.Children.Add(Text(T("Черновик → предпросмотр → сохранить. Отмена закрывает пробное окно без записи конфигурации.", "Draft → preview → save. Cancel closes the preview without writing configuration."), 13, true));
        fields.Children.Add(StudioField(s["Name"], title, "Studio.Widget.Title")); fields.Children.Add(StudioField(T("Тип", "Type"), kind, "Studio.Widget.Kind"));
        fields.Children.Add(StudioField(T("Шаблон", "Template"), template, "Studio.Widget.Template")); fields.Children.Add(preview);
        fields.Children.Add(Text(T("Это макет внутри редактора. Настоящая прозрачность проверяется во внешнем окне. Acrylic/Mica пока используют fallback.", "This is an in-editor mockup. Real opacity is shown in the separate desktop window. Acrylic/Mica currently use a fallback."), 12, true));
        fields.Children.Add(AsyncButton(T("Предпросмотр на рабочем столе", "Desktop preview"), async () =>
        {
            if (!studioSettings.Enabled) throw new InvalidOperationException(T("Сначала включите Studio.", "Enable Studio first."));
            previewOpen = true; await UpdateDesktopPreviewAsync();
        }, false, "Studio.Widget.Preview"));
        foreach (var (ru, en, control, id) in new (string, string, FrameworkElement, string)[] {
            ("Ширина", "Width", width, "Width"), ("Высота", "Height", height, "Height"), ("Позиция X · 0–1", "Position X · 0–1", x, "X"), ("Позиция Y · 0–1", "Position Y · 0–1", y, "Y"),
            ("Монитор", "Monitor", monitor, "Monitor"), ("Поверхность", "Surface", surface, "Surface"), ("Непрозрачность всего окна", "Whole-window opacity", opacity, "Opacity"),
            ("Фон #RRGGBB", "Background #RRGGBB", background, "Background"), ("Текст #RRGGBB", "Text #RRGGBB", foreground, "Foreground"), ("Акцент #RRGGBB", "Accent #RRGGBB", accent, "Accent"),
            ("Шрифт", "Font", font, "Font"), ("Размер шрифта", "Font size", fontSize, "FontSize"), ("Скругление", "Corner radius", corners, "Corners"), ("Отступ", "Padding", padding, "Padding"),
            ("Формат даты", "Date format", date, "Date"), ("Длительность · минуты", "Duration · minutes", focus, "Focus"), ("Интервал монитора · секунды", "Monitor interval · seconds", interval, "Interval"),
            ("Начальный текст новой заметки", "Initial text for a new note", notes, "Notes"), ("Добавить в профиль", "Add to profile", profile, "Profile") }) fields.Children.Add(StudioField(T(ru, en), control, "Studio.Widget." + id));
        fields.Children.Add(alwaysOnTop); fields.Children.Add(clock24); fields.Children.Add(seconds); fields.Children.Add(error);
        var form = new ContentControl { Content = fields, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var dialog = Dialog("Widget Lab", new ScrollViewer { Content = form, MaxHeight = 510 }, s["Save"], s["Cancel"]);
        WidgetDefinition? languageDraft = null; WidgetLayout? languageLayout = null; Guid? languageProfile = null; string? nextLanguage = null;
        fields.Children.Insert(0, Button(T("English · сохранить черновик", "Русский · keep draft"), () =>
        {
            // Validate before reopening so invalid partial values remain in this form for correction.
            var candidate = Candidate(); var layout = Layout(); StudioValidator.Validate(candidate); StudioValidator.Validate(layout);
            languageDraft = candidate; languageLayout = layout; languageProfile = profile.SelectedIndex > 0 ? studioProfiles[profile.SelectedIndex - 1].Id : null;
            nextLanguage = s.Language == "ru" ? "en" : "ru"; dialog.Hide();
        }, false, "Studio.Widget.Language"));
        dialog.SecondaryButtonText = T("Сбросить оформление", "Reset appearance");
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true; var style = StudioTemplates.For(StudioTemplates.All[Math.Max(0, template.SelectedIndex)].TemplateId);
            background.Text = style.Background; foreground.Text = style.Foreground; accent.Text = style.Accent; opacity.Value = style.Opacity;
            surface.SelectedIndex = (int)style.Surface; fontSize.Value = style.FontSize; corners.Value = style.CornerRadius; padding.Value = style.Padding; Changed();
        };
        var saving = false; dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            saving = true; dialog.IsSecondaryButtonEnabled = false; form.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false; var deferral = args.GetDeferral();
            try
            {
                var candidate = Candidate(); var layout = Layout(); StudioValidator.Validate(candidate); StudioValidator.Validate(layout);
                draft = await StudioData.SaveDefinitionAsync(candidate);
                if (profile.SelectedIndex > 0)
                {
                    var target = studioProfiles[profile.SelectedIndex - 1];
                    var placed = (instanceId is Guid origin ? target.Widgets.FirstOrDefault(w => w.Id == origin) : null) ?? target.Widgets.FirstOrDefault(w => w.DefinitionId == draft.Id);
                    var placement = placed is null ? new WidgetInstance { DefinitionId = draft.Id, Layout = layout } : placed with { Layout = layout };
                    await StudioData.SaveProfileAsync(target with { Widgets = target.Widgets.Where(w => w.Id != placement.Id).Append(placement).ToArray() });
                }
                await RefreshStudioAsync();
                // Saving definitions does not silently mutate the active desktop. Apply the profile separately.
            }
            catch (Exception ex) { args.Cancel = true; error.Title = T("Виджет не сохранён полностью", "Widget was not fully saved"); error.Message = s.ErrorDetails(ex); error.IsOpen = true; }
            finally { saving = false; dialog.IsSecondaryButtonEnabled = true; form.IsEnabled = true; dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        UpdateMockup();
        try { await ShowDialogAsync(dialog); }
        finally { timer.Stop(); lifetime.Cancel(); await previewTask; await studioRuntime.ClosePreviewAsync(); }
        if (nextLanguage is not null && languageDraft is not null)
        {
            var settings = vm.Settings with { Language = nextLanguage };
            try { await store.SaveSettingsAsync(settings); vm.Settings = settings; }
            catch (Exception ex) { log.Error("Editor language could not be saved; draft retained", ex); ShowNotice("Widget Lab", T("Язык не сохранён; черновик сохранён в редакторе.", "Language was not saved; your editor draft is retained."), true); }
            s.Language = nextLanguage; BuildShell();
            await EditStudioWidgetAsync(languageDraft, instanceId, languageLayout, languageProfile);
        }
        else RenderScreen();
    }
}
