using System.Text.Json;
using Microsoft.Data.Sqlite;
using Shift.Domain;
using Shift.Application;
using Shift.Infrastructure;

// Test fixture preparation/inspection only. Never callable on a non-disposable profile.
if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Disposable Windows CI only.");
if (args.Length < 3 || !Guid.TryParse(args[2], out var owner)) throw new ArgumentException("stage data-root owner required");
var stage = args[0]; var data = Path.GetFullPath(args[1]);
if (!File.Exists(Path.Combine(data, ".studio-ci-owner")) || File.ReadAllText(Path.Combine(data, ".studio-ci-owner")).Trim() != owner.ToString()) throw new InvalidOperationException("Unowned test data refused.");
var store = new SqliteStore(Path.Combine(data, "shift.db"));
if (stage.StartsWith("sharing-", StringComparison.Ordinal))
{
    await StudioSharingAcceptance.RunAsync(stage, store, data);
    return;
}
var json = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
var proofPath = Path.Combine(data, "studio-ci.json");
var wallpaper = new WindowsWallpaperService();
var imageLibrary = new WindowsStudioImageLibrary(Path.Combine(data, "Studio", "Images"));
Guid Id(int n) => new($"00000000-0000-4000-8000-{n:D12}");
if (stage == "seed")
{
    if (File.Exists(store.DatabasePath)) throw new InvalidOperationException("Refusing existing test database.");
    var upgraded = false;
    if (args.Length > 3)
    {
        var old = Path.GetFullPath(args[3]); var oldOwner = Path.Combine(Path.GetDirectoryName(old)!, ".shift-ci-owner");
        if (!File.Exists(oldOwner) || File.ReadAllText(oldOwner).Trim() != owner.ToString()) throw new InvalidOperationException("RC2 source profile is not owned.");
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = old, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.DatabasePath, Pooling = false }.ToString());
        source.Open(); destination.Open(); using var command = source.CreateCommand(); command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(command.ExecuteScalar()) != 1) throw new InvalidOperationException("Actual RC2 profile must have schema1.");
        source.BackupDatabase(destination); upgraded = true;
    }
    await store.InitializeAsync();
    if (upgraded && (!(await store.ListAsync()).Any(w => w.Name == "SHIFT smoke workspace") || !(await store.HistoryAsync()).Any())) throw new InvalidOperationException("RC2 workspace/history was lost.");
    await store.SaveSettingsAsync(new AppSettings { OnboardingComplete = true, Language = "en", Theme = AppTheme.Midnight, MinimizeToTray = false, Notifications = false });
    var definitions = new List<WidgetDefinition>();
    foreach (var kind in Enum.GetValues<WidgetKind>()) definitions.Add(await store.SaveDefinitionAsync(new WidgetDefinition { Id = Id(10 + (int)kind), Title = "Studio CI " + kind,
        Kind = kind, Style = StudioTemplates.For("Midnight") with { Opacity = .85, Surface = WidgetSurface.Translucent }, Content = new() { ShowSeconds = true, InitialNotes = "Initial CI note", FocusMinutes = 1, UpdateIntervalSeconds = 1 } }));
    // A valid declaration beyond the old editor's bounds must survive an unrelated edit.
    var boundary = await store.SaveDefinitionAsync(new WidgetDefinition { Id = Id(14), Title = "Studio CI preserved", Kind = WidgetKind.Notes,
        Style = StudioTemplates.For("Midnight") with { TemplateId = "Custom CI", CornerRadius = 64, Padding = 64, BorderWidth = 8, FontFamily = "Cascadia Mono", FontSize = 32, Shadow = false },
        Content = new() { InitialNotes = new string('Я', 20000), FocusMinutes = 1440, UpdateIntervalSeconds = 3600 } });
    await store.SaveProfileAsync(new DesktopProfile { Id = Id(199), Name = "Studio CI editor preservation",
        Widgets = [new WidgetInstance { Id = Id(19900), DefinitionId = boundary.Id, Visible = false,
            Layout = new() { Width = 1600, Height = 1200, MonitorId = "temporarily-disconnected-ci-display", X = .3, Y = .4 } }] });
    var monitors = await new WindowsMonitorTopology().GetMonitorsAsync(); if (monitors.Count == 0) throw new InvalidOperationException("No Windows monitor.");
    WallpaperSnapshot? original = null; string? limitation = null;
    try { original = await wallpaper.CaptureAsync(); if (!original.CanRestore) limitation = original.Limitation; }
    catch (Exception ex) { limitation = ex.GetType().Name + ": " + ex.Message; }
    var png = Path.Combine(data, "wallpaper-ci.png"); await File.WriteAllBytesAsync(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAIAAAD8GO2jAAAAKklEQVR4nGNQMHCgKWIYtWDUglELRi0YtWDUglELRi0YtWDUglELhooFAFi8QB/+7vg+AAAAAElFTkSuQmCC"));
    var managed = await imageLibrary.ImportAsync(png);
    foreach (var count in new[] { 1, 4, 5, 10 })
    {
        var profile = new DesktopProfile { Id = Id(100 + count), Name = "Studio CI " + count,
            Widgets = Enumerable.Range(0, count).Select(i => new WidgetInstance { Id = Id(count * 1000 + i), DefinitionId = definitions[i % 4].Id,
                Layout = new WidgetLayout { MonitorId = monitors[0].DeviceId, Width = 280, Height = 210, X = count == 4 ? .15 + (i % 2) * .48 : (i % 4) * .24, Y = count == 4 ? .04 + (i / 2) * .48 : (i / 4) * .32 } }).ToArray() };
        if (count == 4 && original?.CanRestore == true) profile = profile with { Wallpapers = original.Wallpapers.Select(w => new WallpaperAssignment(w.MonitorId, managed)).ToArray(), ChangeWallpaperPosition = true, WallpaperPosition = WallpaperPlacement.Fill };
        await store.SaveProfileAsync(profile);
        foreach (var instance in profile.Widgets.Where(w => w.DefinitionId == definitions[(int)WidgetKind.FocusTimer].Id))
            await store.SaveWidgetStateAsync(new WidgetRuntimeState { InstanceId = instance.Id, Timer = new() { RemainingSeconds = 3 } });
    }
    var provider = new WindowsSystemMetricsProvider(); var first = provider.Sample(); await Task.Delay(1100); var metrics = provider.Sample();
    await File.WriteAllTextAsync(proofPath, JsonSerializer.Serialize(new { source_commit = Environment.GetEnvironmentVariable("GITHUB_SHA"), upgraded_from_actual_rc2 = upgraded, original, wallpaper_eligible = original?.CanRestore == true, limitation, metrics, monitor_count = monitors.Count }, json));
    Console.WriteLine(proofPath);
}
else if (stage == "lab-check")
{
    var definitions = await store.ListDefinitionsAsync();
    if (definitions.Count != 5 || definitions.Single(d => d.Id == Id(10)).Title != "Studio CI Clock" || definitions.Single(d => d.Id == Id(10)).Style.Foreground != "#EEDDCC") throw new InvalidOperationException("Lab save/cancel did not preserve declarations.");
    using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
    using var command = db.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM StudioWidgetState";
    if (Convert.ToInt32(command.ExecuteScalar()) != 4) throw new InvalidOperationException("Preview changed persisted state.");
    Console.WriteLine("LAB_SAVE_CANCEL_VERIFIED");
}
else if (stage is "lab-style-cancel-check" or "lab-style-save-check")
{
    var definitions = await store.ListDefinitionsAsync();
    var source = definitions.Single(d => d.Id == Id(14));
    var created = definitions.SingleOrDefault(d => d.Title == "RC5 Reused style");
    if ((await store.ListProfilesAsync()).Count != 5 || (await store.LoadStudioSettingsAsync()).ActiveProfileId is not null)
        throw new InvalidOperationException("Style editing changed profile data or active environment.");
    if (stage == "lab-style-cancel-check")
    {
        if (definitions.Count != 5 || created is not null) throw new InvalidOperationException("Style copy cancel persisted a draft.");
    }
    else if (definitions.Count != 6 || created is null || created.Kind != WidgetKind.Clock || created.Style != source.Style
        || created.Content.InitialNotes.Length != 0 || created.Content.FocusMinutes != 25 || created.Revision != 1)
        throw new InvalidOperationException("Full custom appearance was not saved independently from source content and identity.");
    Console.WriteLine("WIDGET_APPEARANCE_COPY_VERIFIED");
}
else if (stage == "inspect")
{
    var profiles = await store.ListProfilesAsync();
    var note = await store.GetWidgetStateAsync(Id(4003));
    var timer = await store.GetWidgetStateAsync(Id(4002));
    if (note?.Notes != "Native CI note — Unicode") throw new InvalidOperationException("Native note edit was not preserved.");
    if (timer?.Timer.CompletionNotified != true) throw new InvalidOperationException("Native timer completion was not saved.");
    var journals = await store.ListRecoveryJournalsAsync();
    var windowsJournal = journals.LastOrDefault(j => j.ProfileId == Id(104) && j.AttemptedMonitorIds.Count > 0);
    Console.WriteLine(JsonSerializer.Serialize(new { note_saved = true, timer_completed = true, profiles = profiles.Count, wallpaper_journal_restored = windowsJournal?.Status == StudioApplyStatus.Restored }, json));
}
else if (stage == "mode-check")
{
    var instance = (await store.ListProfilesAsync()).Single(p => p.Id == Id(104)).Widgets.Single(w => w.Id == Id(4000));
    var bounds = WidgetLayoutService.Resolve(instance.Layout, await new WindowsMonitorTopology().GetMonitorsAsync());
    if (instance.Interaction != WidgetInteraction.Desktop || Math.Abs(bounds.X - 80) > 2 || Math.Abs(bounds.Y - 90) > 2 || Math.Abs(bounds.Width - 300) > 2 || Math.Abs(bounds.Height - 220) > 2)
        throw new InvalidOperationException("Mode change lost the pending native geometry.");
    Console.WriteLine("MODE_AND_GEOMETRY_SAVED");
}
else if (stage == "gather-check")
{
    var instance = (await store.ListProfilesAsync()).Single(p => p.Id == Id(104)).Widgets.Single(w => w.Id == Id(4000));
    var topology = await new WindowsMonitorTopology().GetMonitorsAsync(); var primary = topology.FirstOrDefault(m => m.Primary) ?? topology[0];
    var actual = WidgetLayoutService.Resolve(instance.Layout, topology);
    var expected = WidgetLayoutService.Resolve(instance.Layout with { MonitorId = primary.DeviceId, X = .04, Y = .04 }, topology);
    if (instance.Layout.MonitorId != primary.DeviceId || Math.Abs(actual.X - expected.X) > 2 || Math.Abs(actual.Y - expected.Y) > 2) throw new InvalidOperationException("Gather did not persist native recovery geometry.");
    Console.WriteLine("GATHER_GEOMETRY_PERSISTED");
}
else if (stage == "keyboard-note-check")
{
    if (args.Length < 4 || (await store.GetWidgetStateAsync(Id(4003)))?.Notes != args[3])
        throw new InvalidOperationException("Widget keyboard save did not preserve the expected note text.");
    Console.WriteLine("WIDGET_KEYBOARD_NOTE_SAVED");
}
else if (stage == "timer-restart-check")
{
    var timer = (await store.GetWidgetStateAsync(Id(5002)))?.Timer;
    if (timer?.Running != true || timer.CompletionNotified || timer.RemainingSeconds < 30) throw new InvalidOperationException("Completed native timer did not restart.");
    Console.WriteLine("TIMER_RESTARTED");
}
else if (stage == "editor-preserve-check")
{
    var definition = (await store.ListDefinitionsAsync()).Single(d => d.Id == Id(14));
    var instance = (await store.ListProfilesAsync()).Single(p => p.Id == Id(199)).Widgets.Single();
    if (definition.Style.TemplateId != "Custom CI" || definition.Style.CornerRadius != 64 || definition.Style.Padding != 64 || definition.Style.BorderWidth != 8 || definition.Style.FontFamily != "Cascadia Mono" || definition.Style.FontSize != 32 || definition.Style.Shadow || definition.Content.FocusMinutes != 1440 || definition.Content.UpdateIntervalSeconds != 3600 || definition.Content.InitialNotes != new string('Я', 20000)
        || instance.Layout.Width != 1600 || instance.Layout.Height != 1200 || instance.Layout.MonitorId != "temporarily-disconnected-ci-display" || instance.Visible)
        throw new InvalidOperationException("Editing changed valid boundary values or the unavailable display association.");
    Console.WriteLine("EDITOR_VALUES_PRESERVED");
}
else if (stage == "preset-cancel-check")
{
    if ((await store.ListProfilesAsync()).Count != 5 || (await store.ListDefinitionsAsync()).Count != 6) throw new InvalidOperationException("Preset cancel wrote data.");
    Console.WriteLine("PRESET_CANCEL_READONLY");
}
else if (stage == "preset-check")
{
    var profiles = await store.ListProfilesAsync(); var definitions = await store.ListDefinitionsAsync();
    var created = profiles.Single(p => p.Name == "RC4 Фокус");
    if (profiles.Count != 6 || definitions.Count != 9 || created.Wallpapers.Count != 0 || created.ApplyOnWorkspaceLaunch || created.WorkspaceId is not null || created.Widgets.Count != 3
        || (await store.LoadStudioSettingsAsync()).ActiveProfileId != Id(110)) throw new InvalidOperationException("Starter preset changed the active environment or was not saved completely.");
    Console.WriteLine(JsonSerializer.Serialize(new { profile_id = created.Id, widget_count = created.Widgets.Count, no_automatic_apply = true }, json));
}
else if (stage == "quick-check")
{
    using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(data, "quick-setup-ci.json")));
    var expected = fixture.RootElement.GetProperty("targetPaths").EnumerateArray().Select(x => x.GetString()).ToArray();
    var workspaces = await store.ListAsync(); var created = workspaces.Single(w => w.Name == fixture.RootElement.GetProperty("expectedName").GetString());
    if (created.Items.Count != 2 || !created.Items.OrderBy(i => i.Order).Select(i => i.Target).SequenceEqual(expected)
        || created.Items.Any(i => i.Kind != ItemKind.App || i.Arguments.Length != 0) || workspaces.Any(w => w.Name == fixture.RootElement.GetProperty("cancelledName").GetString()))
        throw new InvalidOperationException("Quick setup save/order/cancel did not match explicit choices.");
    Console.WriteLine("QUICK_SETUP_ORDER_SAVE_CANCEL_VERIFIED");
}
else if (stage == "cleanup" || stage == "verify-wallpaper")
{
    using var proof = JsonDocument.Parse(await File.ReadAllTextAsync(proofPath));
    if (!proof.RootElement.GetProperty("wallpaper_eligible").GetBoolean()) { Console.WriteLine("WALLPAPER_BLOCKED"); return; }
    var original = proof.RootElement.GetProperty("original").Deserialize<WallpaperSnapshot>(json)!;
    var current = await wallpaper.CaptureAsync();
    foreach (var before in original.Wallpapers)
    {
        var actual = current.Wallpapers.Single(w => w.MonitorId == before.MonitorId);
        if (string.Equals(actual.ImagePath, before.ImagePath, StringComparison.OrdinalIgnoreCase)) continue;
        if (stage == "verify-wallpaper") throw new InvalidOperationException("UI recovery did not restore wallpaper.");
        if (!Path.GetFullPath(actual.ImagePath).StartsWith(Path.GetFullPath(Path.Combine(data, "Studio", "Images")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Cleanup refuses a later unowned wallpaper change.");
        await wallpaper.SetWallpaperAsync(before);
    }
    if (current.Position != original.Position)
    {
        if (stage == "verify-wallpaper") throw new InvalidOperationException("UI recovery did not restore placement.");
        if (current.Position != WallpaperPlacement.Fill) throw new InvalidOperationException("Cleanup refuses a later placement change.");
        await wallpaper.SetPositionAsync(original.Position);
    }
    var after = await wallpaper.CaptureAsync();
    if (after.Position != original.Position || original.Wallpapers.Any(w => !after.Wallpapers.Any(a => a.MonitorId == w.MonitorId && string.Equals(a.ImagePath, w.ImagePath, StringComparison.OrdinalIgnoreCase)))) throw new InvalidOperationException("Wallpaper restoration check failed.");
    Console.WriteLine("WALLPAPER_RESTORED");
}
else throw new ArgumentException("Unknown CI stage");
