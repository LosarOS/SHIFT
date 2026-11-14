namespace Shift.App;

public sealed partial class MainWindow
{
    private async Task OpenRecoveryCenterAsync()
    {
        if (vm.IsLaunching || activeOperations > 1) throw new ValidationException([s["WaitForActions"]]);
        IBackupCatalog catalog = new BackupCatalog();
        using var cancellation = new CancellationTokenSource();
        var body = Stack(12);
        body.Children.Add(Text(s.Recovery("Scope"), 13, true));
        body.Children.Add(Text(s.Recovery("FullCopy"), 12, true));
        var picker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = s.Recovery("Choose") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(picker, "Recovery.Backups");
        var details = Text(s.Recovery("NotChecked"), 13, true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(details, "Recovery.Preview");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(details, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var discovery = Text("", 12, true);
        var selectedPath = ""; string? validatedPath = null; var closed = false; var busy = false; var closeRequested = false;
        var browse = Button(s.Recovery("Browse"), () => { });
        var check = Button(s.Recovery("Inspect"), () => { });
        var create = Button(s["Backup"], () => { });
        var actions = new FlowPanel(); actions.Children.Add(browse); actions.Children.Add(check); actions.Children.Add(create);
        body.Children.Add(picker); body.Children.Add(discovery); body.Children.Add(actions); body.Children.Add(details);
        var dialog = Dialog(s.Recovery("Center"), new ScrollViewer { Content = body, MaxHeight = 520, MaxWidth = 680 }, s["Restore"], s["Cancel"]);
        dialog.IsPrimaryButtonEnabled = false;
        dialog.Closing += (_, args) => { if (busy) { closeRequested = true; cancellation.Cancel(); args.Cancel = true; details.Text = s.Recovery("CancelPending"); } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(create, "Recovery.Create");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(check, "Recovery.Inspect");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(browse, "Recovery.Browse");
        void Reset(string path)
        {
            selectedPath = path; validatedPath = null; dialog.IsPrimaryButtonEnabled = false;
            details.Text = s.Recovery("NotChecked") + (path.Length == 0 ? "" : "\n" + path);
        }
        void SetBusy(bool value)
        {
            busy = value; picker.IsEnabled = browse.IsEnabled = check.IsEnabled = create.IsEnabled = !value && !cancellation.IsCancellationRequested;
            dialog.IsPrimaryButtonEnabled = !value && validatedPath is not null;
            if (!value && closeRequested) dialog.Hide();
        }
        async Task Refresh()
        {
            var inventory = await catalog.ListAsync(BackupDirectory, cancellation.Token);
            if (closed) return;
            picker.Items.Clear();
            foreach (var entry in inventory.Entries)
                picker.Items.Add(new ComboBoxItem { Content = $"{entry.ModifiedAt.LocalDateTime:g} · {entry.SizeBytes:N0} B · {Path.GetFileName(entry.Path)}", Tag = entry.Path });
            discovery.Text = string.Join("\n", new[] { inventory.Entries.Count == 0 ? s.Recovery("Empty") : "", inventory.Truncated ? s.Recovery("Limited") : "", inventory.UnavailableDirectories.Count > 0 ? s.Recovery("Unavailable") : "" }.Where(t => t.Length > 0));
            Reset("");
        }
        picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is ComboBoxItem item) Reset((string)item.Tag); };
        browse.Click += async (_, _) =>
        {
            if (busy) return;
            SetBusy(true);
            try { var path = await PickFileAsync([".db"]); if (!closed && path is not null) { picker.SelectedIndex = -1; Reset(path); } }
            catch (Exception ex) { if (!closed) details.Text = s.Recovery("Failed") + s.ErrorDetails(ex); }
            finally { if (!closed) SetBusy(false); }
        };
        check.Click += async (_, _) =>
        {
            if (busy || selectedPath.Length == 0) return;
            var path = selectedPath; validatedPath = null; SetBusy(true); details.Text = s.Recovery("Checking");
            try
            {
                var result = await catalog.InspectAsync(path, cancellation.Token);
                if (!closed) { validatedPath = result.Path; details.Text = string.Format(s.Recovery("Summary"), result.SchemaVersion, result.Workspaces, result.LaunchRuns, result.StudioProfiles, result.WidgetDefinitions) + "\n" + result.Path; }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { if (!closed) details.Text = s.Recovery("Failed") + s.ErrorDetails(ex); }
            finally { if (!closed) SetBusy(false); }
        };
        create.Click += async (_, _) =>
        {
            if (busy) return;
            SetBusy(true);
            try { await store.BackupAsync(BackupDirectory, cancellation.Token); await Refresh(); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { if (!closed) details.Text = s.ErrorDetails(ex); }
            finally { if (!closed) SetBusy(false); }
        };
        await Refresh();
        var result = await ShowDialogAsync(dialog);
        closed = true; cancellation.Cancel();
        if (result == ContentDialogResult.Primary && validatedPath is not null)
            await RestoreBackupFromPathAsync(validatedPath);
    }
}
