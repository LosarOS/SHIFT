using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;

namespace Shift.Infrastructure;

public sealed partial class SqliteStore : IStudioPresetStore
{
    /// <summary>Creates a fresh starter bundle in one transaction; never updates existing Studio data or enables Studio.</summary>
    public async Task<DesktopProfile> SavePresetAsync(StudioPresetDraft draft, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(draft);
        Require(draft.Profile is not null && draft.Definitions is not null,
            "Studio preset bundle is invalid.");
        Require(draft.Profile.Widgets is not null && draft.Profile.Wallpapers is not null,
            "Studio preset bundle is invalid.");
        // The public contract accepts read-only views over mutable lists. Snapshot before awaiting
        // so caller edits cannot turn a validated bundle into different writes halfway through.
        var profile = draft.Profile with
        {
            Widgets = draft.Profile.Widgets.ToArray(),
            Wallpapers = draft.Profile.Wallpapers.ToArray()
        };
        var definitions = draft.Definitions.ToArray();
        ValidateProfile(profile);
        Require(profile.Revision == 0 && profile.LastAppliedAt is null && profile.WorkspaceId is null
            && !profile.ApplyOnWorkspaceLaunch && !profile.ChangeWallpaperPosition && profile.Wallpapers.Count == 0,
            "A starter preset must be new and must not change Windows or automatic workspace rules.");
        Require(definitions.Length is >= 1 and <= 64 && profile.Widgets.Count > 0,
            "Studio preset bundle must contain widgets.");
        Require(definitions.All(d => d is not null), "Studio preset bundle contains a null definition.");
        foreach (var definition in definitions)
        {
            ValidateDefinition(definition);
            Require(definition.Revision == 0, "Studio preset definitions must be new.");
        }
        var identities = definitions.Select(d => d.Id).ToHashSet();
        Require(identities.Count == definitions.Length
            && identities.SetEquals(profile.Widgets.Select(w => w.DefinitionId)),
            "Studio preset widget references must match its definitions.");

        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction();
        try
        {
            foreach (var definition in definitions)
                await ExecuteAsync(db, tx, """
                    INSERT INTO StudioDefinitions(Id,SchemaVersion,Title,Kind,Style,Content,Revision)
                    VALUES($id,$schema,$title,$kind,$style,$content,1)
                    """, token, ("$id", definition.Id.ToString()), ("$schema", definition.SchemaVersion),
                    ("$title", definition.Title.Trim()), ("$kind", (int)definition.Kind),
                    ("$style", Encode(definition.Style)), ("$content", Encode(definition.Content)));
            await ExecuteAsync(db, tx, """
                INSERT INTO StudioProfiles(Id,Name,Description,WorkspaceId,AutoApply,Enabled,ChangePosition,Position,LastApplied,Revision)
                VALUES($id,$name,$description,NULL,0,$enabled,0,$position,NULL,1)
                """, token, ("$id", profile.Id.ToString()), ("$name", profile.Name.Trim()),
                ("$description", profile.Description), ("$enabled", profile.Enabled), ("$position", (int)profile.WallpaperPosition));
            for (var index = 0; index < profile.Widgets.Count; index++)
            {
                var instance = profile.Widgets[index];
                await ExecuteAsync(db, tx, """
                    INSERT INTO StudioInstances(Id,ProfileId,DefinitionId,SortOrder,Layout,Visible,Interaction)
                    VALUES($id,$profile,$definition,$order,$layout,$visible,$interaction)
                    """, token, ("$id", instance.Id.ToString()), ("$profile", profile.Id.ToString()),
                    ("$definition", instance.DefinitionId.ToString()), ("$order", index),
                    ("$layout", Encode(instance.Layout)), ("$visible", instance.Visible), ("$interaction", (int)instance.Interaction));
            }
            token.ThrowIfCancellationRequested();
            tx.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            // INSERT, never UPSERT: an ID collision must not adopt or overwrite owner records.
            throw new InvalidOperationException("Studio preset was not saved. Existing data was not overwritten.", ex);
        }
        return profile with { Name = profile.Name.Trim(), Revision = 1 };
    }
}
