using System.Text.Json;
using Shift.Domain;
using Shift.Infrastructure;

internal static class StudioQuickActionsAcceptance
{
    private static Guid Id(int n) => new($"00000000-0000-4000-8000-{n:D12}");
    public static async Task RunAsync(string stage, SqliteStore store, string data)
    {
        var marker = Path.Combine(data, "quick-action-dispatch.txt");
        if (stage == "actions-prepare")
        {
            if (File.Exists(marker)) throw new InvalidOperationException("Quick action marker already exists.");
            await store.SaveAsync(new Workspace { Id = Id(500), Name = "Quick Actions CI workspace", Items = [new WorkspaceItem {
                Name = "Explicit quick action consent", Kind = ItemKind.Command, Shell = CommandShell.PowerShell,
                Target = "[System.IO.File]::WriteAllText('" + marker.Replace("'", "''") + "','QUICK_ACTION_CONFIRMED')" }] });
            var definition = await store.SaveDefinitionAsync(new WidgetDefinition { Id = Id(501), Title = "Quick Actions CI", Kind = WidgetKind.QuickActions });
            await store.SaveProfileAsync(new DesktopProfile { Id = Id(502), Name = "Quick Actions CI profile", Widgets = [new WidgetInstance { Id = Id(503), DefinitionId = definition.Id }] });
            Console.WriteLine("QUICK_ACTION_FIXTURE_PREPARED");
        }
        else if (stage == "actions-check-editor")
        {
            var definition = (await store.ListDefinitionsAsync()).Single(d => d.Id == Id(501));
            var action = definition.Content.Actions.Single();
            if (action.Title != "CI launch reviewed workspace" || action.Kind != WidgetQuickActionKind.Workspace || action.WorkspaceId != Id(500))
                throw new InvalidOperationException("Quick Actions Lab editor did not persist the selected action.");
            if (File.Exists(marker)) throw new InvalidOperationException("Editing or preview dispatched a command.");
            Console.WriteLine("QUICK_ACTION_EDITOR_NO_DISPATCH");
        }
        else if (stage == "actions-refused")
        {
            if (File.Exists(marker)) throw new InvalidOperationException("Refused quick action executed.");
            var history = await store.QueryHistoryAsync(new());
            Console.WriteLine(JsonSerializer.Serialize(new { declined = history.Runs.Any(r => r.WorkspaceId == Id(500) && r.Status == RunStatus.Declined) }));
        }
        else if (stage == "actions-check-marker")
        {
            if (!File.Exists(marker) || await File.ReadAllTextAsync(marker) != "QUICK_ACTION_CONFIRMED")
                throw new InvalidOperationException("Confirmed Quick Action did not dispatch the owned command.");
            var history = await store.QueryHistoryAsync(new());
            if (!history.Runs.Any(r => r.WorkspaceId == Id(500) && r.Status == RunStatus.Completed)
                || !history.Runs.Any(r => r.WorkspaceId == Id(500) && r.Status == RunStatus.Declined))
                throw new InvalidOperationException("Quick Action did not preserve approved/refused command history.");
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", editor_persisted = true, restart = true, command_consent = true, actual_dispatch = true }));
        }
        else throw new InvalidOperationException("Unknown Quick Actions stage.");
    }
}
