using System.Text;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Shift.App;

public sealed partial class MainWindow
{
    private string StudioPackNotice => T(
        "Переносит виджеты, оформление и относительное расположение. Обои, пути к файлам, привязка к пространству, текущие заметки и таймеры не включаются. Это не резервная копия данных.",
        "Transfers widgets, appearance and relative placement. Wallpaper, file paths, workspace binding, current notes and timers are excluded. This is not a data backup.");

    private async Task ExportStudioProfileAsync(DesktopProfile source)
    {
        if (studioRuntime is not null) await studioRuntime.FlushPendingAsync();
        await RefreshStudioAsync();
        var profile = studioProfiles.FirstOrDefault(p => p.Id == source.Id)
            ?? throw new InvalidOperationException(T("Профиль больше не существует.", "The profile no longer exists."));
        var includeNotes = new CheckBox { Content = T("Включить начальный текст шаблонов заметок", "Include note template initial text"), IsChecked = false };
        AutomationProperties.SetAutomationId(includeNotes, "Studio.Pack.IncludeNotes");
        var output = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 180 };
        AutomationProperties.SetAutomationId(output, "Studio.Pack.Output"); AutomationProperties.SetName(output, T("JSON профиля", "Profile JSON"));
        var withoutNotes = StudioProfileTransfer.Export(profile, studioDefinitions);
        string? withNotes = null;
        try { withNotes = StudioProfileTransfer.Export(profile, studioDefinitions, true); }
        catch (InvalidDataException) { includeNotes.IsEnabled = false; }
        void RefreshOutput() => output.Text = includeNotes.IsChecked == true ? withNotes ?? withoutNotes : withoutNotes;
        RefreshOutput(); includeNotes.Checked += (_, _) => RefreshOutput(); includeNotes.Unchecked += (_, _) => RefreshOutput();
        var body = Stack(14); body.Children.Add(Text(profile.Name, 23)); body.Children.Add(Text(StudioPackNotice, 14, true));
        body.Children.Add(Text(T("Названия и описание видны в файле. Начальный текст выключен по умолчанию. Проверьте содержимое перед передачей другому человеку.",
            "Titles and description are visible in the file. Initial text is off by default. Review the contents before sharing."), 13, true));
        if (withNotes is null) body.Children.Add(Text(T("Начальный текст превышает лимит профиля 2 МиБ. Оформление можно экспортировать без текста; для заметок используйте отдельный экспорт.",
            "Initial text exceeds the 2 MiB profile limit. Export the appearance without text; use the separate notes export for note content."), 13, true));
        body.Children.Add(includeNotes); body.Children.Add(output);
        body.Children.Add(Button(T("Копировать JSON", "Copy JSON"), () => CopyStudioText(output.Text), false, "Studio.Pack.Copy"));
        await ShowStudioTextExportAsync(T("Экспорт профиля", "Export profile"), body, () => output.Text, "SHIFT-profile", ".shiftstudio.json");
    }

    private async Task ImportStudioProfileAsync()
    {
        var body = Stack(14);
        body.Children.Add(Text(StudioPackNotice, 13, true));
        var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 180,
            PlaceholderText = T("Выберите файл или вставьте JSON профиля", "Choose a file or paste profile JSON"), MaxLength = StudioProfileTransfer.MaxBytes };
        AutomationProperties.SetAutomationId(input, "Studio.Pack.Input"); AutomationProperties.SetName(input, input.PlaceholderText);
        var sourceFields = Stack(12);
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        var preview = Stack(12); preview.Visibility = Visibility.Collapsed;
        StudioPresetDraft? draft = null;
        var name = new TextBox { MaxLength = 100 };
        var arrange = new CheckBox { Content = T("Разложить видимые виджеты на основном экране", "Arrange visible widgets on the primary display"), IsChecked = true };
        AutomationProperties.SetAutomationId(arrange, "Studio.Pack.Arrange");
        var summary = Stack(8);
        var layoutWarning = Text("", 13, true);
        var form = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var dialog = Dialog(T("Импорт профиля", "Import profile"), new ScrollViewer { Content = form, MaxHeight = 500 }, s["Save"], s["Cancel"]);
        dialog.IsPrimaryButtonEnabled = false;
        var saving = false;
        void Problem(Exception ex)
        {
            error.Title = T("Профиль не импортирован", "Profile was not imported"); error.Message = s.ErrorDetails(ex); error.IsOpen = true;
            DispatcherQueue.TryEnqueue(() => error.StartBringIntoView());
        }
        void Review()
        {
            try
            {
                draft = StudioProfileTransfer.Import(input.Text);
                name.Text = draft.Profile.Name;
                summary.Children.Clear();
                summary.Children.Add(Text($"{T("Виджеты", "Widgets")}: {draft.Profile.Widgets.Count} · {T("Шаблоны", "Definitions")}: {draft.Definitions.Count}", 15));
                summary.Children.Add(Text(draft.Profile.Description, 13, true));
                foreach (var definition in draft.Definitions)
                    summary.Children.Add(Text(KindName(definition.Kind) + " · " + definition.Title, 14));
                summary.Children.Add(Text(T("Будет создан отдельный профиль с новыми идентификаторами. Активное окружение и существующие данные сохранятся. После импорта примените профиль вручную.",
                    "A separate profile with new identities will be created. Your active environment and existing data stay unchanged. Apply the profile manually after import."), 13, true));
                summary.Children.Add(Text(T("Привязки к мониторам сброшены. Окна откроются в режиме редактирования; «Поверх всех окон» и сквозные клики выключены.",
                    "Display bindings are reset. Windows will open in Edit mode; Always on Top and click-through are off."), 13, true));
                if (draft.Definitions.Any(d => d.Content.InitialNotes.Length > 0))
                    summary.Children.Add(Text(T("В файле есть начальный текст заметок. Проверьте его в мастерской перед применением.", "The file contains initial note text. Review it in Widget Lab before applying."), 13, true));
                if (!draft.Profile.Enabled) summary.Children.Add(Text(T("Этот профиль отключён. Включите его в редакторе перед применением.", "This profile is disabled. Enable it in the editor before applying."), 13, true));
                UpdateLayoutWarning();
                error.IsOpen = false; sourceFields.Visibility = Visibility.Collapsed; preview.Visibility = Visibility.Visible; dialog.IsPrimaryButtonEnabled = true;
                DispatcherQueue.TryEnqueue(() => name.Focus(FocusState.Keyboard));
            }
            catch (Exception ex) { draft = null; dialog.IsPrimaryButtonEnabled = false; Problem(ex); }
        }
        void UpdateLayoutWarning()
        {
            IReadOnlyList<string> warnings = draft is not null && arrange.IsChecked == true
                ? StudioPresetLayout.Arrange(draft.Profile.Widgets.Count(w => w.Visible), studioMonitors).Warnings : [];
            layoutWarning.Text = string.Join("\n", warnings.Select(key => s[key]));
            layoutWarning.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        arrange.Checked += (_, _) => UpdateLayoutWarning(); arrange.Unchecked += (_, _) => UpdateLayoutWarning();
        var choose = AsyncButton(T("Выбрать файл", "Choose file"), async () =>
        {
            try
            {
                var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json"); WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
                var file = await picker.PickSingleFileAsync(); if (file is null) return;
                var bytes = await ReadStudioPackAsync(file.Path);
                input.Text = new UTF8Encoding(false, true).GetString(bytes); Review();
            }
            catch (Exception ex) { Problem(ex); }
        }, false, "Studio.Pack.Choose");
        sourceFields.Children.Add(choose); sourceFields.Children.Add(input);
        sourceFields.Children.Add(Button(T("Проверить и продолжить", "Review and continue"), Review, true, "Studio.Pack.Review"));
        preview.Children.Add(StudioField(s["Name"], name, "Studio.Pack.Name")); preview.Children.Add(summary); preview.Children.Add(arrange); preview.Children.Add(layoutWarning);
        preview.Children.Add(Button(T("Назад к JSON", "Back to JSON"), () =>
        {
            draft = null; preview.Visibility = Visibility.Collapsed; sourceFields.Visibility = Visibility.Visible; dialog.IsPrimaryButtonEnabled = false; error.IsOpen = false;
        }, false, "Studio.Pack.Back"));
        body.Children.Add(sourceFields); body.Children.Add(preview); body.Children.Add(error); form.Content = body;
        dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
        DesktopProfile? created = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral(); saving = true; form.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
            try
            {
                if (draft is null) throw new InvalidDataException(T("Сначала проверьте файл профиля.", "Review the profile file first."));
                var candidate = draft.Profile with { Name = name.Text.Trim() };
                if (arrange.IsChecked == true)
                {
                    var visible = candidate.Widgets.Where(w => w.Visible).ToArray();
                    var placement = StudioPresetLayout.Arrange(visible.Length, studioMonitors);
                    var layouts = visible.Select((w, i) => (w.Id, Layout: placement.Layouts[i])).ToDictionary(p => p.Id, p => p.Layout);
                    candidate = candidate with { Widgets = candidate.Widgets.Select(w => w with { Layout = layouts.GetValueOrDefault(w.Id) ?? w.Layout with { MonitorId = "" } }).ToArray() };
                }
                StudioValidator.Validate(candidate);
                if (StudioData is not IStudioPresetStore presets) throw new NotSupportedException(T("Атомарный импорт недоступен.", "Atomic import is unavailable."));
                created = await presets.SavePresetAsync(draft with { Profile = candidate });
            }
            catch (Exception ex) { args.Cancel = true; Problem(ex); }
            finally { saving = false; form.IsEnabled = true; dialog.IsPrimaryButtonEnabled = draft is not null; deferral.Complete(); }
        };
        await ShowDialogAsync(dialog);
        if (created is not null)
        {
            await RefreshStudioAsync(); studioPage = "Profiles"; studioProfileSearch = ""; RenderScreen();
            ShowNotice(T("Профиль импортирован", "Profile imported"), T("Сохранён отдельно. Окружение не изменено; примените профиль, когда будете готовы.", "Saved separately. Your environment was not changed; apply the profile when ready."));
        }
    }

    private async Task ExportStudioNotesAsync(DesktopProfile source)
    {
        // Export is also a recovery escape when database writes are unavailable.
        // Capture live text after reading saved states; do not require a successful flush.
        await RefreshStudioAsync();
        var profile = studioProfiles.FirstOrDefault(p => p.Id == source.Id)
            ?? throw new InvalidOperationException(T("Профиль больше не существует.", "The profile no longer exists."));
        var states = new List<WidgetRuntimeState>();
        foreach (var instance in profile.Widgets.Where(w => studioDefinitions.Any(d => d.Id == w.DefinitionId && d.Kind == WidgetKind.Notes)))
        {
            var state = await StudioData.GetWidgetStateAsync(instance.Id); if (state is not null) states.Add(state);
        }
        if (studioRuntime is not null)
        {
            var live = await studioRuntime.CaptureLiveStatesAsync(profile.Id);
            var latest = states.ToDictionary(state => state.InstanceId);
            foreach (var state in live) latest[state.InstanceId] = state;
            states = latest.Values.ToList();
        }
        var text = StudioNotesExport.Export(profile, studioDefinitions, states, s.Culture);
        var output = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 260 };
        AutomationProperties.SetAutomationId(output, "Studio.Notes.Output"); AutomationProperties.SetName(output, T("Текст заметок профиля", "Profile note text"));
        var body = Stack(14); body.Children.Add(Text(profile.Name, 23));
        body.Children.Add(Text(T("Снимок текста открытых виджетов и сохранённых скрытых заметок. Файл и буфер обмена не зашифрованы: проверьте текст перед передачей. Неоткрытые локальные recovery-черновики не включаются; для них используйте меню восстановления самого виджета.",
            "Snapshot of live widget text and saved hidden notes. The file and clipboard are unencrypted: review before sharing. Unopened local recovery drafts are excluded; use the widget's own recovery menu for them."), 13, true));
        body.Children.Add(output); body.Children.Add(Button(T("Копировать заметки", "Copy notes"), () => CopyStudioText(output.Text), false, "Studio.Notes.Copy"));
        await ShowStudioTextExportAsync(T("Сохранить заметки", "Export notes"), body, () => output.Text, "SHIFT-notes", ".txt");
    }

    private static void CopyStudioText(string text)
    {
        var package = new DataPackage(); package.SetText(text); Clipboard.SetContent(package);
    }

    private async Task ShowStudioTextExportAsync(string title, StackPanel body, Func<string> content, string suggestedName, string extension)
    {
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false }; body.Children.Add(error);
        var form = new ContentControl { Content = body, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var dialog = Dialog(title, new ScrollViewer { Content = form, MaxHeight = 500 }, T("Сохранить файл", "Save file"), s["Close"]);
        var saving = false; dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral(); saving = true; form.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
            try
            {
                var picker = new FileSavePicker { SuggestedFileName = suggestedName }; picker.FileTypeChoices.Add(title, new[] { extension });
                WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
                var file = await picker.PickSaveFileAsync();
                if (file is null) { args.Cancel = true; return; }
                var temporary = file.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                    await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    { await writer.WriteAsync(content()); await writer.FlushAsync(); stream.Flush(true); }
                    File.Move(temporary, file.Path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (Exception ex)
            { args.Cancel = true; error.Title = T("Файл не сохранён", "File was not saved"); error.Message = s.ErrorDetails(ex); error.IsOpen = true; DispatcherQueue.TryEnqueue(() => error.StartBringIntoView()); }
            finally { saving = false; form.IsEnabled = true; dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        await ShowDialogAsync(dialog);
    }

    private static async Task<byte[]> ReadStudioPackAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (stream.Length > StudioProfileTransfer.MaxBytes) throw new InvalidDataException("Studio profile file exceeds the supported size.");
        using var buffer = new MemoryStream(); var block = new byte[65536];
        while (true)
        {
            var count = await stream.ReadAsync(block); if (count == 0) break;
            if (buffer.Length + count > StudioProfileTransfer.MaxBytes) throw new InvalidDataException("Studio profile file exceeds the supported size.");
            await buffer.WriteAsync(block.AsMemory(0, count));
        }
        return buffer.ToArray();
    }
}
