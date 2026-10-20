using Windows.Storage.Pickers;
using Windows.Storage;
namespace Shift.App;

public sealed partial class MainWindow
{
    private ContentDialog Dialog(string title, UIElement content, string primary, string close)
    {
        var dialog = new ContentDialog { Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, DefaultButton = ContentDialogButton.Close, XamlRoot = root.XamlRoot, RequestedTheme = root.RequestedTheme };
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        return dialog;
    }
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (dialogOpen)
            throw new InvalidOperationException("Another dialog is already open.");
        dialogOpen = true;
        try
        {
            return await dialog.ShowAsync();
        }
        finally { dialogOpen = false; }
    }
    private async Task<bool> ConfirmAsync(string title, string message, string action)
    {
        var text = Text(message, 14);
        text.MaxWidth = 600;
        return await ShowDialogAsync(Dialog(title, text, action, s["Cancel"])) == ContentDialogResult.Primary;
    }
    private async Task OnboardAsync()
    {
        var body = Stack(18);
        body.Children.Add(Text("↗ SHIFT", 32, color: palette.Accent));
        body.Children.Add(Text(s["OnboardingNote"], 15));
        var dialog = Dialog(s["OnboardingTitle"], body, s["Demo"], s["StartEmpty"]);
        var result = await ShowDialogAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            var demo = new Workspace
            {
                Name = s["DemoName"],
                Description = s["DemoDescription"],
                IsFavorite = true,
                Tags = ["demo"],
                Items = [
                new(){Name=s["Docs"],Kind=ItemKind.Url,Target="https://learn.microsoft.com/dotnet/",Order=0},
                new(){Name=s["HomeFolder"],Kind=ItemKind.Folder,Target=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Order=1,DelayMs=300}]
            };
            await vm.SaveAsync(demo);
        }
        vm.Settings = vm.Settings with
        {
            OnboardingComplete = true
        };
        await store.SaveSettingsAsync(vm.Settings);
        RenderScreen();
    }
    private async Task EditWorkspaceAsync(Workspace? workspace)
    {
        var source = workspace ?? new Workspace { Name = s["Untitled"] };
        var name = new TextBox { Text = source.Name, MaxLength = 100 };
        var description = new TextBox { Text = source.Description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72, MaxLength = 2000 };
        var tags = new TextBox { Text = string.Join(", ", source.Tags), MaxLength = 820 };
        var colors = new[] { "#BCADFF", "#98BEFF", "#A9D4A5", "#E5BA8C", source.Color }.Distinct().ToArray();
        var accents = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var names = new[] { s["Lavender"], s["Blue"], s["Sage"], s["Sand"] };
        for (var index = 0; index < colors.Length; index++)
        {
            var label = index < names.Length ? names[index] : s["CustomColor"];
            var swatch = Row(10);
            swatch.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(5), Background = Brush(colors[index]) });
            swatch.Children.Add(Text(label));
            var choice = new ComboBoxItem { Content = swatch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choice, label);
            accents.Items.Add(choice);
        }
        accents.SelectedIndex = Math.Max(0, Array.IndexOf(colors, source.Color));
        var favorite = new CheckBox { Content = s["Favorite"], IsChecked = source.IsFavorite };
        var fields = Stack(16);
        fields.MinWidth = 440;
        fields.Children.Add(Field(s["Name"], name));
        fields.Children.Add(Field(s["Description"], description));
        fields.Children.Add(Field(s["Tags"], tags));
        fields.Children.Add(Field(s["Color"], accents));
        fields.Children.Add(favorite);
        var errors = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        fields.Children.Add(errors);
        Workspace? saved = null;
        var dialog = Dialog(workspace is null ? s["New"] : s["Edit"], new ScrollViewer { Content = fields, MaxHeight = 540 }, s["Save"], s["Cancel"]);
        var saving = false;
        dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            saving = true;
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                var candidate = source with
                {
                    Name = name.Text.Trim(),
                    Description = description.Text.Trim(),
                    Tags = tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    Color = colors[Math.Max(0, accents.SelectedIndex)],
                    IsFavorite = favorite.IsChecked == true
                };
                await vm.SaveAsync(candidate);
                saved = candidate;
            }
            catch (Exception ex) { args.Cancel = true; errors.Title = s["ValidationHint"]; errors.Message = s.ErrorDetails(ex); errors.IsOpen = true; log.Error("Workspace save failed", ex); }
            finally { saving = false; dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary && saved is not null)
            OpenWorkspace(vm.Workspaces.First(w => w.Id == saved.Id));
    }
    private async Task EditItemAsync(Workspace workspace, WorkspaceItem? item)
    {
        var source = item ?? new WorkspaceItem { Kind = ItemKind.App, Order = workspace.Items.Count == 0 ? 0 : workspace.Items.Max(i => i.Order) + 1 };
        var name = new TextBox { Text = source.Name, MaxLength = 100 };
        var kinds = Enum.GetValues<ItemKind>();
        var type = new ComboBox { ItemsSource = kinds.Select(k => s[k.ToString()]).ToArray(), SelectedIndex = (int)source.Kind, HorizontalAlignment = HorizontalAlignment.Stretch };
        var target = new TextBox { Text = source.Target, MaxLength = 8192, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
        var arguments = new TextBox { Text = source.Arguments, MaxLength = 8192 };
        var directory = new TextBox { Text = source.WorkingDirectory };
        var shellChoice = new ComboBox { ItemsSource = new[] { "PowerShell (Windows)", "cmd.exe" }, SelectedIndex = (int)source.Shell, HorizontalAlignment = HorizontalAlignment.Stretch };
        var delay = new NumberBox { Value = source.DelayMs, Minimum = 0, Maximum = 300000, SmallChange = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var note = new TextBox { Text = source.Note, MaxLength = 2000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var enabled = new CheckBox { Content = s["Enabled"], IsChecked = source.Enabled };
        var fields = Stack(14);
        fields.MinWidth = 460;
        fields.Children.Add(Field(s["Name"], name));
        fields.Children.Add(Field(s["Kind"], type));
        fields.Children.Add(Field(s["Target"], target));
        var browse = AsyncButton(s["Browse"], async () =>
        {
            var kind = kinds[Math.Max(0, type.SelectedIndex)];
            string? selected;
            if (kind == ItemKind.Folder)
                selected = await PickFolderAsync();
            else
                selected = await PickFileAsync(kind == ItemKind.App ? new[] { ".exe", ".lnk" } : new[] { "*" });
            if (selected is not null)
            {
                target.Text = selected;
                if (string.IsNullOrWhiteSpace(name.Text))
                    name.Text = Path.GetFileNameWithoutExtension(selected);
            }
        });
        fields.Children.Add(browse);
        var argsField = Field(s["Arguments"], arguments);
        fields.Children.Add(argsField);
        var shellField = Field(s["Shell"], shellChoice);
        fields.Children.Add(shellField);
        fields.Children.Add(Field(s["WorkingDirectory"], directory));
        fields.Children.Add(Field(s["Delay"], delay));
        fields.Children.Add(Field(s["Note"], note));
        fields.Children.Add(enabled);
        var warning = new InfoBar { Title = s["Command"], Message = s["CommandWarning"], Severity = InfoBarSeverity.Warning, IsClosable = false };
        fields.Children.Add(warning);
        void RefreshType()
        {
            var kind = kinds[Math.Max(0, type.SelectedIndex)];
            argsField.Visibility = kind == ItemKind.App ? Visibility.Visible : Visibility.Collapsed;
            shellField.Visibility = kind == ItemKind.Command ? Visibility.Visible : Visibility.Collapsed;
            browse.Visibility = kind is ItemKind.Url or ItemKind.Command ? Visibility.Collapsed : Visibility.Visible;
            warning.IsOpen = kind is ItemKind.Command or ItemKind.App;
        }
        type.SelectionChanged += (_, _) => RefreshType();
        RefreshType();
        var errors = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        fields.Children.Add(errors);
        var dialog = Dialog(item is null ? s["AddItem"] : s["EditItem"], new ScrollViewer { Content = fields, MaxHeight = 530 }, s["Save"], s["Cancel"]);
        var saving = false;
        dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            saving = true;
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                if (double.IsNaN(delay.Value) || delay.Value < 0 || delay.Value > 300000)
                    throw new ValidationException([s["Delay"] + ": 0–300000."]);
                var kind = kinds[Math.Max(0, type.SelectedIndex)];
                var candidate = source with
                {
                    Name = name.Text.Trim(),
                    Kind = kind,
                    Target = kind == ItemKind.Command ? target.Text : target.Text.Trim(),
                    Arguments = kind == ItemKind.App ? arguments.Text : "",
                    WorkingDirectory = directory.Text.Trim(),
                    Shell = (CommandShell)Math.Max(0, shellChoice.SelectedIndex),
                    DelayMs = (int)delay.Value,
                    Note = note.Text.Trim(),
                    Enabled = enabled.IsChecked == true
                };
                var updated = workspace with
                {
                    Items = item is null ? [.. workspace.Items, candidate] : workspace.Items.Select(i => i.Id == item.Id ? candidate : i).ToArray()
                };
                await vm.SaveAsync(updated);
            }
            catch (Exception ex) { args.Cancel = true; errors.Title = s["ValidationHint"]; errors.Message = s.ErrorDetails(ex); errors.IsOpen = true; log.Error("Item save failed", ex); }
            finally { saving = false; dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary)
            RenderScreen();
    }
    public async Task<bool> ConfirmAsync(IReadOnlyList<WorkspaceItem> commands, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var body = Stack(18);
        body.Children.Add(Text(s["CommandWarning"], 14));
        foreach (var command in commands)
        {
            var content = Stack(8);
            content.Children.Add(Text(command.Name + " · " + command.Shell, 16, weight: Microsoft.UI.Text.FontWeights.SemiBold));
            var code = Text(command.Target, 13);
            code.FontFamily = new FontFamily("Cascadia Mono");
            code.IsTextSelectionEnabled = true;
            content.Children.Add(code);
            if (command.WorkingDirectory.Length > 0)
                content.Children.Add(Text(s["WorkingDirectory"] + ": " + command.WorkingDirectory, 12, true));
            body.Children.Add(Card(content, 14));
        }
        var dialog = Dialog(s["ConfirmCommands"], new ScrollViewer { Content = body, MaxHeight = 480, MaxWidth = 620 }, s["Allow"], s["Cancel"]);
        // Keep the consent dialog bound to the lifetime of this launch request.
        using var registration = token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        var allowed = await ShowDialogAsync(dialog) == ContentDialogResult.Primary;
        token.ThrowIfCancellationRequested();
        return allowed;
    }
    private async Task RestoreBackupAsync()
    {
        if (vm.IsLaunching)
            throw new ValidationException([s["WaitForLaunch"]]);
        var path = await PickFileAsync([".db"]);
        if (path is null)
            return;
        if (!await ConfirmAsync(s["RestoreTitle"], path + "\n\n" + s["RestoreNote"], s["Restore"]))
            return;
        maintenance = true;
        try
        {
            var result = await store.RestoreAsync(path, BackupDirectory);
            vm.Settings = await store.LoadSettingsAsync();
            vm.Settings = vm.Settings with
            {
                StartWithWindows = !paths.Portable && desktop.IsStartupEnabled()
            };
            s.Language = vm.Settings.Language;
            await vm.RefreshAsync();
            vm.Selected = null;
            vm.Section = "Settings";
            ApplyTheme();
            ConfigureTray();
            BuildShell();
            try
            {
                windowCommands.ConfigureHotkey(vm.Settings.GlobalHotkeyEnabled);
            }
            catch (Exception ex) { log.Error("Restored global shortcut unavailable", ex); ShowNotice(s["Restored"], result.SafetyBackupPath + "\n" + ex.Message, true); return; }
            ShowNotice(s["Restored"], result.SafetyBackupPath);
        }
        finally { maintenance = false; }
    }
    private async Task<string?> PickFileAsync(IEnumerable<string> extensions)
    {
        var picker = new FileOpenPicker();
        foreach (var extension in extensions)
            picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
    private async Task ImportAsync()
    {
        var path = await PickFileAsync([".json"]);
        if (path is null)
            return;
        Workspace imported;
        await using (var input = File.OpenRead(path))
            imported = await WorkspaceTransfer.ImportAsync(input);
        var preview = Stack(14);
        preview.Children.Add(Text(imported.Name, 22));
        preview.Children.Add(Text(s["ImportNote"], 14, true));
        foreach (var item in imported.Items)
        {
            var row = Stack(5);
            row.Children.Add(Text($"{item.Name} · {s[item.Kind.ToString()]} · {s[item.Enabled ? "Enabled" : "Disabled"]}", 14));
            row.Children.Add(Text(item.Target, 12, true));
            if (item.Arguments.Length > 0)
                row.Children.Add(Text(item.Arguments, 12, true));
            preview.Children.Add(Card(row, 12));
        }
        if (await ShowDialogAsync(Dialog(s["ImportReview"], new ScrollViewer { Content = preview, MaxHeight = 500, MaxWidth = 620 }, s["Import"], s["Cancel"])) != ContentDialogResult.Primary)
            return;
        await vm.SaveAsync(imported);
        OpenWorkspace(vm.Workspaces.First(w => w.Id == imported.Id));
        ShowNotice(s["Imported"]);
    }
    private async Task ExportAsync(Workspace workspace)
    {
        var picker = new FileSavePicker { SuggestedFileName = string.Concat(workspace.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) };
        picker.FileTypeChoices.Add("SHIFT workspace", new List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
            return;
        var json = WorkspaceTransfer.Export(workspace);
        // Atomic replacement in the destination directory. Existing export survives an interrupted write.
        var temp = file.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, json);
            File.Move(temp, file.Path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        ShowNotice(s["Exported"]);
    }
}
