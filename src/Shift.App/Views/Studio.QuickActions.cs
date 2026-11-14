using Microsoft.UI.Xaml.Automation;
using Shift.App.Studio;

namespace Shift.App;

public sealed partial class MainWindow
{
    private async Task RunStudioQuickActionAsync(StudioWidgetRuntime sender, WidgetQuickAction action)
    {
        if (!ReferenceEquals(studioRuntime, sender) || !studioSettings.Enabled || quitRequested) return;
        ShowAndActivate();
        if (dialogOpen || vm.IsLaunching)
        {
            ShowNotice(T("Быстрое действие не запущено", "Quick action was not started"),
                T("Закройте диалог или дождитесь текущего запуска и нажмите действие снова.", "Close the dialog or wait for the current launch, then click the action again."), true);
            return;
        }
        WidgetQuickActions.Validate(action);
        Workspace workspace;
        if (action.Kind == WidgetQuickActionKind.Workspace)
        {
            // Resolve the current saved workspace, never the stale definition's launch items.
            workspace = (await store.ListAsync()).FirstOrDefault(w => w.Id == action.WorkspaceId)
                ?? throw new InvalidOperationException(T("Пространство удалено. Измените быстрое действие в мастерской.", "The workspace was deleted. Edit the quick action in Widget Lab."));
        }
        else workspace = new Workspace { Name = action.Title, Items = [WidgetQuickActions.ToItem(action)] };
        await StartLaunchAsync(workspace);
    }

    private StackPanel QuickActionsEditor(List<WidgetQuickAction> draftActions, Action changed)
    {
        var panel = Stack(10); var rows = Stack(8);
        panel.Children.Add(Text(T("До 12 действий. Нажатие запускает обычный сценарий SHIFT; команды пространства требуют подтверждения. Предпросмотр ничего не запускает.",
            "Up to 12 actions. Clicking uses the normal SHIFT launch flow; workspace commands require consent. Preview never launches anything."), 12, true));
        var title = new TextBox { Header = T("Название действия", "Action title"), MaxLength = 100 };
        var kind = new ComboBox { Header = T("Действие", "Action"), ItemsSource = new[] { T("Пространство", "Workspace"), T("Папка", "Folder"), T("Файл", "File"), T("Ссылка HTTP(S)", "HTTP(S) URL") }, SelectedIndex = 0 };
        var available = vm.Workspaces.ToArray();
        var workspace = new ComboBox { Header = T("Пространство", "Workspace"), ItemsSource = available.Select(w => w.Name).ToArray(), SelectedIndex = available.Length > 0 ? 0 : -1 };
        var target = new TextBox { Header = T("Полный путь или URL", "Absolute path or URL"), MaxLength = 32767, Visibility = Visibility.Collapsed };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        kind.SelectionChanged += (_, _) => { workspace.Visibility = kind.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; target.Visibility = kind.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible; };
        void RenderRows()
        {
            rows.Children.Clear();
            foreach (var action in draftActions.ToArray())
            {
                var row = Stack(4);
                row.Children.Add(Text(action.Title, 14));
                row.Children.Add(Text(action.Kind == WidgetQuickActionKind.Workspace
                    ? available.FirstOrDefault(w => w.Id == action.WorkspaceId)?.Name ?? T("Пространство недоступно", "Workspace unavailable") : action.Target, 12, true));
                row.Children.Add(Button(s["Delete"], () => { draftActions.Remove(action); RenderRows(); changed(); }));
                rows.Children.Add(row);
            }
        }
        panel.Children.Add(rows); panel.Children.Add(title); panel.Children.Add(kind); panel.Children.Add(workspace); panel.Children.Add(target);
        panel.Children.Add(Button(T("Добавить действие", "Add action"), () =>
        {
            try
            {
                if (draftActions.Count >= 12) throw new InvalidOperationException(T("Можно добавить до 12 действий.", "Up to 12 actions are supported."));
                var action = new WidgetQuickAction { Title = title.Text.Trim(), Kind = (WidgetQuickActionKind)kind.SelectedIndex,
                    WorkspaceId = kind.SelectedIndex == 0 && workspace.SelectedIndex >= 0 ? available[workspace.SelectedIndex].Id : null,
                    Target = kind.SelectedIndex == 0 ? "" : target.Text.Trim() };
                WidgetQuickActions.Validate(action); draftActions.Add(action); RenderRows(); changed(); error.Text = ""; title.Text = ""; target.Text = "";
            }
            catch (Exception ex) { error.Text = s.ErrorDetails(ex); }
        }, false, "Studio.QuickActions.Add"));
        panel.Children.Add(error);
        AutomationProperties.SetAutomationId(title, "Studio.QuickActions.Title");
        AutomationProperties.SetAutomationId(kind, "Studio.QuickActions.Kind");
        AutomationProperties.SetAutomationId(workspace, "Studio.QuickActions.Workspace");
        AutomationProperties.SetAutomationId(target, "Studio.QuickActions.Target");
        RenderRows(); return panel;
    }
}
