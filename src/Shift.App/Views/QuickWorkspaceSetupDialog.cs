namespace Shift.App;

public sealed partial class MainWindow
{
    private readonly IApplicationDiscovery applicationDiscovery = new StartMenuApplicationDiscovery();

    private async Task OpenQuickSetupAsync()
    {
        using var cancellation = new CancellationTokenSource();
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var selectionOrder = new List<string>();
        var createManually = false;
        ApplicationDiscovery? discovery = null;
        Workspace? saved = null;
        // A refresh failure after SQLite commit must not duplicate a workspace on retry.
        var draftIdentity = new Workspace();
        var previewStep = false;
        var saving = false;
        Task scan = Task.CompletedTask;
        var content = Stack(14);
        content.Children.Add(Text(s["Setup.Scope"], 13, true));
        var status = Text(s["Setup.Scanning"], 16);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(status, "Setup.Status");
        content.Children.Add(status);
        var errors = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        content.Children.Add(errors);
        var selection = Stack(12);
        var query = new TextBox { PlaceholderText = s["Setup.Search"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(query, s["Setup.Search"]);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(query, "Setup.Search");
        selection.Children.Add(query);
        var selectionTools = new FlowPanel();
        selection.Children.Add(selectionTools);
        var choices = Stack(8);
        selection.Children.Add(new ScrollViewer { Content = choices, MaxHeight = 280, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        content.Children.Add(selection);
        var preview = Stack(12);
        preview.Visibility = Visibility.Collapsed;
        var name = new TextBox { Text = s["Untitled"], MaxLength = 100 };
        preview.Children.Add(Field(s["Name"], name));
        // Field assigns the generic Field.Name identity; preserve this wizard-specific identity afterwards.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(name, "Setup.Name");
        preview.Children.Add(Text(s["Setup.Unknown"], 13, true));
        var previewItems = Stack(8);
        preview.Children.Add(new ScrollViewer { Content = previewItems, MaxHeight = 250, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        content.Children.Add(preview);
        var form = new ContentControl { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var dialog = Dialog(s["Setup.Title"], new ScrollViewer { Content = form, MaxHeight = 470, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, s["Setup.Next"], s["Cancel"]);
        dialog.IsPrimaryButtonEnabled = false;
        void UpdateStatus()
        {
            if (discovery is null) return;
            status.Text = string.Format(s[previewStep ? "Setup.PreviewCount" : "Setup.SelectionCount"], selected.Count, discovery.Candidates.Count);
            dialog.IsPrimaryButtonEnabled = selected.Count is > 0 and <= QuickWorkspaceSetup.MaximumSelection && !saving;
        }
        void RenderChoices()
        {
            choices.Children.Clear();
            if (discovery is null) return;
            var matching = QuickWorkspaceSetup.Filter(discovery.Candidates, query.Text);
            if (matching.Count == 0)
                choices.Children.Add(Text(s[discovery.Candidates.Count == 0 ? "Setup.Empty" : "NoResults"], 14, true));
            string? lastGroup = null;
            foreach (var candidate in matching)
            {
                var group = s["Setup.Source." + candidate.Source] + (candidate.Group.Length > 0 ? " · " + candidate.Group : "");
                if (group != lastGroup)
                {
                    choices.Children.Add(Text(group, 12, true));
                    lastGroup = group;
                }
                var label = Stack(4);
                label.Children.Add(Text("↗  " + candidate.Name, 15));
                label.Children.Add(Text(s["Setup.UnknownShort"], 11, true));
                var choice = new CheckBox { Content = label, IsChecked = selected.Contains(candidate.Id), HorizontalAlignment = HorizontalAlignment.Stretch };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choice, candidate.Name + ". " + s["Setup.UnknownShort"]);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(choice, "Setup.App." + candidate.Id);
                ToolTipService.SetToolTip(choice, candidate.ShortcutPath);
                choice.Checked += (_, _) =>
                {
                    if (selected.Count >= QuickWorkspaceSetup.MaximumSelection)
                    {
                        choice.IsChecked = false;
                        errors.Title = s["Setup.Limit"];
                        errors.IsOpen = true;
                        return;
                    }
                    if (selected.Add(candidate.Id)) selectionOrder.Add(candidate.Id);
                    errors.IsOpen = false;
                    UpdateStatus();
                };
                choice.Unchecked += (_, _) => { selected.Remove(candidate.Id); selectionOrder.Remove(candidate.Id); UpdateStatus(); };
                choices.Children.Add(choice);
            }
            UpdateStatus();
        }
        selectionTools.Children.Add(Button(s["Setup.Clear"], () => { selected.Clear(); selectionOrder.Clear(); RenderChoices(); }, false, "Setup.Clear"));
        query.TextChanged += (_, _) => RenderChoices();
        void ShowSelection()
        {
            previewStep = false;
            selection.Visibility = Visibility.Visible;
            preview.Visibility = Visibility.Collapsed;
            dialog.PrimaryButtonText = s["Setup.Next"];
            dialog.SecondaryButtonText = discovery?.Candidates.Count == 0 ? s["Setup.Manual"] : "";
            errors.IsOpen = false;
            UpdateStatus();
            query.Focus(FocusState.Programmatic);
        }
        async Task DiscoverAsync()
        {
            try
            {
                discovery = await applicationDiscovery.DiscoverAsync(cancellation.Token);
                if (cancellation.IsCancellationRequested) return;
                RenderChoices();
                if (discovery.Candidates.Count == 0) dialog.SecondaryButtonText = s["Setup.Manual"];
                if (discovery.UnavailableLocations > 0 || discovery.InvalidShortcuts > 0 || discovery.Limited)
                    content.Children.Add(Text(s["Setup.Partial"], 12, true));
                query.Focus(FocusState.Programmatic);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.Error("Application discovery failed", ex);
                if (!cancellation.IsCancellationRequested)
                {
                    status.Text = s["Setup.Failed"];
                    dialog.IsPrimaryButtonEnabled = false;
                    dialog.SecondaryButtonText = s["Setup.Manual"];
                }
            }
        }
        dialog.Opened += (_, _) => scan = DiscoverAsync();
        void RenderPreview()
        {
            previewItems.Children.Clear();
            if (discovery is null) return;
            var indexed = discovery.Candidates.ToDictionary(c => c.Id, StringComparer.Ordinal);
            for (var index = 0; index < selectionOrder.Count; index++)
            {
                var position = index; var id = selectionOrder[position]; var candidate = indexed[id];
                var item = Stack(6);
                item.Children.Add(Text((position + 1) + ".  " + candidate.Name, 15));
                item.Children.Add(Text(candidate.ShortcutPath, 11, true));
                var order = new FlowPanel();
                void Move(int delta)
                {
                    var target = position + delta;
                    (selectionOrder[position], selectionOrder[target]) = (selectionOrder[target], selectionOrder[position]);
                    RenderPreview();
                }
                var up = Button("↑ " + s["MoveUp"], () => Move(-1), false, "Setup.Up." + id); up.IsEnabled = position > 0;
                var down = Button("↓ " + s["MoveDown"], () => Move(1), false, "Setup.Down." + id); down.IsEnabled = position < selectionOrder.Count - 1;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(up, s["MoveUp"] + ": " + candidate.Name);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(down, s["MoveDown"] + ": " + candidate.Name);
                order.Children.Add(up); order.Children.Add(down); item.Children.Add(order);
                previewItems.Children.Add(Card(item, 12));
            }
        }
        dialog.SecondaryButtonClick += (_, args) =>
        {
            if (saving) { args.Cancel = true; return; }
            if (previewStep) { args.Cancel = true; ShowSelection(); }
            else createManually = true;
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (saved is not null) return;
            if (discovery is null || saving) { args.Cancel = true; return; }
            if (!previewStep)
            {
                args.Cancel = true;
                previewStep = true;
                selection.Visibility = Visibility.Collapsed;
                preview.Visibility = Visibility.Visible;
                RenderPreview();
                dialog.PrimaryButtonText = s["Setup.Save"];
                dialog.SecondaryButtonText = s["Setup.Back"];
                UpdateStatus();
                name.Focus(FocusState.Programmatic);
                name.SelectAll();
                return;
            }
            saving = true; form.IsEnabled = false;
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            dialog.IsSecondaryButtonEnabled = false;
            try
            {
                // Preview order is the launch order; Back/search never reorder explicit choices.
                var orderedIds = selectionOrder.ToArray();
                var draft = QuickWorkspaceSetup.CreateDraft(name.Text, discovery.Candidates, orderedIds) with { Id = draftIdentity.Id, CreatedAt = draftIdentity.CreatedAt };
                await vm.SaveAsync(draft);
                saved = draft;
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                errors.Title = s["ValidationHint"];
                errors.Message = s.ErrorDetails(ex);
                errors.IsOpen = true;
                if (ex is ValidationException) log.Info("Quick workspace validation requires correction.");
                else log.Error("Quick workspace save failed", ex);
            }
            finally { saving = false; form.IsEnabled = true; dialog.IsSecondaryButtonEnabled = true; UpdateStatus(); deferral.Complete(); }
        };
        dialog.Closing += (_, args) => { if (saving) args.Cancel = true; else cancellation.Cancel(); };
        try { await ShowDialogAsync(dialog); }
        finally { cancellation.Cancel(); await scan; }
        if (saved is not null) OpenWorkspace(vm.Workspaces.First(w => w.Id == saved.Id));
        else if (createManually) await EditWorkspaceAsync(null);
    }
}
