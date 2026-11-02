using Shift.Application;
using Shift.Domain;
using System.Globalization;

namespace Shift.Infrastructure;

public sealed partial class SqliteStore
{
    public async Task<bool> RecordProfileAppliedAsync(Guid id, DateTimeOffset appliedAt, CancellationToken token = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("A profile id is required.", nameof(id));
        token.ThrowIfCancellationRequested();
        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction();
        using var read = db.CreateCommand(); read.Transaction = tx;
        read.CommandText = "SELECT LastApplied FROM StudioProfiles WHERE Id=$id";
        read.Parameters.AddWithValue("$id", id.ToString());
        var value = await read.ExecuteScalarAsync(token);
        if (value is null) { token.ThrowIfCancellationRequested(); tx.Commit(); return false; }
        var previous = value is string text ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture) : (DateTimeOffset?)null;
        if (previous is null || appliedAt > previous.Value)
            await ExecuteAsync(db, tx, "UPDATE StudioProfiles SET LastApplied=$at WHERE Id=$id", token,
                ("$id", id.ToString()), ("$at", appliedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
        token.ThrowIfCancellationRequested();
        tx.Commit();
        return true;
    }

    public async Task<StudioLayoutWriteResult> SaveLayoutEditsAsync(Guid profileId, IReadOnlyList<StudioLayoutEdit> edits,
        CancellationToken token = default)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("A profile id is required.", nameof(profileId));
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count > 64 || edits.Any(e => e is null || e.Expected is null || e.Expected.Id == Guid.Empty
                || e.Expected.DefinitionId == Guid.Empty || !Enum.IsDefined(e.Expected.Interaction) || !Enum.IsDefined(e.Interaction))
            || edits.Select(e => e.Expected.Id).Distinct().Count() != edits.Count)
            throw new InvalidDataException("Invalid widget layout edit list.");
        foreach (var edit in edits) { StudioValidator.Validate(edit.Expected.Layout); StudioValidator.Validate(edit.Layout); }
        token.ThrowIfCancellationRequested();
        await using var db = await OpenAsync(token);
        // One transaction compares and writes each instance. Configuration fields,
        // other instances and runtime notes/timer rows are never replaced.
        using var tx = db.BeginTransaction();
        var saved = new List<WidgetInstance>(); var conflicts = new List<Guid>(); var changed = false;
        foreach (var edit in edits)
        {
            WidgetInstance? current = null;
            using (var read = db.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT Id,DefinitionId,Layout,Visible,Interaction FROM StudioInstances WHERE Id=$id AND ProfileId=$profile";
                read.Parameters.AddWithValue("$id", edit.Expected.Id.ToString()); read.Parameters.AddWithValue("$profile", profileId.ToString());
                await using var row = await read.ExecuteReaderAsync(token);
                if (await row.ReadAsync(token)) current = new WidgetInstance { Id = Guid.Parse(row.GetString(0)),
                    DefinitionId = Guid.Parse(row.GetString(1)), Layout = Decode<WidgetLayout>(row.GetString(2)),
                    Visible = row.GetBoolean(3), Interaction = (WidgetInteraction)row.GetInt32(4) };
            }
            if (current is null || current.DefinitionId != edit.Expected.DefinitionId || current.Visible != edit.Expected.Visible)
            { conflicts.Add(edit.Expected.Id); continue; }
            // An exact prior acknowledgement is idempotent; it does not bump Revision.
            if (current.Layout == edit.Layout && current.Interaction == edit.Interaction)
            { saved.Add(current); continue; }
            if (current.Layout != edit.Expected.Layout || current.Interaction != edit.Expected.Interaction)
            { conflicts.Add(current.Id); continue; }
            await ExecuteAsync(db, tx, "UPDATE StudioInstances SET Layout=$layout,Interaction=$mode WHERE Id=$id AND ProfileId=$profile",
                token, ("$layout", Encode(edit.Layout)), ("$mode", (int)edit.Interaction),
                ("$id", current.Id.ToString()), ("$profile", profileId.ToString()));
            changed = true;
            saved.Add(current with { Layout = edit.Layout, Interaction = edit.Interaction });
        }
        if (changed) await ExecuteAsync(db, tx, "UPDATE StudioProfiles SET Revision=Revision+1 WHERE Id=$id", token, ("$id", profileId.ToString()));
        token.ThrowIfCancellationRequested();
        tx.Commit();
        return new(saved, conflicts);
    }
}
