namespace Shift.App;

public sealed partial class MainWindow
{
    private readonly IWorkspaceDoctor doctor = new WorkspaceDoctor(new LocalItemInspection());

    private async Task OpenDoctorAsync(Workspace workspace)
    {
        using var cancellation = new CancellationTokenSource();
        var content = Stack(16);
        content.Children.Add(Text(s["Doctor.Scope"], 14, true));
        var summary = Text(s["Doctor.Checking"], 19, weight: Microsoft.UI.Text.FontWeights.SemiBold);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(summary, "Doctor.Summary");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(summary, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        content.Children.Add(summary);
        var results = Stack(12);
        content.Children.Add(results);
        var dialog = Dialog(s["Doctor.Title"], new ScrollViewer { Content = content, MaxHeight = 440, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, s["Doctor.Recheck"], s["Close"]);
        Guid? edit = null;
        Task scan = Task.CompletedTask;
        async Task CheckAsync()
        {
            dialog.IsPrimaryButtonEnabled = false;
            summary.Text = s["Doctor.Checking"];
            results.Children.Clear();
            try
            {
                var report = await doctor.CheckAsync(workspace, cancellation.Token);
                if (cancellation.IsCancellationRequested) return;
                summary.Text = string.Format(s["Doctor.Summary"], report.Ready, report.Warnings, report.Errors, report.Unknown);
                if (report.Items.Count == 0)
                    results.Children.Add(Text(s["NoItemsNote"], 14, true));
                foreach (var item in report.Items)
                {
                    var card = Stack(8);
                    card.Children.Add(Text(item.Name, 17, weight: Microsoft.UI.Text.FontWeights.SemiBold));
                    card.Children.Add(Text(s["Doctor." + item.State], 12, color: item.State == DoctorState.Ready ? palette.Accent : palette.Text));
                    foreach (var finding in item.Findings)
                        card.Children.Add(Text(s[finding.Code], 13, true));
                    card.Children.Add(Button(s["Doctor.Edit"], () => { edit = item.ItemId; dialog.Hide(); }, false, "Doctor.Edit." + item.ItemId));
                    results.Children.Add(Card(card, 16));
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.Error("Workspace inspection failed", ex);
                if (!cancellation.IsCancellationRequested) summary.Text = s["Doctor.Failed"];
            }
            finally { if (!cancellation.IsCancellationRequested) dialog.IsPrimaryButtonEnabled = true; }
        }
        dialog.Opened += (_, _) => scan = CheckAsync();
        dialog.PrimaryButtonClick += (_, args) => { args.Cancel = true; if (scan.IsCompleted) scan = CheckAsync(); };
        dialog.Closing += (_, _) => cancellation.Cancel();
        try { await ShowDialogAsync(dialog); }
        finally { cancellation.Cancel(); await scan; }
        if (edit is not null && !quitRequested)
        {
            var current = vm.Workspaces.FirstOrDefault(w => w.Id == workspace.Id);
            var item = current?.Items.FirstOrDefault(i => i.Id == edit);
            if (current is not null && item is not null) await EditItemAsync(current, item);
        }
    }
}
