using Microsoft.Data.Sqlite;
using Shift.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Shift.Infrastructure;

public sealed partial class SqliteStore
{
    private const string StudioSchema = """
        CREATE TABLE StudioProfiles(
          Id TEXT PRIMARY KEY,Name TEXT NOT NULL CHECK(length(Name) BETWEEN 1 AND 100),Description TEXT NOT NULL,
          WorkspaceId TEXT REFERENCES Workspaces(Id) ON DELETE SET NULL,
          AutoApply INTEGER NOT NULL CHECK(AutoApply IN (0,1)),Enabled INTEGER NOT NULL CHECK(Enabled IN (0,1)),
          ChangePosition INTEGER NOT NULL CHECK(ChangePosition IN (0,1)),Position INTEGER NOT NULL CHECK(Position BETWEEN 0 AND 5),
          LastApplied TEXT,Revision INTEGER NOT NULL CHECK(Revision>0));
        CREATE INDEX IX_StudioProfiles_Workspace ON StudioProfiles(WorkspaceId);
        CREATE UNIQUE INDEX IX_StudioProfiles_AutoApply ON StudioProfiles(WorkspaceId)
          WHERE AutoApply=1 AND Enabled=1 AND WorkspaceId IS NOT NULL;
        CREATE TABLE StudioWallpapers(ProfileId TEXT NOT NULL REFERENCES StudioProfiles(Id) ON DELETE CASCADE,
          MonitorId TEXT NOT NULL,ImagePath TEXT NOT NULL,PRIMARY KEY(ProfileId,MonitorId));
        CREATE TABLE StudioDefinitions(Id TEXT PRIMARY KEY,SchemaVersion INTEGER NOT NULL CHECK(SchemaVersion=1),
          Title TEXT NOT NULL,Kind INTEGER NOT NULL CHECK(Kind BETWEEN 0 AND 3),Style TEXT NOT NULL CHECK(json_valid(Style)),
          Content TEXT NOT NULL CHECK(json_valid(Content)),Revision INTEGER NOT NULL CHECK(Revision>0));
        CREATE TABLE StudioInstances(Id TEXT PRIMARY KEY,ProfileId TEXT NOT NULL REFERENCES StudioProfiles(Id) ON DELETE CASCADE,
          DefinitionId TEXT NOT NULL REFERENCES StudioDefinitions(Id) ON DELETE RESTRICT,SortOrder INTEGER NOT NULL,
          Layout TEXT NOT NULL CHECK(json_valid(Layout)),Visible INTEGER NOT NULL CHECK(Visible IN (0,1)),
          Interaction INTEGER NOT NULL CHECK(Interaction BETWEEN 0 AND 2));
        CREATE INDEX IX_StudioInstances_Profile ON StudioInstances(ProfileId,SortOrder);
        CREATE INDEX IX_StudioInstances_Definition ON StudioInstances(DefinitionId);
        CREATE TABLE StudioWidgetState(InstanceId TEXT PRIMARY KEY REFERENCES StudioInstances(Id) ON DELETE CASCADE,
          Notes TEXT NOT NULL CHECK(length(Notes)<=100000),Timer TEXT NOT NULL CHECK(json_valid(Timer)),Revision INTEGER NOT NULL CHECK(Revision>0));
        CREATE TABLE StudioSettings(Id INTEGER PRIMARY KEY CHECK(Id=1),
          Enabled INTEGER NOT NULL CHECK(Enabled IN (0,1)),ActiveProfileId TEXT REFERENCES StudioProfiles(Id) ON DELETE SET NULL,
          WallpaperConsent INTEGER NOT NULL CHECK(WallpaperConsent IN (0,1)),RuntimeWasRunning INTEGER NOT NULL CHECK(RuntimeWasRunning IN (0,1)),
          Failures INTEGER NOT NULL CHECK(Failures BETWEEN 0 AND 100),Palette TEXT NOT NULL);
        -- Journals outlive deleted profiles: snapshots may be the only way to restore Windows.
        CREATE TABLE StudioRecovery(Id TEXT PRIMARY KEY,ProfileId TEXT NOT NULL,CreatedAt TEXT NOT NULL,
          Status INTEGER NOT NULL CHECK(Status BETWEEN 0 AND 8),Json TEXT NOT NULL CHECK(json_valid(Json) AND length(Json)<=2097152));
        CREATE INDEX IX_StudioRecovery_Status ON StudioRecovery(Status,CreatedAt DESC);
        """;
    private static readonly JsonSerializerOptions StudioJson = new()
    {
        PropertyNameCaseInsensitive = false, MaxDepth = 16, AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, StudioJson);
    private static T Decode<T>(string json)
    {
        if (json.Length > 2_097_152) throw new InvalidDataException("Studio data exceeds its size limit.");
        try { return JsonSerializer.Deserialize<T>(json, StudioJson) ?? throw new InvalidDataException("Studio data is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("Studio data format is invalid.", ex); }
    }
    private static InvalidOperationException Conflict() => new("Studio data changed. Reload it before saving; your newer data was not overwritten.");
    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    private static void TextLimit(string? value, int max, bool required = false) => Require(value is not null && value.Length <= max
        && (!required || !string.IsNullOrWhiteSpace(value)), "Studio text is missing or exceeds its limit.");
    private static void ValidateShared(Action validation)
    {
        try { validation(); }
        catch (ValidationException ex) { throw new InvalidDataException("Studio model violates the supported declaration schema.", ex); }
    }
    private static void ValidateLayout(WidgetLayout l)
    {
        Require(l is not null, "Widget layout is missing.");
        TextLimit(l.MonitorId, 2048);
        Require(double.IsFinite(l.X) && double.IsFinite(l.Y) && l.X is >= 0 and <= 1 && l.Y is >= 0 and <= 1
            && double.IsFinite(l.Width) && double.IsFinite(l.Height) && l.Width is >= 80 and <= 2000 && l.Height is >= 60 and <= 2000,
            "Widget layout is outside supported bounds.");
        ValidateShared(() => StudioValidator.Validate(l));
    }
    private static void ValidateDefinition(WidgetDefinition d)
    {
        Require(d is not null && d.Id != Guid.Empty && d.SchemaVersion == 1 && Enum.IsDefined(d.Kind) && d.Revision >= 0, "Widget definition is invalid.");
        TextLimit(d.Title, 100, true);
        Require(d.Style is not null && d.Content is not null, "Widget style/content is missing.");
        TextLimit(d.Style.TemplateId, 100, true); TextLimit(d.Style.Background, 9, true); TextLimit(d.Style.Foreground, 9, true); TextLimit(d.Style.Accent, 9, true);
        Require(Enum.IsDefined(d.Style.Surface) && double.IsFinite(d.Style.Opacity) && d.Style.Opacity is >= .1 and <= 1
            && double.IsFinite(d.Style.FontSize) && d.Style.FontSize is >= 8 and <= 120 && double.IsFinite(d.Style.Padding) && d.Style.Padding is >= 0 and <= 100
            && double.IsFinite(d.Style.CornerRadius) && d.Style.CornerRadius is >= 0 and <= 100 && double.IsFinite(d.Style.BorderWidth) && d.Style.BorderWidth is >= 0 and <= 20,
            "Widget appearance is invalid.");
        Require(StudioValidator.SafeFonts.Contains(d.Style.FontFamily, StringComparer.Ordinal), "Unsupported widget font.");
        Require(new[] { d.Style.Background, d.Style.Foreground, d.Style.Accent }.All(c => System.Text.RegularExpressions.Regex.IsMatch(c, "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")), "Widget color is invalid.");
        TextLimit(d.Content.InitialNotes, 100000); TextLimit(d.Content.DateFormat, 100, true);
        Require(d.Content.FocusMinutes is >= 1 and <= 1440 && d.Content.UpdateIntervalSeconds is >= 1 and <= 3600, "Widget timing is invalid.");
        try { _ = DateTimeOffset.UtcNow.ToString(d.Content.DateFormat, System.Globalization.CultureInfo.InvariantCulture); }
        catch (FormatException ex) { throw new InvalidDataException("Widget date format is invalid.", ex); }
        ValidateShared(() => StudioValidator.Validate(d));
    }
    private static void ValidateProfile(DesktopProfile p)
    {
        Require(p is not null && p.Id != Guid.Empty && p.Revision >= 0 && Enum.IsDefined(p.WallpaperPosition) && p.WorkspaceId != Guid.Empty, "Studio profile is invalid.");
        TextLimit(p.Name, 100, true); TextLimit(p.Description, 2000);
        Require(p.Wallpapers is not null && p.Widgets is not null && p.Wallpapers.Count <= 32 && p.Widgets.Count <= 64, "Studio profile has too many items.");
        Require(p.Wallpapers.All(w => w is not null) && p.Widgets.All(w => w is not null), "Studio profile contains a null item.");
        Require(p.Wallpapers.Select(w => w.MonitorId).Distinct(StringComparer.Ordinal).Count() == p.Wallpapers.Count && p.Widgets.Select(w => w.Id).Distinct().Count() == p.Widgets.Count, "Studio profile contains duplicate identities.");
        foreach (var w in p.Wallpapers) { TextLimit(w.MonitorId, 2048); TextLimit(w.ImagePath, 32768, true); }
        foreach (var w in p.Widgets) { Require(w is not null && w.Id != Guid.Empty && w.DefinitionId != Guid.Empty && Enum.IsDefined(w.Interaction), "Widget instance is invalid."); ValidateLayout(w.Layout); }
        ValidateShared(() => StudioValidator.Validate(p));
    }
    private static void ValidateState(WidgetRuntimeState s)
    {
        Require(s is not null && s.InstanceId != Guid.Empty && s.Revision >= 0 && s.Timer is not null, "Widget state is invalid.");
        TextLimit(s.Notes, 100000);
        Require(double.IsFinite(s.Timer.RemainingSeconds) && s.Timer.RemainingSeconds is >= 0 and <= 86400 && (!s.Timer.Running || s.Timer.EndsAtUtc is not null), "Focus timer state is invalid.");
        ValidateShared(() => StudioValidator.Validate(s));
    }
    private static void ValidateSettings(StudioSettings s)
    {
        Require(s is not null && s.ActiveProfileId != Guid.Empty && s.ConsecutiveRuntimeFailures is >= 0 and <= 100, "Studio settings are invalid.");
        TextLimit(s.Palette, 64, true);
        ValidateShared(() => StudioValidator.Validate(s));
    }
    private static void ValidateJournal(StudioRecoveryJournal j)
    {
        Require(j is not null && j.Id != Guid.Empty && j.ProfileId != Guid.Empty && Enum.IsDefined(j.Status), "Studio journal is invalid.");
        Require(j.Before is not null && j.Intended is not null && j.AttemptedMonitorIds is not null && j.AppliedMonitorIds is not null && j.Errors is not null,
            "Studio recovery snapshot is missing.");
        foreach (var snapshot in new[] { j.Before, j.Intended })
        {
            Require(Enum.IsDefined(snapshot.Position) && snapshot.Wallpapers is not null && snapshot.Wallpapers.Count <= 32, "Studio recovery snapshot is invalid.");
            Require(snapshot.Wallpapers.All(w => w is not null), "Studio snapshot contains a null wallpaper.");
            foreach (var w in snapshot.Wallpapers) { TextLimit(w.MonitorId, 2048); TextLimit(w.ImagePath, 32768); }
        }
        Require(j.AttemptedMonitorIds.Count <= 32 && j.AppliedMonitorIds.Count <= 32 && j.Errors.Count <= 100, "Studio recovery journal exceeds its limit.");
        foreach (var id in j.AttemptedMonitorIds.Concat(j.AppliedMonitorIds)) TextLimit(id, 2048);
        foreach (var error in j.Errors) TextLimit(error, 4000);
        Require(Encode(j).Length <= 2_097_152, "Studio recovery journal exceeds its size limit.");
        ValidateShared(() => StudioValidator.Validate(j));
    }

    public async Task<IReadOnlyList<DesktopProfile>> ListProfilesAsync(CancellationToken token = default)
    {
        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction(deferred: true);
        var profiles = new List<DesktopProfile>();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Id,Name,Description,WorkspaceId,AutoApply,Enabled,ChangePosition,Position,LastApplied,Revision FROM StudioProfiles ORDER BY Name,Id";
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token)) profiles.Add(new() { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1), Description = r.GetString(2), WorkspaceId = r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)), ApplyOnWorkspaceLaunch = r.GetBoolean(4), Enabled = r.GetBoolean(5), ChangeWallpaperPosition = r.GetBoolean(6), WallpaperPosition = (WallpaperPlacement)r.GetInt32(7), LastAppliedAt = r.IsDBNull(8) ? null : DateTimeOffset.Parse(r.GetString(8)), Revision = r.GetInt64(9) });
        }
        for (var i = 0; i < profiles.Count; i++)
        {
            var p = profiles[i]; var wallpapers = new List<WallpaperAssignment>(); var widgets = new List<WidgetInstance>();
            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx; cmd.CommandText = "SELECT MonitorId,ImagePath FROM StudioWallpapers WHERE ProfileId=$id ORDER BY MonitorId"; cmd.Parameters.AddWithValue("$id", p.Id.ToString());
                await using var r = await cmd.ExecuteReaderAsync(token);
                while (await r.ReadAsync(token)) wallpapers.Add(new(r.GetString(0), r.GetString(1)));
            }
            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx; cmd.CommandText = "SELECT Id,DefinitionId,Layout,Visible,Interaction FROM StudioInstances WHERE ProfileId=$id ORDER BY SortOrder,Id"; cmd.Parameters.AddWithValue("$id", p.Id.ToString());
                await using var r = await cmd.ExecuteReaderAsync(token);
                while (await r.ReadAsync(token)) widgets.Add(new() { Id = Guid.Parse(r.GetString(0)), DefinitionId = Guid.Parse(r.GetString(1)), Layout = Decode<WidgetLayout>(r.GetString(2)), Visible = r.GetBoolean(3), Interaction = (WidgetInteraction)r.GetInt32(4) });
            }
            profiles[i] = p with { Wallpapers = wallpapers, Widgets = widgets }; ValidateProfile(profiles[i]);
        }
        tx.Commit(); return profiles;
    }
    public async Task<DesktopProfile> SaveProfileAsync(DesktopProfile profile, CancellationToken token = default)
    {
        ValidateProfile(profile);
        await using var db = await OpenAsync(token); using var tx = db.BeginTransaction();
        var saved = await SaveProfileInTransactionAsync(db, tx, profile, token);
        tx.Commit(); return saved;
    }
    private static async Task<DesktopProfile> SaveProfileInTransactionAsync(SqliteConnection db, SqliteTransaction tx,
        DesktopProfile profile, CancellationToken token)
    {
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO StudioProfiles VALUES($id,$name,$description,$workspace,$auto,$enabled,$change,$position,$last,1)
                ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name,Description=excluded.Description,WorkspaceId=excluded.WorkspaceId,
                AutoApply=excluded.AutoApply,Enabled=excluded.Enabled,ChangePosition=excluded.ChangePosition,Position=excluded.Position,
                LastApplied=excluded.LastApplied,Revision=StudioProfiles.Revision+1 WHERE StudioProfiles.Revision=$revision;
                """;
            foreach (var (key, value) in new (string, object?)[] { ("$id",profile.Id.ToString()),("$name",profile.Name.Trim()),("$description",profile.Description),("$workspace",profile.WorkspaceId?.ToString()),("$auto",profile.ApplyOnWorkspaceLaunch),("$enabled",profile.Enabled),("$change",profile.ChangeWallpaperPosition),("$position",(int)profile.WallpaperPosition),("$last",profile.LastAppliedAt?.ToString("O")),("$revision",profile.Revision) }) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
            // Existing records require CAS; a stale nonzero revision may never recreate a deleted record.
            await EnsureRevisionAsync(db, tx, "StudioProfiles", "Id", profile.Id, profile.Revision, token);
            if (profile.Enabled && profile.ApplyOnWorkspaceLaunch && profile.WorkspaceId is not null)
            {
                using var automatic = db.CreateCommand(); automatic.Transaction = tx;
                automatic.CommandText = "SELECT COUNT(*) FROM StudioProfiles WHERE WorkspaceId=$workspace AND Id<>$id AND AutoApply=1 AND Enabled=1";
                automatic.Parameters.AddWithValue("$workspace", profile.WorkspaceId.Value.ToString()); automatic.Parameters.AddWithValue("$id", profile.Id.ToString());
                if (Convert.ToInt64(await automatic.ExecuteScalarAsync(token)) != 0)
                    throw new InvalidOperationException("Another enabled profile applies automatically to this workspace. Disable its automatic application first.");
            }
            if (await cmd.ExecuteNonQueryAsync(token) != 1) throw Conflict();
        }
        await ExecuteAsync(db, tx, "DELETE FROM StudioWallpapers WHERE ProfileId=$id", token, ("$id", profile.Id.ToString()));
        foreach (var w in profile.Wallpapers) await ExecuteAsync(db, tx, "INSERT INTO StudioWallpapers VALUES($p,$m,$path)", token, ("$p", profile.Id.ToString()),("$m",w.MonitorId),("$path",w.ImagePath));
        // Do not delete/reinsert retained instances: that would cascade-delete notes/timer state.
        var retained = profile.Widgets.Select(w => w.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        var previousIds = new List<string>();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx; cmd.CommandText = "SELECT Id FROM StudioInstances WHERE ProfileId=$id"; cmd.Parameters.AddWithValue("$id",profile.Id.ToString());
            await using var r = await cmd.ExecuteReaderAsync(token); while (await r.ReadAsync(token)) previousIds.Add(r.GetString(0));
        }
        foreach (var id in previousIds.Where(id => !retained.Contains(id))) await ExecuteAsync(db,tx,"DELETE FROM StudioInstances WHERE Id=$id AND ProfileId=$p",token,("$id",id),("$p",profile.Id.ToString()));
        for (var index = 0; index < profile.Widgets.Count; index++)
        {
            var w = profile.Widgets[index];
            using var cmd = db.CreateCommand(); cmd.Transaction=tx;
            cmd.CommandText = """
                INSERT INTO StudioInstances VALUES($id,$p,$d,$order,$layout,$visible,$interaction)
                ON CONFLICT(Id) DO UPDATE SET DefinitionId=excluded.DefinitionId,SortOrder=excluded.SortOrder,Layout=excluded.Layout,
                Visible=excluded.Visible,Interaction=excluded.Interaction WHERE StudioInstances.ProfileId=excluded.ProfileId;
                """;
            foreach (var (key,value) in new (string,object)[]{("$id",w.Id.ToString()),("$p",profile.Id.ToString()),("$d",w.DefinitionId.ToString()),("$order",index),("$layout",Encode(w.Layout)),("$visible",w.Visible),("$interaction",(int)w.Interaction)}) cmd.Parameters.AddWithValue(key,value);
            if (await cmd.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Widget instance already belongs to a different profile.");
        }
        return profile with { Name = profile.Name.Trim(), Revision = profile.Revision + 1 };
    }
    private static async Task EnsureRevisionAsync(SqliteConnection db, SqliteTransaction tx, string table, string key, Guid id, long revision, CancellationToken token)
    {
        // Table/key are fixed internal constants, never user input.
        using var cmd = db.CreateCommand(); cmd.Transaction=tx; cmd.CommandText=$"SELECT Revision FROM {table} WHERE {key}=$id"; cmd.Parameters.AddWithValue("$id",id.ToString());
        var actual=await cmd.ExecuteScalarAsync(token);
        if ((actual is null && revision != 0) || (actual is not null && Convert.ToInt64(actual) != revision)) throw Conflict();
    }
    public async Task DeleteProfileAsync(Guid id, CancellationToken token = default)
    {
        await using var db=await OpenAsync(token); await ExecuteAsync(db,null,"DELETE FROM StudioProfiles WHERE Id=$id",token,("$id",id.ToString()));
    }
    public async Task<IReadOnlyList<WidgetDefinition>> ListDefinitionsAsync(CancellationToken token = default)
    {
        await using var db=await OpenAsync(token); using var cmd=db.CreateCommand(); cmd.CommandText="SELECT Id,SchemaVersion,Title,Kind,Style,Content,Revision FROM StudioDefinitions ORDER BY Title,Id";
        var list=new List<WidgetDefinition>(); await using var r=await cmd.ExecuteReaderAsync(token);
        while(await r.ReadAsync(token)) { var d=new WidgetDefinition { Id=Guid.Parse(r.GetString(0)),SchemaVersion=r.GetInt32(1),Title=r.GetString(2),Kind=(WidgetKind)r.GetInt32(3),Style=Decode<WidgetStyle>(r.GetString(4)),Content=Decode<WidgetContent>(r.GetString(5)),Revision=r.GetInt64(6) }; ValidateDefinition(d); list.Add(d); }
        return list;
    }
    public async Task<WidgetDefinition> SaveDefinitionAsync(WidgetDefinition definition, CancellationToken token = default)
    {
        ValidateDefinition(definition); await using var db=await OpenAsync(token); using var tx=db.BeginTransaction();
        var saved = await SaveDefinitionInTransactionAsync(db, tx, definition, token);
        tx.Commit(); return saved;
    }
    private static async Task<WidgetDefinition> SaveDefinitionInTransactionAsync(SqliteConnection db, SqliteTransaction tx,
        WidgetDefinition definition, CancellationToken token)
    {
        await EnsureRevisionAsync(db,tx,"StudioDefinitions","Id",definition.Id,definition.Revision,token);
        await ExecuteAsync(db,tx,"""
            INSERT INTO StudioDefinitions VALUES($id,$schema,$title,$kind,$style,$content,1)
            ON CONFLICT(Id) DO UPDATE SET SchemaVersion=excluded.SchemaVersion,Title=excluded.Title,Kind=excluded.Kind,
            Style=excluded.Style,Content=excluded.Content,Revision=StudioDefinitions.Revision+1 WHERE StudioDefinitions.Revision=$revision
            """,token,("$id",definition.Id.ToString()),("$schema",definition.SchemaVersion),("$title",definition.Title.Trim()),("$kind",(int)definition.Kind),("$style",Encode(definition.Style)),("$content",Encode(definition.Content)),("$revision",definition.Revision));
        return definition with { Title=definition.Title.Trim(),Revision=definition.Revision+1 };
    }
    public async Task DeleteDefinitionAsync(Guid id, CancellationToken token = default)
    {
        await using var db=await OpenAsync(token);
        try { await ExecuteAsync(db,null,"DELETE FROM StudioDefinitions WHERE Id=$id",token,("$id",id.ToString())); }
        catch(SqliteException ex) when(ex.SqliteErrorCode==19) { throw new InvalidOperationException("Remove this widget's instances before deleting its definition.",ex); }
    }
    public async Task<WidgetRuntimeState?> GetWidgetStateAsync(Guid instanceId, CancellationToken token = default)
    {
        await using var db=await OpenAsync(token); using var cmd=db.CreateCommand(); cmd.CommandText="SELECT Notes,Timer,Revision FROM StudioWidgetState WHERE InstanceId=$id"; cmd.Parameters.AddWithValue("$id",instanceId.ToString());
        await using var r=await cmd.ExecuteReaderAsync(token); if(!await r.ReadAsync(token)) return null;
        var state=new WidgetRuntimeState { InstanceId=instanceId,Notes=r.GetString(0),Timer=Decode<FocusTimerState>(r.GetString(1)),Revision=r.GetInt64(2) }; ValidateState(state); return state;
    }
    public async Task<WidgetRuntimeState> SaveWidgetStateAsync(WidgetRuntimeState state, CancellationToken token = default)
    {
        ValidateState(state); await using var db=await OpenAsync(token); using var tx=db.BeginTransaction();
        await EnsureRevisionAsync(db,tx,"StudioWidgetState","InstanceId",state.InstanceId,state.Revision,token);
        await ExecuteAsync(db,tx,"""
            INSERT INTO StudioWidgetState VALUES($id,$notes,$timer,1)
            ON CONFLICT(InstanceId) DO UPDATE SET Notes=excluded.Notes,Timer=excluded.Timer,Revision=StudioWidgetState.Revision+1 WHERE StudioWidgetState.Revision=$revision
            """,token,("$id",state.InstanceId.ToString()),("$notes",state.Notes),("$timer",Encode(state.Timer)),("$revision",state.Revision));
        tx.Commit(); return state with { Revision=state.Revision+1 };
    }
    public async Task<StudioSettings> LoadStudioSettingsAsync(CancellationToken token = default)
    {
        await using var db=await OpenAsync(token); using var cmd=db.CreateCommand(); cmd.CommandText="SELECT Enabled,ActiveProfileId,WallpaperConsent,RuntimeWasRunning,Failures,Palette FROM StudioSettings WHERE Id=1";
        await using var r=await cmd.ExecuteReaderAsync(token); if(!await r.ReadAsync(token)) return new();
        var s=new StudioSettings { Enabled=r.GetBoolean(0),ActiveProfileId=r.IsDBNull(1)?null:Guid.Parse(r.GetString(1)),WallpaperConsentGranted=r.GetBoolean(2),RuntimeWasRunning=r.GetBoolean(3),ConsecutiveRuntimeFailures=r.GetInt32(4),Palette=r.GetString(5) }; ValidateSettings(s); return s;
    }
    public async Task SaveStudioSettingsAsync(StudioSettings settings,CancellationToken token=default)
    {
        ValidateSettings(settings); await using var db=await OpenAsync(token);
        await ExecuteAsync(db,null,"""
            INSERT INTO StudioSettings VALUES(1,$enabled,$active,$consent,$running,$failures,$palette)
            ON CONFLICT(Id) DO UPDATE SET Enabled=excluded.Enabled,ActiveProfileId=excluded.ActiveProfileId,
            WallpaperConsent=excluded.WallpaperConsent,RuntimeWasRunning=excluded.RuntimeWasRunning,Failures=excluded.Failures,Palette=excluded.Palette
            """,token,("$enabled",settings.Enabled),("$active",settings.ActiveProfileId?.ToString()),("$consent",settings.WallpaperConsentGranted),("$running",settings.RuntimeWasRunning),("$failures",settings.ConsecutiveRuntimeFailures),("$palette",settings.Palette));
    }
    public async Task<IReadOnlyList<StudioRecoveryJournal>> ListRecoveryJournalsAsync(CancellationToken token=default)
    {
        await using var db=await OpenAsync(token); using var cmd=db.CreateCommand(); cmd.CommandText="SELECT Id,ProfileId,CreatedAt,Status,Json FROM StudioRecovery ORDER BY CreatedAt DESC,Id DESC";
        var list=new List<StudioRecoveryJournal>(); await using var r=await cmd.ExecuteReaderAsync(token);
        while(await r.ReadAsync(token)) { var j=Decode<StudioRecoveryJournal>(r.GetString(4)); ValidateJournal(j); Require(j.Id.ToString()==r.GetString(0) && j.ProfileId.ToString()==r.GetString(1) && j.CreatedAt.ToString("O")==r.GetString(2) && (int)j.Status==r.GetInt32(3),"Studio journal metadata differs from its snapshot."); list.Add(j); }
        return list;
    }
    public async Task SaveRecoveryJournalAsync(StudioRecoveryJournal journal,CancellationToken token=default)
    {
        ValidateJournal(journal); await using var db=await OpenAsync(token);
        await ExecuteAsync(db,null,"""
            INSERT INTO StudioRecovery VALUES($id,$profile,$created,$status,$json)
            ON CONFLICT(Id) DO UPDATE SET Status=excluded.Status,Json=excluded.Json
            WHERE StudioRecovery.ProfileId=excluded.ProfileId AND StudioRecovery.CreatedAt=excluded.CreatedAt
            """,token,("$id",journal.Id.ToString()),("$profile",journal.ProfileId.ToString()),("$created",journal.CreatedAt.ToString("O")),("$status",(int)journal.Status),("$json",Encode(journal)));
        // Verify a caller cannot silently repurpose an existing durable operation ID.
        using var verify=db.CreateCommand(); verify.CommandText="SELECT Json FROM StudioRecovery WHERE Id=$id";verify.Parameters.AddWithValue("$id",journal.Id.ToString());
        Require((string?)await verify.ExecuteScalarAsync(token)==Encode(journal),"Studio journal identity cannot be changed.");
    }
    private async Task ValidateStudioBackupAsync(CancellationToken token)
    {
        await using var db = await OpenAsync(token);
        foreach (var sql in new[] {
            "SELECT Id,Name,Description,WorkspaceId,AutoApply,Enabled,ChangePosition,Position,LastApplied,Revision FROM StudioProfiles LIMIT 0",
            "SELECT ProfileId,MonitorId,ImagePath FROM StudioWallpapers LIMIT 0",
            "SELECT Id,SchemaVersion,Title,Kind,Style,Content,Revision FROM StudioDefinitions LIMIT 0",
            "SELECT Id,ProfileId,DefinitionId,SortOrder,Layout,Visible,Interaction FROM StudioInstances LIMIT 0",
            "SELECT InstanceId,Notes,Timer,Revision FROM StudioWidgetState LIMIT 0",
            "SELECT Id,Enabled,ActiveProfileId,WallpaperConsent,RuntimeWasRunning,Failures,Palette FROM StudioSettings LIMIT 0",
            "SELECT Id,ProfileId,CreatedAt,Status,Json FROM StudioRecovery LIMIT 0" })
        {
            using var shape = db.CreateCommand(); shape.CommandText = sql;
            await using var reader = await shape.ExecuteReaderAsync(token);
        }
        var definitions = await ListDefinitionsAsync(token); var profiles = await ListProfilesAsync(token);
        var settings = await LoadStudioSettingsAsync(token); await ListRecoveryJournalsAsync(token);
        var workspaceIds = (await ListAsync(token)).Select(w => w.Id).ToHashSet();
        Require(profiles.All(p => p.WorkspaceId is null || workspaceIds.Contains(p.WorkspaceId.Value)), "Studio backup references a missing workspace.");
        Require(settings.ActiveProfileId is null || profiles.Any(p => p.Id == settings.ActiveProfileId), "Studio backup references a missing active profile.");
        var instances = profiles.SelectMany(p => p.Widgets).ToArray();
        Require(instances.All(i => definitions.Any(d => d.Id == i.DefinitionId)), "Studio backup references a missing definition.");
        using var states = db.CreateCommand(); states.CommandText = "SELECT InstanceId FROM StudioWidgetState";
        var stateIds = new List<Guid>();
        await using (var reader = await states.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) stateIds.Add(Guid.Parse(reader.GetString(0)));
        Require(stateIds.All(id => instances.Any(i => i.Id == id)), "Studio backup contains orphan widget state.");
        foreach (var id in stateIds) await GetWidgetStateAsync(id, token);
    }
}
