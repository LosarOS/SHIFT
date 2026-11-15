using Microsoft.Data.Sqlite;
using Xunit;

namespace Shift.Tests;

public class DatabaseFingerprintTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "SHIFT-fingerprint-" + Guid.NewGuid().ToString("N"));
    public DatabaseFingerprintTests() => Directory.CreateDirectory(root);
    [Fact] public void Wal_checkpoint_and_row_order_do_not_change_logical_proof_but_data_changes_do()
    {
        var path = Path.Combine(root, "live.db");
        using var writer = new SqliteConnection("Data Source=" + path + ";Pooling=False"); writer.Open();
        using var command = writer.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE Proof(Id INTEGER,Text TEXT,Data BLOB); INSERT INTO Proof VALUES(1,'Привет',X'0102'),(2,NULL,NULL)";
        command.ExecuteNonQuery();
        var before = DatabaseFingerprintAcceptance.Compute(path);
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)"; command.ExecuteNonQuery();
        Assert.Equal(before, DatabaseFingerprintAcceptance.Compute(path));
        command.CommandText = "DELETE FROM Proof; INSERT INTO Proof VALUES(2,NULL,NULL),(1,'Привет',X'0102')"; command.ExecuteNonQuery();
        Assert.Equal(before, DatabaseFingerprintAcceptance.Compute(path));
        command.CommandText = "UPDATE Proof SET Text='changed' WHERE Id=1"; command.ExecuteNonQuery();
        Assert.NotEqual(before, DatabaseFingerprintAcceptance.Compute(path));
    }
    [Fact] public void Schema_and_duplicate_rows_are_included_and_reading_does_not_write_rows()
    {
        var path = Path.Combine(root, "schema.db");
        using var writer = new SqliteConnection("Data Source=" + path + ";Pooling=False"); writer.Open();
        using var command = writer.CreateCommand(); command.CommandText = "CREATE TABLE Proof(Id INTEGER); INSERT INTO Proof VALUES(1)"; command.ExecuteNonQuery();
        var first = DatabaseFingerprintAcceptance.Compute(path);
        Assert.Equal(first, DatabaseFingerprintAcceptance.Compute(path));
        command.CommandText = "INSERT INTO Proof VALUES(1)"; command.ExecuteNonQuery();
        var duplicate = DatabaseFingerprintAcceptance.Compute(path); Assert.NotEqual(first, duplicate);
        command.CommandText = "CREATE INDEX IX_Proof ON Proof(Id)"; command.ExecuteNonQuery();
        Assert.NotEqual(duplicate, DatabaseFingerprintAcceptance.Compute(path));
        command.CommandText = "SELECT COUNT(*) FROM Proof"; Assert.Equal(2L, command.ExecuteScalar());
    }
    [Fact] public void Missing_database_is_refused_without_creation()
    {
        var path = Path.Combine(root, "absent.db");
        Assert.Throws<SqliteException>(() => DatabaseFingerprintAcceptance.Compute(path));
        Assert.False(File.Exists(path));
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
