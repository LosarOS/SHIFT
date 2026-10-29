using Microsoft.UI.Xaml.Automation;
namespace Shift.App;

public sealed partial class MainWindow
{
    private async Task EditStudioWidgetAsync(WidgetDefinition? existing, Guid? instanceId = null, WidgetLayout? retainedLayout = null, Guid? retainedProfileId = null)
    {
        if (studioRuntime is null) return;
        await studioRuntime.FlushPendingAsync();
        await RefreshStudioAsync();
        if (existing is { Revision: > 0 } && retainedLayout is null)
            existing = studioDefinitions.FirstOrDefault(d => d.Id == existing.Id) ?? throw new InvalidOperationException("A widget definition is missing.");
        var draft = existing ?? new WidgetDefinition { Title = T("Мой виджет", "My widget") };
        var title = new TextBox { Text = draft.Title, MaxLength = 100 };
        var kind = new ComboBox { ItemsSource = Enum.GetValues<WidgetKind>().Select(KindName).ToArray(), SelectedIndex = (int)draft.Kind, IsEnabled = existing is null || existing.Revision == 0 };
        var templateIds = StudioTemplates.All.Select(t => t.TemplateId).ToList();
        if (!templateIds.Contains(draft.Style.TemplateId, StringComparer.Ordinal)) templateIds.Add(draft.Style.TemplateId);
        var appearancePresets = StudioTemplates.All.ToDictionary(style => style.TemplateId, StringComparer.Ordinal);
        if (!appearancePresets.ContainsKey(draft.Style.TemplateId)) appearancePresets.Add(draft.Style.TemplateId, draft.Style);
        var template = new ComboBox { ItemsSource = templateIds.Select(s.TemplateName).ToArray(), SelectedIndex = templateIds.IndexOf(draft.Style.TemplateId) };
        var appearanceSources = studioDefinitions.Where(widget => widget.Id != draft.Id)
            .OrderBy(widget => widget.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var appearanceSource = new ComboBox
        {
            ItemsSource = appearanceSources.Select(widget => widget.Title + " · " + KindName(widget.Kind)).ToArray(),
            PlaceholderText = appearanceSources.Length == 0
                ? T("Сначала добавьте другой виджет", "Add another widget first")
                : T("Выберите виджет из библиотеки", "Choose a library widget"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = appearanceSources.Length > 0,
            SelectedIndex = -1
        };
        var surface = new ComboBox { ItemsSource = new[] { T("Сплошной", "Solid"), T("Полупрозрачный", "Translucent"), T("Acrylic · резервный фон", "Acrylic · fallback"), T("Mica · резервный фон", "Mica · fallback") }, SelectedIndex = (int)draft.Style.Surface };
        var background = new TextBox { Text = draft.Style.Background, MaxLength = 7 };
        var foreground = new TextBox { Text = draft.Style.Foreground, MaxLength = 7 };
        var accent = new TextBox { Text = draft.Style.Accent, MaxLength = 7 };
        var opacity = new Slider { Minimum = .25, Maximum = 1, Value = draft.Style.Opacity, StepFrequency = .05 };
        NumberBox Number(double value, double min, double max) => new() { Value = value, Minimum = min, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var width = Number(320, 160, 1600); var height = Number(200, 100, 1200);
        var x = Number(.7, 0, 1); var y = Number(.1, 0, 1);
        var monitorIds = new List<string> { "" }; monitorIds.AddRange(studioMonitors.Select(m => m.DeviceId));
        var monitorNames = new List<string> { T("Основной / автоматически", "Primary / automatic") }; monitorNames.AddRange(studioMonitors.Select(m => m.Name));
        var monitor = new ComboBox { SelectedIndex = 0 };
        void SelectMonitor(string id)
        {
            if (!monitorIds.Contains(id, StringComparer.Ordinal))
            { monitorIds.Add(id); monitorNames.Add(T("Недоступный экран · сохранить привязку", "Unavailable display · keep assignment")); }
            monitor.ItemsSource = monitorNames.ToArray(); monitor.SelectedIndex = monitorIds.IndexOf(id);
        }
        SelectMonitor("");
        var fontSize = Number(draft.Style.FontSize, 10, 96); var corners = Number(draft.Style.CornerRadius, 0, 64); var padding = Number(draft.Style.Padding, 0, 64);
        var borderWidth = Number(draft.Style.BorderWidth, 0, 8);
        var shadow = new CheckBox { Content = T("Тень окна · доступность зависит от Windows", "Window shadow · availability depends on Windows"), IsChecked = draft.Style.Shadow };
        AutomationProperties.SetAutomationId(shadow, "Studio.Widget.Shadow");
        var fonts = StudioValidator.SafeFonts.ToArray();
        var font = new ComboBox { ItemsSource = fonts, SelectedIndex = Math.Max(0, Array.IndexOf(fonts, draft.Style.FontFamily)) };
        var alwaysOnTop = new CheckBox { Content = T("Поверх всех окон — выключено по умолчанию", "Always on top — off by default") };
        var seconds = new CheckBox { Content = T("Показывать секунды", "Show seconds"), IsChecked = draft.Content.ShowSeconds };
        var clock24 = new CheckBox { Content = T("24-часовой формат", "24-hour clock"), IsChecked = draft.Content.Use24Hour };
        var date = new TextBox { Text = draft.Content.DateFormat, MaxLength = 80 };
        var focus = Number(draft.Content.FocusMinutes, 1, 1440); var interval = Number(draft.Content.UpdateIntervalSeconds, 1, 3600);
        var notes = new TextBox { Text = draft.Content.InitialNotes, MaxLength = 20000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        var profile = new ComboBox { ItemsSource = new[] { T("Только библиотека", "Library only") }.Concat(studioProfiles.Select(p => p.Name)).ToArray(), SelectedIndex = 0 };
        var instance = studioProfiles.SelectMany(p => p.Widgets).FirstOrDefault(w => instanceId is Guid id ? w.Id == id : w.DefinitionId == existing?.Id);
        if (instance is not null)
        {
            profile.SelectedIndex = studioProfiles.ToList().FindIndex(p => p.Widgets.Any(w => w.Id == instance.Id)) + 1;
            width.Value = instance.Layout.Width; height.Value = instance.Layout.Height; x.Value = instance.Layout.X; y.Value = instance.Layout.Y; alwaysOnTop.IsChecked = instance.Layout.AlwaysOnTop;
            SelectMonitor(instance.Layout.MonitorId);
        }
        if (retainedLayout is not null)
        {
            width.Value = retainedLayout.Width; height.Value = retainedLayout.Height; x.Value = retainedLayout.X; y.Value = retainedLayout.Y; alwaysOnTop.IsChecked = retainedLayout.AlwaysOnTop;
            SelectMonitor(retainedLayout.MonitorId);
        }
        if (retainedLayout is not null) profile.SelectedIndex = retainedProfileId is Guid retained ? Math.Max(0, studioProfiles.ToList().FindIndex(p => p.Id == retained) + 1) : 0;
        var clockContent = Stack(12); var monitorContent = Stack(12); var timerContent = Stack(12); var notesContent = Stack(12);
        var applyingAppearance = false;
        var fields = Stack(12); var preview = new Border { Height = 150, CornerRadius = new CornerRadius(16), Padding = new Thickness(16) };
        var previewText = new TextBlock { TextWrapping = TextWrapping.Wrap }; preview.Child = previewText;
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(250); timer.IsRepeating = false;
        using var lifetime = new CancellationTokenSource(); var previewOpen = false; Task previewTask = Task.CompletedTask;
        WidgetDefinition Candidate() => draft with { Title = title.Text.Trim(), Kind = (WidgetKind)Math.Max(0, kind.SelectedIndex),
            Style = draft.Style with { TemplateId = templateIds[Math.Max(0, template.SelectedIndex)], Surface = (WidgetSurface)Math.Max(0, surface.SelectedIndex),
                Background = background.Text, Foreground = foreground.Text, Accent = accent.Text, Opacity = opacity.Value, FontFamily = fonts[Math.Max(0, font.SelectedIndex)],
                FontSize = fontSize.Value, CornerRadius = corners.Value, Padding = padding.Value, BorderWidth = borderWidth.Value, Shadow = shadow.IsChecked == true },
            Content = draft.Content with { Use24Hour = clock24.IsChecked == true, ShowSeconds = seconds.IsChecked == true, DateFormat = date.Text,
                FocusMinutes = (int)focus.Value, UpdateIntervalSeconds = (int)interval.Value, InitialNotes = notes.Text } };
        WidgetLayout Layout() => new() { Width = width.Value, Height = height.Value, X = x.Value, Y = y.Value,
            MonitorId = monitorIds[Math.Max(0, monitor.SelectedIndex)], AlwaysOnTop = alwaysOnTop.IsChecked == true };
        void RevealContentErrors(Exception exception)
        {
            // A new widget can switch kinds with unfinished values. Keep any invalid
            // content reachable for repair instead of trapping it in a hidden panel.
            if (exception is not ValidationException validation) return;
            foreach (var issue in validation.Issues)
            {
                if (issue.Code == "Studio.DateFormat") clockContent.Visibility = Visibility.Visible;
                if (issue.Code == "Studio.Interval") { monitorContent.Visibility = Visibility.Visible; timerContent.Visibility = Visibility.Visible; }
                if (issue.Code == "Studio.Notes") notesContent.Visibility = Visibility.Visible;
            }
        }
        void UpdateMockup()
        {
            try
            {
                var candidate = Candidate(); StudioValidator.Validate(candidate); StudioValidator.Validate(Layout());
                preview.Background = Brush(accessibility.HighContrast ? palette.Surface : candidate.Style.Background); preview.BorderBrush = Brush(accessibility.HighContrast ? palette.Border : candidate.Style.Accent); preview.BorderThickness = new Thickness(candidate.Style.BorderWidth);
                preview.CornerRadius = new CornerRadius(candidate.Style.CornerRadius); preview.Padding = new Thickness(candidate.Style.Padding);
                previewText.Foreground = Brush(accessibility.HighContrast ? palette.Text : candidate.Style.Foreground); previewText.FontSize = Math.Min(40, candidate.Style.FontSize); previewText.FontFamily = new FontFamily(candidate.Style.FontFamily);
                previewText.Text = candidate.Kind switch
                {
                    WidgetKind.Clock => DateTime.Now.ToString(candidate.Content.Use24Hour ? (candidate.Content.ShowSeconds ? "HH:mm:ss" : "HH:mm") : (candidate.Content.ShowSeconds ? "hh:mm:ss tt" : "hh:mm tt")) + "\n" + DateTime.Now.ToString(candidate.Content.DateFormat, s.Culture),
                    WidgetKind.Notes => candidate.Content.InitialNotes.Length > 0 ? candidate.Content.InitialNotes : T("Ваша локальная заметка", "Your local note"),
                    WidgetKind.FocusTimer => $"{candidate.Content.FocusMinutes}:00 · " + T("Готов к старту", "Ready to start"),
                    _ => T("Реальные показатели — во внешнем предпросмотре", "Live metrics appear in desktop preview")
                };
                error.IsOpen = false;
            }
            catch (Exception ex) { RevealContentErrors(ex); error.Title = T("Проверьте свойства", "Check properties"); error.Message = s.ErrorDetails(ex); error.IsOpen = true; }
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
            catch (Exception ex) { error.Title = T("Предпросмотр недоступен", "Preview unavailable"); error.Message = s.ErrorDetails(ex); error.IsOpen = true; DispatcherQueue.TryEnqueue(() => error.StartBringIntoView()); }
        }
        void RefreshContent()
        {
            var selected = (WidgetKind)Math.Max(0, kind.SelectedIndex);
            clockContent.Visibility = selected == WidgetKind.Clock ? Visibility.Visible : Visibility.Collapsed;
            monitorContent.Visibility = selected == WidgetKind.SystemMonitor ? Visibility.Visible : Visibility.Collapsed;
            timerContent.Visibility = selected == WidgetKind.FocusTimer ? Visibility.Visible : Visibility.Collapsed;
            notesContent.Visibility = selected == WidgetKind.Notes ? Visibility.Visible : Visibility.Collapsed;
        }
        void Changed() { if (applyingAppearance) return; UpdateMockup(); if (previewOpen) timer.Start(); }
        void ApplyAppearance(WidgetStyle style)
        {
            // Copy only a validated declarative style. Identity, content, layout and persistence remain untouched.
            StudioValidator.Validate(new WidgetDefinition { Title = "Appearance", Style = style });
            applyingAppearance = true;
            try
            {
                if (!StudioTemplates.All.Any(builtIn => builtIn.TemplateId == style.TemplateId)) appearancePresets[style.TemplateId] = style;
                if (!templateIds.Contains(style.TemplateId, StringComparer.Ordinal))
                {
                    templateIds.Add(style.TemplateId);
                    template.ItemsSource = templateIds.Select(s.TemplateName).ToArray();
                }
                draft = draft with { Style = style };
                template.SelectedIndex = templateIds.IndexOf(style.TemplateId);
                background.Text = style.Background; foreground.Text = style.Foreground; accent.Text = style.Accent;
                opacity.Value = style.Opacity; surface.SelectedIndex = (int)style.Surface;
                font.SelectedIndex = Array.IndexOf(fonts, style.FontFamily); fontSize.Value = style.FontSize;
                corners.Value = style.CornerRadius; padding.Value = style.Padding;
                borderWidth.Value = style.BorderWidth; shadow.IsChecked = style.Shadow;
            }
            finally { applyingAppearance = false; }
            Changed();
        }
        timer.Tick += (_, _) => previewTask = UpdateDesktopPreviewAsync();
        foreach (var box in new[] { title, background, foreground, accent, date, notes }) box.TextChanged += (_, _) => Changed();
        foreach (var box in new[] { width, height, x, y, fontSize, corners, padding, borderWidth, focus, interval }) box.ValueChanged += (_, _) => Changed();
        foreach (var box in new[] { surface, font, monitor }) box.SelectionChanged += (_, _) => Changed();
        kind.SelectionChanged += (_, _) => { RefreshContent(); Changed(); };
        opacity.ValueChanged += (_, _) => Changed();
        foreach (var box in new[] { alwaysOnTop, seconds, clock24, shadow }) { box.Checked += (_, _) => Changed(); box.Unchecked += (_, _) => Changed(); }
        template.SelectionChanged += (_, _) =>
        {
            if (applyingAppearance) return;
            var selectedId = templateIds[Math.Max(0, template.SelectedIndex)];
            ApplyAppearance(appearancePresets.GetValueOrDefault(selectedId) ?? draft.Style);
        };
        var copyAppearance = Button(T("Скопировать оформление", "Copy appearance"), () =>
        {
            if (appearanceSource.SelectedIndex < 0 || appearanceSource.SelectedIndex >= appearanceSources.Length) return;
            var source = appearanceSources[appearanceSource.SelectedIndex]; StudioValidator.Validate(source);
            ApplyAppearance(source.Style);
        }, false, "Studio.Widget.CopyAppearance");
        copyAppearance.IsEnabled = false;
        appearanceSource.SelectionChanged += (_, _) => copyAppearance.IsEnabled = appearanceSource.SelectedIndex >= 0;
        fields.Children.Add(Text(T("Черновик → предпросмотр → сохранить. Отмена закрывает пробное окно без записи конфигурации.", "Draft → preview → save. Cancel closes the preview without writing configuration."), 13, true));
        fields.Children.Add(StudioField(s["Name"], title, "Studio.Widget.Title")); fields.Children.Add(StudioField(T("Тип", "Type"), kind, "Studio.Widget.Kind"));
        fields.Children.Add(StudioField(T("Шаблон", "Template"), template, "Studio.Widget.Template"));
        fields.Children.Add(StudioField(T("Взять оформление другого виджета", "Reuse another widget's appearance"), appearanceSource, "Studio.Widget.StyleSource"));
        fields.Children.Add(copyAppearance);
        fields.Children.Add(Text(T("Копируется только оформление. Текст, таймер и расположение не меняются; сохранение — отдельное действие.",
            "Copies appearance only. Text, timer and placement stay unchanged; saving is a separate action."), 12, true));
        fields.Children.Add(preview);
        fields.Children.Add(Text(T("Это макет внутри редактора. Настоящая прозрачность проверяется во внешнем окне. Acrylic/Mica пока используют резервный фон.", "This is an in-editor mockup. Real opacity is shown in the separate desktop window. Acrylic/Mica currently use a fallback."), 12, true));
        var desktopPreview = AsyncButton(T("Предпросмотр на рабочем столе", "Desktop preview"), async () =>
        {
            if (!studioSettings.Enabled) throw new InvalidOperationException(T("Сначала включите Studio.", "Enable Studio first."));
            previewOpen = true; await UpdateDesktopPreviewAsync();
        }, false, "Studio.Widget.Preview");
        fields.Children.Add(desktopPreview);
        foreach (var (ru, en, control, id) in new (string, string, FrameworkElement, string)[] {
            ("Ширина", "Width", width, "Width"), ("Высота", "Height", height, "Height"), ("Позиция X · 0–1", "Position X · 0–1", x, "X"), ("Позиция Y · 0–1", "Position Y · 0–1", y, "Y"),
            ("Монитор", "Monitor", monitor, "Monitor"), ("Поверхность", "Surface", surface, "Surface"), ("Непрозрачность всего окна", "Whole-window opacity", opacity, "Opacity"),
            ("Фон #RRGGBB", "Background #RRGGBB", background, "Background"), ("Текст #RRGGBB", "Text #RRGGBB", foreground, "Foreground"), ("Акцент #RRGGBB", "Accent #RRGGBB", accent, "Accent"),
            ("Толщина рамки", "Border width", borderWidth, "BorderWidth"),
            ("Шрифт", "Font", font, "Font"), ("Размер шрифта", "Font size", fontSize, "FontSize"), ("Скругление", "Corner radius", corners, "Corners"), ("Отступ", "Padding", padding, "Padding") })
        {
            var section = id switch { "Width" => T("Размер и расположение", "Size and placement"), "Surface" => T("Оформление", "Appearance"), "Font" => T("Типографика", "Typography"), _ => null };
            if (section is not null) fields.Children.Add(Text(section, 19, weight: Microsoft.UI.Text.FontWeights.SemiBold));
            fields.Children.Add(StudioField(T(ru, en), control, "Studio.Widget." + id));
            if (id == "BorderWidth") fields.Children.Add(shadow);
        }
        fields.Children.Add(alwaysOnTop);
        fields.Children.Add(Text(T("Содержимое", "Content"), 19, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        // Hidden kind-specific controls retain their original values for imports and language changes.
        // Switching a new widget's kind never destroys an unfinished note or coerces valid bounds.
        clockContent.Children.Add(StudioField(T("Формат даты", "Date format"), date, "Studio.Widget.Date"));
        AutomationProperties.SetAutomationId(clock24, "Studio.Widget.Clock24"); AutomationProperties.SetAutomationId(seconds, "Studio.Widget.Seconds");
        clockContent.Children.Add(clock24); clockContent.Children.Add(seconds);
        monitorContent.Children.Add(StudioField(T("Интервал монитора · секунды", "Monitor interval · seconds"), interval, "Studio.Widget.Interval"));
        timerContent.Children.Add(StudioField(T("Длительность · минуты", "Duration · minutes"), focus, "Studio.Widget.Focus"));
        notesContent.Children.Add(StudioField(T("Начальный текст новой заметки", "Initial text for a new note"), notes, "Studio.Widget.Notes"));
        notesContent.Children.Add(Text(T("Этот текст используется для новых экземпляров. Текущую заметку редактируйте в окне виджета.",
            "This text is used for new instances. Edit a current note in its widget window."), 12, true));
        fields.Children.Add(clockContent); fields.Children.Add(monitorContent); fields.Children.Add(timerContent); fields.Children.Add(notesContent);
        fields.Children.Add(Text(T("Размещение в профиле", "Profile placement"), 19, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        fields.Children.Add(StudioField(T("Добавить в профиль", "Add to profile"), profile, "Studio.Widget.Profile"));
        fields.Children.Add(error);
        RefreshContent();
        UIElement editorContent = new ScrollViewer { Content = fields, MaxHeight = 510 };
        if (root.ActualWidth >= 1120)
        {
            fields.Children.Remove(preview); fields.Children.Remove(desktopPreview);
            var live = Stack(16); live.Children.Add(Text(T("Предпросмотр", "Preview"), 22)); live.Children.Add(preview); live.Children.Add(desktopPreview);
            live.Children.Add(Text(T("Изменения хранятся в черновике до сохранения.", "Changes remain a draft until saved."), 13, true));
            var grid = new Grid { ColumnSpacing = 24 }; grid.ColumnDefinitions.Add(new() { Width = new GridLength(270) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(live); var properties = new ScrollViewer { Content = fields, MaxHeight = 510 }; Grid.SetColumn(properties, 1); grid.Children.Add(properties); editorContent = grid;
        }
        var form = new ContentControl { Content = editorContent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var dialog = Dialog(s["Studio.Lab"], form, s["Save"], s["Cancel"]);
        if (root.ActualWidth >= 1120) dialog.Resources["ContentDialogMaxWidth"] = 1040d;
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
            args.Cancel = true;
            var selectedId = templateIds[Math.Max(0, template.SelectedIndex)];
            ApplyAppearance(appearancePresets.GetValueOrDefault(selectedId) ?? draft.Style);
        };
        var saving = false; dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            saving = true; dialog.IsSecondaryButtonEnabled = false; form.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false; var deferral = args.GetDeferral();
            try
            {
                var candidate = Candidate(); var layout = Layout(); StudioValidator.Validate(candidate); StudioValidator.Validate(layout);
                DesktopProfile? targetProfile = null;
                if (profile.SelectedIndex > 0)
                {
                    var target = studioProfiles[profile.SelectedIndex - 1];
                    var placed = (instanceId is Guid origin ? target.Widgets.FirstOrDefault(w => w.Id == origin) : null) ?? target.Widgets.FirstOrDefault(w => w.DefinitionId == candidate.Id);
                    var placement = placed is null ? new WidgetInstance { DefinitionId = candidate.Id, Layout = layout } : placed with { Layout = layout };
                    targetProfile = target with { Widgets = target.Widgets.Where(w => w.Id != placement.Id).Append(placement).ToArray() };
                }
                if (StudioData is not IStudioWidgetEditorStore editor) throw new NotSupportedException("Atomic Studio editing is unavailable.");
                draft = await editor.SaveWidgetEditAsync(candidate, targetProfile);
                await RefreshStudioAsync();
                // Saving definitions does not silently mutate the active desktop. Apply the profile separately.
            }
            catch (Exception ex) { args.Cancel = true; error.Title = T("Изменения не сохранены", "Changes were not saved"); error.Message = s.ErrorDetails(ex); error.IsOpen = true; DispatcherQueue.TryEnqueue(() => error.StartBringIntoView()); }
            finally { saving = false; dialog.IsSecondaryButtonEnabled = true; form.IsEnabled = true; dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        UpdateMockup();
        try { await ShowDialogAsync(dialog); }
        finally { timer.Stop(); lifetime.Cancel(); await previewTask; await studioRuntime.ClosePreviewAsync(); }
        if (nextLanguage is not null && languageDraft is not null)
        {
            var settings = vm.Settings with { Language = nextLanguage };
            try { await store.SaveSettingsAsync(settings); vm.Settings = settings; }
            catch (Exception ex) { log.Error("Editor language could not be saved; draft retained", ex); ShowNotice(s["Studio.Lab"], T("Язык не сохранён; черновик сохранён в редакторе.", "Language was not saved; your editor draft is retained."), true); }
            s.Language = nextLanguage; BuildShell();
            await EditStudioWidgetAsync(languageDraft, instanceId, languageLayout, languageProfile);
        }
        else RenderScreen();
    }
}
