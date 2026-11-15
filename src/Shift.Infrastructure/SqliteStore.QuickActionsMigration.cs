using Microsoft.Data.Sqlite;

namespace Shift.Infrastructure;

public sealed partial class SqliteStore
{
    private static async Task MigrateQuickActionsAsync(SqliteConnection db, CancellationToken token)
    {
        // Rebuild only the parent declaration table. Child identity, layout and state remain untouched.
        // FK enforcement must be disabled outside the transaction, then explicitly checked before commit.
        await ExecuteAsync(db, null, "PRAGMA foreign_keys=OFF", token);
        try
        {
            using var tx = db.BeginTransaction();
            await ExecuteAsync(db, tx, """
                CREATE TABLE StudioDefinitions_v3(Id TEXT PRIMARY KEY,SchemaVersion INTEGER NOT NULL CHECK(SchemaVersion=1),
                  Title TEXT NOT NULL,Kind INTEGER NOT NULL CHECK(Kind BETWEEN 0 AND 4),Style TEXT NOT NULL CHECK(json_valid(Style)),
                  Content TEXT NOT NULL CHECK(json_valid(Content)),Revision INTEGER NOT NULL CHECK(Revision>0));
                INSERT INTO StudioDefinitions_v3 SELECT * FROM StudioDefinitions;
                DROP TABLE StudioDefinitions;
                ALTER TABLE StudioDefinitions_v3 RENAME TO StudioDefinitions;
                """, token);
            using var check = db.CreateCommand(); check.Transaction = tx; check.CommandText = "PRAGMA foreign_key_check";
            await using (var reader = await check.ExecuteReaderAsync(token))
                if (await reader.ReadAsync(token)) throw new InvalidDataException("Studio migration found invalid references.");
            await ExecuteAsync(db, tx, "PRAGMA user_version=3", token);
            tx.Commit();
        }
        finally { await ExecuteAsync(db, null, "PRAGMA foreign_keys=ON", CancellationToken.None); }
    }
}
