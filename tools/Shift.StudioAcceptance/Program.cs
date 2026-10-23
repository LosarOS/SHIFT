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
    if (definitions.Count != 4 || definitions.Single(d => d.Id == Id(10)).Title != "Studio CI Clock" || definitions.Single(d => d.Id == Id(10)).Style.Foreground != "#EEDDCC") throw new InvalidOperationException("Lab save/cancel did not preserve declarations.");
    using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
    using var command = db.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM StudioWidgetState";
    if (Convert.ToInt32(command.ExecuteScalar()) != 4) throw new InvalidOperationException("Preview changed persisted state.");
    Console.WriteLine("LAB_SAVE_CANCEL_VERIFIED");
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
