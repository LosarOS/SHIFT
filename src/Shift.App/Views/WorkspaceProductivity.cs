namespace Shift.App;

public sealed partial class MainWindow
{
    private async Task OpenComposerAsync(Workspace workspace)
    {
        CompositionPreview? preview = null;
        Workspace? committed = null;
        var saving = false;
        var refreshed = false;
        string? refreshError = null;
        var selected = new HashSet<int>();
        var kinds = new[] { ItemKind.Url, ItemKind.App, ItemKind.Folder, ItemKind.File };
        var type = new ComboBox { ItemsSource = kinds.Select(k => s[k.ToString()]).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180, MaxLength = WorkspaceComposer.MaximumInput };
        var inputPanel = Stack(12);
        inputPanel.Children.Add(Text(s["Compose.Scope"], 13, true));
        inputPanel.Children.Add(Field(s["Kind"], type));
        inputPanel.Children.Add(Field(s["Compose.Input"], input));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(type, "Compose.Kind");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(input, "Compose.Input");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, s["Compose.Input"]);
        var summary = Text("", 17);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(summary, "Compose.Summary");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(summary, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var rows = Stack(10);
        var enabled = new CheckBox { Content = s["Compose.Enabled"], IsChecked = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(enabled, "Compose.Enabled");
        var delay = new NumberBox { Value = 0, Minimum = 0, Maximum = 300000, SmallChange = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var review = Stack(12);
        review.Children.Add(summary);
        review.Children.Add(Text(s["Compose.ReviewScope"], 13, true));
        review.Children.Add(enabled);
        review.Children.Add(Field(s["Delay"], delay));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(delay, "Compose.Delay");
        review.Children.Add(rows);
        review.Visibility = Visibility.Collapsed;
        var errors = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        var body = Stack(14);
        body.Children.Add(inputPanel); body.Children.Add(review); body.Children.Add(errors);
        var scroll = new ScrollViewer { Content = body, MaxHeight = 500, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var dialog = Dialog(s["Compose.Title"], scroll, s["Compose.Check"], s["Cancel"]);
        void UpdateSelection()
        {
            if (preview is null) return;
            summary.Text = string.Format(s.Culture, s["Compose.Summary"], selected.Count, preview.Ready, preview.Duplicates, preview.Invalid, preview.RemainingSlots);
            dialog.IsPrimaryButtonEnabled = !saving && selected.Count > 0 && selected.Count <= preview.RemainingSlots;
        }
        void ShowPreview()
        {
            rows.Children.Clear(); selected.Clear();
            foreach (var entry in preview!.Entries)
            {
                var details = Stack(5);
                details.Children.Add(Text(entry.Line + ". " + (entry.Item?.Name ?? s["Compose.Invalid"]), 16, weight: Microsoft.UI.Text.FontWeights.SemiBold));
                var target = Text(entry.Target, 12, true); target.IsTextSelectionEnabled = true; details.Children.Add(target);
                foreach (var code in entry.Codes) details.Children.Add(Text(s["Validation." + code], 12, true));
                if (entry.State == CompositionState.Ready)
                {
                    var choice = new CheckBox { Content = details, IsChecked = selected.Count < preview.RemainingSlots, HorizontalAlignment = HorizontalAlignment.Stretch };
                    if (choice.IsChecked == true) selected.Add(entry.Line);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(choice, "Compose.Row." + entry.Line);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choice, entry.Line + ". " + entry.Item!.Name + ". " + entry.Target);
                    choice.Checked += (_, _) => { selected.Add(entry.Line); UpdateSelection(); };
                    choice.Unchecked += (_, _) => { selected.Remove(entry.Line); UpdateSelection(); };
                    rows.Children.Add(Card(choice, 12));
                }
                else rows.Children.Add(Card(details, 12));
            }
            inputPanel.Visibility = Visibility.Collapsed; review.Visibility = Visibility.Visible;
            dialog.PrimaryButtonText = s["Compose.Add"]; dialog.SecondaryButtonText = s["Compose.Back"];
            errors.IsOpen = false; scroll.ChangeView(null, 0, null, true); UpdateSelection();
        }
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            if (saving) return;
            preview = null; review.Visibility = Visibility.Collapsed; inputPanel.Visibility = Visibility.Visible;
            errors.IsOpen = false; dialog.PrimaryButtonText = s["Compose.Check"]; dialog.SecondaryButtonText = "";
            dialog.IsPrimaryButtonEnabled = true; scroll.ChangeView(null, 0, null, true);
        };
        dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (preview is null)
            {
                args.Cancel = true;
                try { preview = WorkspaceComposer.Preview(workspace, input.Text, kinds[Math.Max(0, type.SelectedIndex)]); ShowPreview(); }
                catch (Exception ex) { errors.Title = s["ValidationHint"]; errors.Message = s.ErrorDetails(ex); errors.IsOpen = true; }
                return;
            }
            saving = true; dialog.IsPrimaryButtonEnabled = false; dialog.IsSecondaryButtonEnabled = false;
            var deferral = args.GetDeferral();
            try
            {
                if (activeOperations > 1 || vm.IsLaunching) throw new ValidationException([s["WaitForActions"]]);
                if (!double.IsFinite(delay.Value) || delay.Value is < 0 or > 300000 || delay.Value != Math.Truncate(delay.Value)) throw new ValidationException([new ValidationIssue("Delay", "Invalid delay.")]);
                var current = (await store.ListAsync()).FirstOrDefault(w => w.Id == workspace.Id)
                    ?? throw new ValidationException([new ValidationIssue("Compose.Stale", "Workspace no longer exists.")]);
                if (activeOperations > 1 || vm.IsLaunching) throw new ValidationException([s["WaitForActions"]]);
                var updated = WorkspaceComposer.Apply(current, preview, selected.OrderBy(i => i).ToArray(), checked((int)delay.Value), enabled.IsChecked == true);
                await store.SaveAsync(updated);
                committed = updated;
                // A refresh failure after commit must never append the same rows again.
                try { await vm.RefreshAsync(); refreshed = true; }
                catch (Exception ex) { log.Error("Workspace composed, shell refresh failed", ex); refreshError = s.ErrorDetails(ex); }
            }
            catch (Exception ex)
            {
                args.Cancel = true; errors.Title = s["ValidationHint"]; errors.Message = s.ErrorDetails(ex); errors.IsOpen = true;
            }
            finally { saving = false; dialog.IsSecondaryButtonEnabled = true; UpdateSelection(); deferral.Complete(); }
        };
        await ShowDialogAsync(dialog);
        if (committed is not null)
        {
            OpenWorkspace(refreshed ? vm.Workspaces.FirstOrDefault(w => w.Id == committed.Id) ?? committed : committed);
            ShowNotice(s["Compose.Saved"], refreshError ?? "", refreshError is not null);
        }
    }

    private async Task OpenLaunchPlanAsync(Workspace workspace)
    {
        var plan = WorkspaceLaunchPlanner.Create(workspace);
        var body = Stack(14);
        body.Children.Add(Text(s["Plan.Scope"], 13, true));
        var summary = Text(string.Format(s.Culture, s["Plan.Summary"], plan.Enabled, plan.Disabled, plan.Commands, plan.RepeatedTargets), 18);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(summary, "Plan.Summary");
        body.Children.Add(summary);
        body.Children.Add(Text(s["Plan.DelayBudget"] + ": " + TimeSpan.FromMilliseconds(plan.TotalDelayMs).ToString(@"hh\:mm\:ss\.fff"), 14, color: palette.Accent));
        if (plan.Commands > 0) body.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Title = s["ConfirmCommands"], Message = s["CommandWarning"] });
        foreach (var step in plan.Steps)
        {
            var row = Stack(6);
            row.Children.Add(Text(step.Position.ToString("00") + " · " + step.Item.Name, 17, weight: Microsoft.UI.Text.FontWeights.SemiBold));
            row.Children.Add(Text(s[step.Item.Kind.ToString()] + " · " + s[step.Item.Enabled ? "Enabled" : "Disabled"], 12, true));
            row.Children.Add(Text(step.DelayBudgetMs is long budget ? s["Plan.NotBefore"] + " " + TimeSpan.FromMilliseconds(budget).ToString(@"hh\:mm\:ss\.fff") : s["Plan.Skipped"], 12, true));
            var target = Text(step.Item.Target, 13); target.IsTextSelectionEnabled = true; row.Children.Add(target);
            if (step.Item.Arguments.Length > 0) row.Children.Add(Text(s["Arguments"] + ": " + step.Item.Arguments, 12, true));
            if (step.Item.WorkingDirectory.Length > 0) row.Children.Add(Text(s["WorkingDirectory"] + ": " + step.Item.WorkingDirectory, 12, true));
            if (step.RepeatsItemId is not null) row.Children.Add(Text(s["Plan.Repeated"], 12, color: palette.Accent));
            var card = Card(row, 14);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(card, "Plan.Step." + step.Item.Id.ToString("N"));
            body.Children.Add(card);
        }
        if (plan.Steps.Count == 0) body.Children.Add(Text(s["NoItemsNote"], 14, true));
        var dialog = Dialog(s["Plan.Title"], new ScrollViewer { Content = body, MaxHeight = 500, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, s["Plan.CheckTargets"], s["Close"]);
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary && !quitRequested)
            await OpenDoctorAsync(vm.Workspaces.FirstOrDefault(w => w.Id == workspace.Id) ?? workspace);
    }
}
