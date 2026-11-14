namespace Shift.App;

public sealed partial class MainWindow
{
    private async Task OpenScenarioAsync(Workspace workspace)
    {
        var initial = vm.Settings.WorkspaceScenarios.GetValueOrDefault(workspace.Id) ?? new LaunchScenarioOptions();
        var preflight = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { T("Без проверки (как раньше)", "No preflight (legacy behavior)"),
                T("Проверить и продолжить", "Inspect and continue"), T("Остановить при ошибках", "Stop on errors"),
                T("Пропустить элементы с ошибками", "Skip items with errors") }, SelectedIndex = (int)initial.Preflight };
        var onError = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { T("Продолжить следующие шаги", "Continue remaining steps"), T("Остановить оставшиеся шаги", "Stop remaining steps") },
            SelectedIndex = (int)initial.OnLaunchError };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(preflight, "Scenario.Preflight");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(onError, "Scenario.OnError");
        var panel = Stack(14);
        panel.Children.Add(Text(workspace.Name, 18));
        panel.Children.Add(Text(T("Настройки действуют на этом устройстве для этого пространства. Импорт и копирование пространства не включают сценарий автоматически.",
            "These local settings apply to this workspace on this device. Importing or cloning a workspace does not enable a scenario automatically."), 13, true));
        panel.Children.Add(Field(T("Перед запуском: Проверка пространства", "Before launch: Workspace Doctor"), preflight));
        panel.Children.Add(Text(T("Проверка читает локальные сведения без открытия целей и сетевых запросов. Правила остановки и пропуска относятся только к состоянию «Ошибка»; предупреждения и неизвестные результаты не гарантируют готовности приложения.",
            "Inspection reads local facts without opening targets or making network requests. Stop/skip rules apply only to Error results; warnings and unknown results do not guarantee application readiness."), 13, true));
        panel.Children.Add(Field(T("Если Windows не приняла запуск элемента", "If Windows rejects an item launch"), onError));
        panel.Children.Add(Text(T("Команды по-прежнему требуют отдельного подтверждения. Шаги идут последовательно. Остановка не закрывает уже открытые программы; повтора запуска нет. Сохранение настроек ничего не запускает.",
            "Commands still require separate consent. Steps run sequentially. Stopping does not close programs already opened; nothing is retried. Saving these settings starts nothing."), 13, true));
        var errors = new InfoBar { IsClosable = false, Severity = InfoBarSeverity.Error }; panel.Children.Add(errors);
        var dialog = Dialog(T("Сценарий запуска", "Launch scenario"), new ScrollViewer { Content = panel, MaxHeight = 440 }, s["Save"], s["Cancel"]);
        var saving = false;
        dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            saving = true; dialog.IsPrimaryButtonEnabled = false;
            var deferral = args.GetDeferral();
            try
            {
                if (vm.IsLaunching || activeOperations > 1) throw new ValidationException([s["WaitForActions"]]);
                var workspaceIds = (await store.ListAsync()).Select(w => w.Id).ToHashSet();
                if (!workspaceIds.Contains(workspace.Id))
                    throw new ValidationException([T("Пространство удалено. Закройте диалог.", "The workspace was deleted. Close this dialog.")]);
                var settings = await store.LoadSettingsAsync();
                if (vm.IsLaunching || activeOperations > 1) throw new ValidationException([s["WaitForActions"]]);
                var policies = settings.WorkspaceScenarios.Where(x => workspaceIds.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
                var policy = new LaunchScenarioOptions { Preflight = (PreflightPolicy)preflight.SelectedIndex, OnLaunchError = (LaunchErrorPolicy)onError.SelectedIndex };
                policy.Validate();
                if (policy == new LaunchScenarioOptions()) policies.Remove(workspace.Id); else policies[workspace.Id] = policy;
                var updated = settings with { WorkspaceScenarios = policies };
                await store.SaveSettingsAsync(updated);
                vm.Settings = updated;
            }
            catch (Exception ex) { args.Cancel = true; errors.Title = s["ValidationHint"]; errors.Message = s.ErrorDetails(ex); errors.IsOpen = true; }
            finally { saving = false; dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        await ShowDialogAsync(dialog);
    }
}
