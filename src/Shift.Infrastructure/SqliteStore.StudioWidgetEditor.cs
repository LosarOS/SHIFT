using Shift.Application;
using Shift.Domain;

namespace Shift.Infrastructure;

public sealed partial class SqliteStore : IStudioWidgetEditorStore
{
    public async Task<WidgetDefinition> SaveWidgetEditAsync(WidgetDefinition definition, DesktopProfile? targetProfile,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ValidateDefinition(definition);
        DesktopProfile? snapshot = null;
        if (targetProfile is not null)
        {
            ValidateProfile(targetProfile);
            // Keep the complete intended placement stable across awaited database operations.
            snapshot = targetProfile with { Widgets = targetProfile.Widgets.ToArray(), Wallpapers = targetProfile.Wallpapers.ToArray() };
            Require(snapshot.Widgets.Any(w => w.DefinitionId == definition.Id), "Edited widget is missing from the target profile.");
        }
        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction();
        var saved = await SaveDefinitionInTransactionAsync(db, tx, definition, token);
        if (snapshot is not null) await SaveProfileInTransactionAsync(db, tx, snapshot, token);
        token.ThrowIfCancellationRequested();
        tx.Commit();
        return saved;
    }
}
