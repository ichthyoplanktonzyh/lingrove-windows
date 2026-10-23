using System.IO;
using System.Security.AccessControl;
using Cotabby.Core.Vocabulary;
using Value = Cotabby.Win32.Vocabulary.VocabularySQLiteConnection.Value;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// One archive owns this relational database and its connection. Typed projection decides
/// which rows changed before encoding; SQLite owns atomic compare-and-increment acknowledgement.
/// This synchronous boundary retains Mac table order, deferred FKs and rollback. The named-file
/// factory adds Windows identity, exclusive creation and cross-process locking. No AppHost service
/// or product Save button is connected by this library.
/// </summary>
internal sealed class VocabularySQLiteDatabase : IDisposable
{
    internal const long ApplicationId = 0x4C47564E, SchemaVersion = 2;
    private readonly object _gate = new();
    private readonly VocabularySQLiteConnection _connection;
    private readonly VocabularyWindowsFileLock? _fileLock;
    private bool? _held;
    private bool _disposed;
    internal long LastWrittenRows { get; private set; }

    private VocabularySQLiteDatabase(VocabularySQLiteConnection connection, VocabularyWindowsFileLock? fileLock = null)
    { _connection = connection; _fileLock = fileLock; }
    internal static VocabularySQLiteDatabase OpenFile(string path)
    {
        if (!OperatingSystem.IsWindows()) throw Invalid();
        VocabularySQLiteConnection? connection = null; VocabularyWindowsFileLock? fileLock = null;
        try
        {
            path = VocabularyWindowsFile.FullPath(path); VocabularyWindowsFile.CreateParentDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) CreateFile(path);
            connection = VocabularySQLiteConnection.OpenNamed(path); fileLock = new(path);
            var result = new VocabularySQLiteDatabase(connection, fileLock); result.Initialize(); return result;
        }
        catch (Exception error) when (error is VocabularyException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { connection?.Dispose(); fileLock?.Dispose(); if (error is VocabularyException) throw; throw Invalid(); }
    }
    private static void CreateFile(string destination)
    {
        if (!OperatingSystem.IsWindows()) throw Invalid();
        string staged = Path.Combine(Path.GetDirectoryName(destination)!, ".vocabulary-" + Guid.NewGuid().ToString("D") + ".sqlite");
        bool created = false;
        try
        {
            using (new FileInfo(VocabularyWindowsFile.Extended(staged)).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None,
                4096, FileOptions.None, VocabularyWindowsFile.NewFileSecurity())) { created = true; }
            using (var connection = VocabularySQLiteConnection.OpenNamed(staged)) CreateSchema(connection);
            VocabularyWindowsFile.Publish(staged, destination);
        }
        finally
        {
            // Delete only our random staging names. A failed open/version/integrity check must
            // never reset the destination or remove a database another process published first.
            if (created) foreach (string suffix in new[] { "", "-journal", "-wal", "-shm" })
                try { File.Delete(staged + suffix); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
    internal static VocabularySQLiteDatabase CreateMemory()
    {
        var connection = VocabularySQLiteConnection.OpenMemory();
        try { CreateSchema(connection); var result = new VocabularySQLiteDatabase(connection); result.Initialize(); return result; }
        catch { connection.Dispose(); throw; }
    }
    // Test admission takes ownership of an isolated memory connection, including on failure.
    // Production named-file admission always uses OpenFile's identity and lock composition.
    internal static VocabularySQLiteDatabase Admit(VocabularySQLiteConnection connection)
    {
        if (connection.IsNamed) { connection.Dispose(); throw Invalid(); }
        var result = new VocabularySQLiteDatabase(connection);
        try { result.Initialize(); return result; }
        catch { result.Dispose(); throw; }
    }
    private void Initialize() => Access(true, () =>
    {
        if (_connection.Scalar("PRAGMA application_id") != ApplicationId) throw Invalid();
        long version = _connection.Scalar("PRAGMA user_version");
        if (version is not 1 and not SchemaVersion) throw new VocabularyException(VocabularyFailure.UnsupportedVersion);
        var integrity = _connection.Rows("PRAGMA quick_check");
        if (integrity is not [{ Length: 1 } check] || check[0] is not Value.Text { Data: "ok" }
            || _connection.Rows("PRAGMA foreign_key_check").Length != 0) throw Invalid();
        if (version == 1) _connection.Transaction(true, () =>
        { CreateReceipts(_connection); _connection.Execute("PRAGMA user_version=" + SchemaVersion); return true; });
        _connection.Execute("PRAGMA journal_mode=WAL");
        _connection.Execute("PRAGMA synchronous=FULL");
        return true;
    });
    internal T Access<T>(bool write, Func<T> operation)
    {
        lock (_gate)
        {
            if (_disposed) throw Invalid();
            if (_held is { } held)
            {
                if (!held && write) throw new VocabularyException(VocabularyFailure.InvalidContent);
                _connection.VerifyFile();
                return operation();
            }
            using var lease = _fileLock?.Enter(write);
            _connection.VerifyFile();
            _held = write;
            try { return operation(); }
            finally { _held = null; }
        }
    }
    internal ulong Revision() => Access(false, () =>
    {
        long value = _connection.Scalar("SELECT revision FROM state WHERE singleton=1");
        return value >= 0 ? (ulong)value : throw Invalid();
    });
    internal VocabularySnapshot Read() => Access(false, () => _connection.Transaction(false, () =>
    {
        ulong revision = Revision(); long templates = _connection.Scalar("SELECT has_templates FROM state WHERE singleton=1");
        if (templates is not 0 and not 1) throw Invalid();
        var records = new List<VocabularyDatabaseRecord>();
        foreach (var table in Enum.GetValues<VocabularyDatabaseTable>())
        {
            string[] columns = ["id", "parent_id", "position", "payload", .. table.ExtraColumns()];
            foreach (var row in _connection.Rows("SELECT " + string.Join(',', columns) + " FROM " + table.Name()))
            {
                if (row[0] is not Value.Text id || row[1] is not Value.Null and not Value.Text
                    || row[2] is not Value.Integer { Data: >= 0 and <= int.MaxValue } position
                    || row[3] is not Value.Blob bytes || row.Skip(4).Any(value => value is not Value.Text)) throw Invalid();
                VocabularyDatabasePayload payload;
                try { payload = VocabularyPayloadCodec.Decode(bytes.Data, table); } catch (VocabularyException) { throw Invalid(); }
                records.Add(new(table, id.Data, (row[1] as Value.Text)?.Data, (int)position.Data, payload,
                    row.Skip(4).Cast<Value.Text>().Select(value => value.Data).ToArray()));
            }
        }
        return new VocabularySnapshot(revision, VocabularyDatabaseProjection.Document(records, templates == 1));
    }));
    internal VocabularySQLiteReceipt? Receipt(string key) => Access(false, () =>
    {
        var rows = _connection.Rows("SELECT fingerprint,result FROM cli_receipts WHERE key=?", new Value.Text(key));
        if (rows.Length == 0) return null;
        if (rows[0] is not [Value.Text fingerprint, Value.Blob result]) throw Invalid();
        return new VocabularySQLiteReceipt(key, fingerprint.Data, result.Data);
    });
    internal ulong Write(VocabularyDocument next, VocabularyDocument previous, ulong expectedRevision, VocabularySQLiteReceipt? receipt = null) => Access(true, () =>
    {
        var oldRows = VocabularyDatabaseProjection.Rows(previous); var newRows = VocabularyDatabaseProjection.Rows(next);
        var changed = newRows.Values.Where(row => !oldRows.TryGetValue(row.Key, out var old) || old != row).ToArray();
        var removed = oldRows.Values.Where(row => !newRows.ContainsKey(row.Key)).ToArray();
        long before = _connection.TotalChanges;
        var (revision, writtenRows) = _connection.Transaction(true, () =>
        {
            if (Revision() != expectedRevision || expectedRevision >= long.MaxValue)
                throw new VocabularyException(VocabularyFailure.EditConflict);
            // A restore can give an existing lookup key to a different UUID. Release keys before
            // installing new owners, still inside the transaction so a later failure restores all.
            foreach (var row in oldRows.Values.Where(row => row.Table == VocabularyDatabaseTable.Entries))
                if (!newRows.TryGetValue(row.Key, out var replacement)
                    // Lookup keys are already lowercase ASCII SHA-256 values, not user spelling.
                    || !StringComparer.Ordinal.Equals(row.Extra[0], replacement.Extra[0]))
                    _connection.Execute("UPDATE entries SET lookup_key=NULL WHERE id=?", new Value.Text(row.Id));
            foreach (var table in Enum.GetValues<VocabularyDatabaseTable>())
                foreach (var row in changed.Where(row => row.Table == table)) Upsert(row);
            // Reparent surviving usages/cards before deleting their old owners. Otherwise an
            // ON DELETE CASCADE could erase the very rows this transaction intends to move.
            foreach (var table in Enum.GetValues<VocabularyDatabaseTable>().Reverse())
                foreach (var row in removed.Where(row => row.Table == table))
                    _connection.Execute("DELETE FROM " + table.Name() + " WHERE id=?", new Value.Text(row.Id));
            ulong result = expectedRevision + 1;
            if (receipt is not null) _connection.Execute("INSERT INTO cli_receipts (key,fingerprint,result) VALUES (?,?,?)",
                new Value.Text(receipt.Key), new Value.Text(receipt.Fingerprint), new Value.Blob(receipt.Result));
            _connection.Execute("UPDATE state SET revision=?,has_templates=? WHERE singleton=1",
                new Value.Integer((long)result), new Value.Integer(next.ReviewTemplates is null ? 0 : 1));
            // This diagnostic is fallible SQL too. Evaluate it before COMMIT so an error still
            // rolls back metadata; a media owner must never delete staged files after committed
            // metadata only because a post-COMMIT counter query failed.
            return (result, _connection.TotalChanges - before);
        });
        LastWrittenRows = writtenRows;
        return revision;
    });
    private void Upsert(VocabularyDatabaseRecord row)
    {
        string[] columns = ["id", "parent_id", "position", "payload", .. row.Table.ExtraColumns()];
        string sql = "INSERT INTO " + row.Table.Name() + " (" + string.Join(',', columns) + ") VALUES ("
            + string.Join(',', columns.Select(_ => "?")) + ") ON CONFLICT(id) DO UPDATE SET "
            + string.Join(',', columns.Skip(1).Select(column => column + "=excluded." + column));
        Value[] values = [new Value.Text(row.Id), row.ParentId is { } parent ? new Value.Text(parent) : new Value.Null(),
            new Value.Integer(row.Position), new Value.Blob(VocabularyPayloadCodec.Encode(row.Payload)), .. row.Extra.Select(value => new Value.Text(value))];
        _connection.Execute(sql, values);
    }
    internal static void CreateSchema(VocabularySQLiteConnection connection)
    {
        connection.Execute("PRAGMA synchronous=FULL");
        connection.Transaction(true, () =>
        {
            connection.Execute("CREATE TABLE state (singleton INTEGER PRIMARY KEY CHECK(singleton=1), revision INTEGER NOT NULL CHECK(revision>=0), has_templates INTEGER NOT NULL CHECK(has_templates IN (0,1)))");
            connection.Execute("INSERT INTO state VALUES (1,0,0)");
            foreach (var table in Enum.GetValues<VocabularyDatabaseTable>())
            {
                string parent = table.Parent() is { } owner ? "TEXT NOT NULL REFERENCES " + owner.Name() + "(id) ON DELETE CASCADE DEFERRABLE INITIALLY DEFERRED" : "TEXT CHECK(parent_id IS NULL)";
                string extra = table == VocabularyDatabaseTable.Entries ? ",lookup_key TEXT UNIQUE,language TEXT NOT NULL,expression TEXT NOT NULL"
                    : table == VocabularyDatabaseTable.Senses ? ",meaning TEXT NOT NULL" : "";
                connection.Execute("CREATE TABLE " + table.Name() + " (id TEXT PRIMARY KEY NOT NULL,parent_id " + parent + ",position INTEGER NOT NULL CHECK(position>=0),payload BLOB NOT NULL" + extra + ")");
                connection.Execute("CREATE INDEX " + table.Name() + "_owner ON " + table.Name() + "(parent_id,position)");
            }
            connection.Execute("CREATE INDEX entries_language ON entries(language,expression)"); CreateReceipts(connection);
            connection.Execute("PRAGMA application_id=" + ApplicationId); connection.Execute("PRAGMA user_version=" + SchemaVersion);
            return true;
        });
    }
    private static void CreateReceipts(VocabularySQLiteConnection connection) => connection.Execute("CREATE TABLE cli_receipts (key TEXT PRIMARY KEY NOT NULL, fingerprint TEXT NOT NULL, result BLOB NOT NULL)");
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _connection.Dispose(); _fileLock?.Dispose(); } }
}

/// <summary>
/// Opaque idempotency receipt travels with the same SQL commit as its graph revision. The future
/// CLI owns fingerprint/result interpretation; this value copies bytes at both boundaries so its
/// caller cannot mutate an acknowledged receipt. No CLI listener is registered by this library.
/// </summary>
internal sealed class VocabularySQLiteReceipt(string key, string fingerprint, byte[] result)
{
    private readonly byte[] _result = result.ToArray();
    internal string Key { get; } = key;
    internal string Fingerprint { get; } = fingerprint;
    internal byte[] Result => _result.ToArray();
}
