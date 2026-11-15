using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

/// <summary>Logical data proof for CI. Physical WAL/checkpoint bytes are deliberately not compared.</summary>
public static class DatabaseFingerprintAcceptance
{
    public static string Compute(string path)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        using (var queryOnly = db.CreateCommand()) { queryOnly.CommandText = "PRAGMA query_only=ON"; queryOnly.ExecuteNonQuery(); }
        // Deferred read transaction supplies one consistent committed snapshot, including WAL.
        using var transaction = db.BeginTransaction(deferred: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
        }
        using (var version = db.CreateCommand())
        {
            version.Transaction = transaction; version.CommandText = "PRAGMA user_version";
            Append(Convert.ToString(version.ExecuteScalar(), CultureInfo.InvariantCulture)!);
        }
        var tables = new List<string>();
        using (var schema = db.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = "SELECT type,name,tbl_name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name";
            using var reader = schema.ExecuteReader();
            while (reader.Read())
            {
                for (var i = 0; i < 4; i++) Append(reader.IsDBNull(i) ? "N" : "S" + reader.GetString(i));
                if (reader.GetString(0) == "table") tables.Add(reader.GetString(1));
            }
        }
        foreach (var table in tables.Order(StringComparer.Ordinal))
        {
            Append(table);
            using var command = db.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\"";
            var rows = new List<string>();
            using (var reader = command.ExecuteReader())
            {
                for (var i = 0; i < reader.FieldCount; i++) Append(reader.GetName(i));
                while (reader.Read())
                {
                    using var row = new MemoryStream();
                    using (var writer = new BinaryWriter(row, Encoding.UTF8, leaveOpen: true))
                    {
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            var value = reader.GetValue(i);
                            switch (value)
                            {
                                case DBNull: writer.Write((byte)0); break;
                                case long integer: writer.Write((byte)1); writer.Write(integer); break;
                                case double real: writer.Write((byte)2); writer.Write(real); break;
                                case string text: writer.Write((byte)3); writer.Write(text); break;
                                case byte[] blob: writer.Write((byte)4); writer.Write(blob.Length); writer.Write(blob); break;
                                default: throw new InvalidDataException("Unexpected SQLite storage type.");
                            }
                        }
                    }
                    rows.Add(Convert.ToHexString(SHA256.HashData(row.ToArray())));
                }
            }
            Append(rows.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var row in rows.Order(StringComparer.Ordinal)) Append(row);
        }
        transaction.Commit();
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
