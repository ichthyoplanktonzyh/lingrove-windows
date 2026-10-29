using System.IO;
using System.Security.AccessControl;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using VocabularyDocument = Cotabby.Core.Vocabulary.Frozen.VocabularyDocument;
using VocabularyDatabaseRecord = Cotabby.Core.Vocabulary.Frozen.VocabularyDatabaseRecord;
using VocabularyDatabaseProjection = Cotabby.Core.Vocabulary.Frozen.VocabularyDatabaseProjection;
using VocabularyDatabaseTable = Cotabby.Core.Vocabulary.Frozen.VocabularyDatabaseTable;
using Value = Cotabby.Win32.Vocabulary.VocabularySQLiteConnection.Value;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// One archive owns this relational database and its connection. Typed projection decides
/// which rows changed before encoding; SQLite owns atomic compare-and-increment acknowledgement.
/// This synchronous boundary retains Mac table order, deferred FKs and rollback. The named-file
/// factory adds Windows identity, exclusive creation and cross-process locking. No AppHost service
/// or product Save button is connected by this library.
/// </summary>
internal sealed class FrozenVocabularySQLiteDatabase : IDisposable
{
    internal const long ApplicationId = 0x4C47564E, SchemaVersion = 5;
    private readonly object _gate = new();
    private readonly VocabularySQLiteConnection _connection;
    private readonly VocabularyWindowsFileLock? _fileLock;
    private bool? _held;
    private bool _disposed;
    internal long LastWrittenRows { get; private set; }

    private FrozenVocabularySQLiteDatabase(VocabularySQLiteConnection connection, VocabularyWindowsFileLock? fileLock = null)
    { _connection = connection; _fileLock = fileLock; }
    internal static FrozenVocabularySQLiteDatabase OpenFile(string path)
    {
        if (!OperatingSystem.IsWindows()) throw Invalid();
        VocabularySQLiteConnection? connection = null; VocabularyWindowsFileLock? fileLock = null;
        try
        {
            path = VocabularyWindowsFile.FullPath(path); VocabularyWindowsFile.CreateParentDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) CreateFile(path);
            connection = VocabularySQLiteConnection.OpenNamed(path); fileLock = new(path);
            var result = new FrozenVocabularySQLiteDatabase(connection, fileLock); result.Initialize(); return result;
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
    internal static FrozenVocabularySQLiteDatabase CreateMemory()
    {
        var connection = VocabularySQLiteConnection.OpenMemory();
        try { CreateSchema(connection); var result = new FrozenVocabularySQLiteDatabase(connection); result.Initialize(); return result; }
        catch { connection.Dispose(); throw; }
    }
    // Test admission takes ownership of an isolated memory connection, including on failure.
    // Production named-file admission always uses OpenFile's identity and lock composition.
    internal static FrozenVocabularySQLiteDatabase Admit(VocabularySQLiteConnection connection)
    {
        if (connection.IsNamed) { connection.Dispose(); throw Invalid(); }
        var result = new FrozenVocabularySQLiteDatabase(connection);
        try { result.Initialize(); return result; }
        catch { result.Dispose(); throw; }
    }
    private void Initialize() => Access(true, () =>
    {
        if (_connection.Scalar("PRAGMA application_id") != ApplicationId) throw Invalid();
        long version = _connection.Scalar("PRAGMA user_version");
        if (version is not 4 and not SchemaVersion) throw new VocabularyException(VocabularyFailure.UnsupportedVersion);
        var integrity = _connection.Rows("PRAGMA quick_check");
        if (integrity is not [{ Length: 1 } check] || check[0] is not Value.Text { Data: "ok" }) throw Invalid();
        // Fail at the first violation instead of accumulating every broken relationship.
        _connection.VisitRows("PRAGMA foreign_key_check", [], CancellationToken.None, null, _ => throw Invalid());
        VerifySchema(_connection, version == 4);
        if (version == 4) MigrateClassification(_connection);
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
    internal FrozenVocabularySnapshot Read(CancellationToken cancellationToken = default, long maximumBytes = VocabularyDocumentCodec.MaximumDocumentBytes) => Access(false, () => _connection.Transaction(false, () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        ulong revision = Revision(); long templates = _connection.Scalar("SELECT has_templates FROM state WHERE singleton=1");
        if (templates is not 0 and not 1) throw Invalid();
        var records = new List<VocabularyDatabaseRecord>();
        long remaining = Math.Min(maximumBytes, VocabularyDocumentCodec.MaximumDocumentBytes);
        void Account(int size)
        {
            if (size < 0 || size > remaining) throw new VocabularyException(VocabularyFailure.OversizedArchive);
            remaining -= size;
        }
        foreach (var table in Enum.GetValues<VocabularyDatabaseTable>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] columns = ["id", "parent_id", "position", "payload", .. table.ExtraColumns()];
            _connection.VisitRows("SELECT " + string.Join(',', columns) + " FROM " + table.Name(), [], cancellationToken, Account, row =>
            {
                if (row[0] is not Value.Text id || row[1] is not Value.Null and not Value.Text
                    || row[2] is not Value.Integer { Data: >= 0 and <= int.MaxValue } position
                    || row[3] is not Value.Blob bytes || row.Skip(4).Any(value => value is not Value.Text and not Value.Null)) throw Invalid();
                object payload;
                try { payload = VocabularyDatabasePayloadCodec.Decode(table, bytes.Data); } catch (VocabularyException) { throw Invalid(); }
                records.Add(new(table, id.Data, (row[1] as Value.Text)?.Data, (int)position.Data, payload,
                    row.Skip(4).Select(value => (value as Value.Text)?.Data).ToArray()));
            });
        }
        var document = VocabularyDatabaseProjection.Document(records, templates == 1);
        Cotabby.Core.Vocabulary.Frozen.VocabularyArchiveValidation.Validate(document);
        cancellationToken.ThrowIfCancellationRequested();
        return new FrozenVocabularySnapshot(revision, document);
    }));
    internal VocabularySQLiteReceipt? Receipt(string key) => Access(false, () =>
    {
        var rows = _connection.Rows("SELECT fingerprint,result FROM cli_receipts WHERE key=?", new Value.Text(key));
        if (rows.Length == 0) return null;
        if (rows[0] is not [Value.Text fingerprint, Value.Blob result]) throw Invalid();
        return new VocabularySQLiteReceipt(key, fingerprint.Data, result.Data);
    });
    internal ulong Write(VocabularyDocument next, VocabularyDocument previous, ulong expectedRevision, VocabularySQLiteReceipt? receipt = null, CancellationToken cancellationToken = default) => Access(true, () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Cotabby.Core.Vocabulary.Frozen.VocabularyArchiveValidation.Validate(next);
        var oldRows = VocabularyDatabaseProjection.Rows(previous); var newRows = VocabularyDatabaseProjection.Rows(next);
        EnsureReadBudget(newRows.Values, VocabularyDocumentCodec.MaximumDocumentBytes, cancellationToken);
        var changed = newRows.Values.Where(row => !oldRows.TryGetValue(row.Key, out var old) || old != row).ToArray();
        var removed = oldRows.Values.Where(row => !newRows.ContainsKey(row.Key)).ToArray();
        long before = _connection.TotalChanges;
        var (revision, writtenRows) = _connection.Transaction(true, () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Revision() != expectedRevision || expectedRevision >= long.MaxValue)
                throw new VocabularyException(VocabularyFailure.EditConflict);
            // A restore can give an existing lookup key to a different UUID. Release keys before
            // installing new owners, still inside the transaction so a later failure restores all.
            foreach (var row in oldRows.Values.Where(row => row.Table == VocabularyDatabaseTable.Entries))
                if (!newRows.TryGetValue(row.Key, out var replacement)
                    // Lookup keys are already lowercase ASCII SHA-256 values, not user spelling.
                    || !StringComparer.Ordinal.Equals(row.Extra[0], replacement.Extra[0]))
                    _connection.Execute("UPDATE entries SET lookup_key=NULL WHERE id=?", new Value.Text(row.Id));
            foreach (var row in oldRows.Values.Where(row => row.Table == VocabularyDatabaseTable.MeaningLabels))
                if (!newRows.TryGetValue(row.Key, out var replacement) || replacement != row)
                    _connection.Execute("UPDATE meaning_labels SET preferred='no' WHERE id=?", new Value.Text(row.Id));
            foreach (var table in Enum.GetValues<VocabularyDatabaseTable>())
                foreach (var row in changed.Where(row => row.Table == table)) { cancellationToken.ThrowIfCancellationRequested(); Upsert(row); }
            // Reparent surviving usages/cards before deleting their old owners. Otherwise an
            // ON DELETE CASCADE could erase the very rows this transaction intends to move.
            foreach (var table in Enum.GetValues<VocabularyDatabaseTable>().Reverse())
                foreach (var row in removed.Where(row => row.Table == table))
                { cancellationToken.ThrowIfCancellationRequested(); _connection.Execute("DELETE FROM " + table.Name() + " WHERE id=?", new Value.Text(row.Id)); }
            // Unchanged native rows may retain larger JSON bytes than re-encoding the same
            // value. Check the actual transaction contents as well before acknowledgement.
            CheckStoredReadBudget(VocabularyDocumentCodec.MaximumDocumentBytes, cancellationToken);
            ulong result = expectedRevision + 1;
            if (receipt is not null) _connection.Execute("INSERT INTO cli_receipts (key,fingerprint,result) VALUES (?,?,?)",
                new Value.Text(receipt.Key), new Value.Text(receipt.Fingerprint), new Value.Blob(receipt.Result));
            _connection.Execute("UPDATE state SET revision=?,has_templates=? WHERE singleton=1",
                new Value.Integer((long)result), new Value.Integer(next.ReviewTemplates is null ? 0 : 1));
            // This diagnostic is fallible SQL too. Evaluate it before COMMIT so an error still
            // rolls back metadata; a media owner must never delete staged files after committed
            // metadata only because a post-COMMIT counter query failed.
            cancellationToken.ThrowIfCancellationRequested();
            return (result, _connection.TotalChanges - before);
        });
        LastWrittenRows = writtenRows;
        return revision;
    });
    // A successful write must not create a snapshot the same adapter cannot reopen.
    internal static void EnsureReadBudget(IEnumerable<VocabularyDatabaseRecord> rows, long maximumBytes, CancellationToken cancellationToken = default)
    {
        long remaining = Math.Min(maximumBytes, VocabularyDocumentCodec.MaximumDocumentBytes);
        void Account(long amount)
        {
            if (amount < 0 || amount > remaining) throw new VocabularyException(VocabularyFailure.OversizedArchive);
            remaining -= amount;
        }
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Account(256 + (4 + row.Extra.Count) * 16);
            Account(System.Text.Encoding.UTF8.GetByteCount(row.Id));
            if (row.ParentId is { } parent) Account(System.Text.Encoding.UTF8.GetByteCount(parent));
            foreach (var extra in row.Extra) if (extra is not null) Account(System.Text.Encoding.UTF8.GetByteCount(extra));
            Account(VocabularyDatabasePayloadCodec.Encode(row).Length);
        }
    }
    internal void CheckStoredReadBudget(long maximumBytes, CancellationToken cancellationToken = default)
    {
        long remaining = Math.Min(maximumBytes, VocabularyDocumentCodec.MaximumDocumentBytes);
        void Account(long amount)
        {
            if (amount < 0 || amount > remaining) throw new VocabularyException(VocabularyFailure.OversizedArchive);
            remaining -= amount;
        }
        foreach (var table in Enum.GetValues<VocabularyDatabaseTable>())
        {
            string[] columns = ["id", "parent_id", "payload", .. table.ExtraColumns()];
            string sql = "SELECT " + string.Join(',', columns.Select(x => "length(CAST(" + x + " AS BLOB))")) + " FROM " + table.Name();
            _connection.VisitRows(sql, [], cancellationToken, null, values =>
            {
                Account(256 + (4 + table.ExtraColumns().Count) * 16);
                foreach (var value in values)
                    if (value is Value.Integer count) Account(count.Data);
                    else if (value is not Value.Null) throw Invalid();
            });
        }
    }
    private void Upsert(VocabularyDatabaseRecord row)
    {
        string[] columns = ["id", "parent_id", "position", "payload", .. row.Table.ExtraColumns()];
        string sql = "INSERT INTO " + row.Table.Name() + " (" + string.Join(',', columns) + ") VALUES ("
            + string.Join(',', columns.Select(_ => "?")) + ") ON CONFLICT(id) DO UPDATE SET "
            + string.Join(',', columns.Skip(1).Select(column => column + "=excluded." + column));
        Value[] values = [new Value.Text(row.Id), row.ParentId is { } parent ? new Value.Text(parent) : new Value.Null(),
            new Value.Integer(row.Position), new Value.Blob(VocabularyDatabasePayloadCodec.Encode(row)), .. row.Extra.Select(value => value is null ? (Value)new Value.Null() : new Value.Text(value))];
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
                string parent = table.Parent() is { } owner ? "TEXT NOT NULL REFERENCES " + owner.Name() + "(id) DEFERRABLE INITIALLY DEFERRED" : "TEXT CHECK(parent_id IS NULL)";
                string extra = ExtraSql(table);
                connection.Execute("CREATE TABLE " + table.Name() + " (id TEXT PRIMARY KEY NOT NULL,parent_id " + parent + ",position INTEGER NOT NULL CHECK(position>=0),payload BLOB NOT NULL" + extra + ")");
                connection.Execute("CREATE INDEX " + table.Name() + "_owner ON " + table.Name() + "(parent_id,position)");
            }
            connection.Execute("CREATE INDEX entries_language ON entries(language,expression)"); CreateMeaningIndexes(connection); CreateClassificationIndexes(connection); CreateReceipts(connection);
            connection.Execute("PRAGMA application_id=" + ApplicationId); connection.Execute("PRAGMA user_version=" + SchemaVersion);
            return true;
        });
    }
    // Admission compares the actual SQLite DDL to a source-shaped schema created by this same
    // adapter. Whitespace/case outside SQL string literals do not change its meaning. Reject
    // unexpected tables/indexes/triggers or missing constraints before any ordinary write.
    private static readonly VocabularyDatabaseTable[] ClassificationTables = [VocabularyDatabaseTable.ClassificationFrameworks,
        VocabularyDatabaseTable.Categories, VocabularyDatabaseTable.CategoryParents, VocabularyDatabaseTable.CategoryMemberships, VocabularyDatabaseTable.CategoryOverrides];
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ExpectedSchema = new(() =>
    {
        using var expected = VocabularySQLiteConnection.OpenMemory(); CreateSchema(expected); return Schema(expected);
    });
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ExpectedSchema4 = new(() =>
    {
        using var expected = VocabularySQLiteConnection.OpenMemory(); CreateSchema(expected);
        foreach (var table in ClassificationTables.Reverse()) expected.Execute("DROP TABLE " + table.Name());
        return Schema(expected);
    });
    private static void VerifySchema(VocabularySQLiteConnection connection, bool version4 = false)
    {
        var expected = version4 ? ExpectedSchema4.Value : ExpectedSchema.Value; var actual = Schema(connection);
        if (expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var sql) || sql != pair.Value)) throw Invalid();
    }
    private static IReadOnlyDictionary<string, string> Schema(VocabularySQLiteConnection connection)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in connection.Rows("SELECT type,name,sql FROM sqlite_master WHERE type IN ('table','index','trigger','view') AND substr(name,1,7) <> 'sqlite_'"))
        {
            if (row is not [Value.Text type, Value.Text name, Value.Text sql] || !result.TryAdd(type.Data + ":" + name.Data, NormalizeSql(sql.Data))) throw Invalid();
        }
        return result;
    }
    private static string NormalizeSql(string sql)
    {
        var result = new System.Text.StringBuilder(sql.Length); bool quoted = false;
        for (int i = 0; i < sql.Length; i++)
        {
            char value = sql[i];
            if (value == '\'')
            {
                result.Append(value);
                if (quoted && i + 1 < sql.Length && sql[i + 1] == '\'') { result.Append(sql[++i]); continue; }
                quoted = !quoted;
            }
            else if (quoted) result.Append(value);
            else if (!char.IsWhiteSpace(value)) result.Append(char.ToLowerInvariant(value));
        }
        if (quoted) throw Invalid();
        return result.ToString();
    }
    private static void MigrateClassification(VocabularySQLiteConnection connection) => connection.Transaction(true, () =>
    {
        foreach (var table in ClassificationTables)
        {
            string parent = table.Parent() is { } owner ? "TEXT NOT NULL REFERENCES " + owner.Name() + "(id) DEFERRABLE INITIALLY DEFERRED" : "TEXT CHECK(parent_id IS NULL)";
            connection.Execute("CREATE TABLE " + table.Name() + " (id TEXT PRIMARY KEY NOT NULL,parent_id " + parent +
                ",position INTEGER NOT NULL CHECK(position>=0),payload BLOB NOT NULL" + ExtraSql(table) + ")");
            connection.Execute("CREATE INDEX " + table.Name() + "_owner ON " + table.Name() + "(parent_id,position)");
        }
        CreateClassificationIndexes(connection); VerifySchema(connection); connection.Execute("PRAGMA user_version=5"); return true;
    });
    private static string ExtraSql(VocabularyDatabaseTable table)
    {
        const string d = " DEFERRABLE INITIALLY DEFERRED";
        return table switch
        {
            VocabularyDatabaseTable.Entries => ",lookup_key TEXT UNIQUE,language TEXT NOT NULL,expression TEXT NOT NULL",
            VocabularyDatabaseTable.Senses => ",meaning TEXT NOT NULL",
            VocabularyDatabaseTable.Usages => ",occurrence_id TEXT NOT NULL REFERENCES occurrences(id)" + d + ",sense_id TEXT REFERENCES senses(id)" + d + ",status TEXT NOT NULL CHECK(status IN ('candidate','accepted','rejected'))",
            VocabularyDatabaseTable.Collections => ",sense_id TEXT NOT NULL UNIQUE REFERENCES senses(id)" + d,
            VocabularyDatabaseTable.CollectionInterpretations => ",usage_id TEXT NOT NULL REFERENCES usages(id)" + d + ",UNIQUE(parent_id,usage_id)",
            VocabularyDatabaseTable.MeaningNodes => ",kind TEXT NOT NULL CHECK(kind IN ('concept','function'))",
            VocabularyDatabaseTable.MeaningLabels => ",language TEXT NOT NULL,text TEXT NOT NULL,preferred TEXT NOT NULL CHECK(preferred IN ('yes','no'))",
            VocabularyDatabaseTable.MeaningRoles or VocabularyDatabaseTable.PatternSlots => ",name TEXT NOT NULL",
            VocabularyDatabaseTable.UsagePatterns => ",template TEXT NOT NULL,UNIQUE(id,parent_id)",
            VocabularyDatabaseTable.MeaningMappings => ",meaning_id TEXT NOT NULL REFERENCES meaning_nodes(id) ON DELETE CASCADE" + d + ",pattern_id TEXT,relation TEXT NOT NULL CHECK(relation IN ('expressesContent','realizesFunction')),FOREIGN KEY(pattern_id,parent_id) REFERENCES usage_patterns(id,parent_id) ON DELETE CASCADE" + d,
            VocabularyDatabaseTable.MappingRoles => ",role_id TEXT NOT NULL REFERENCES meaning_roles(id) ON DELETE CASCADE" + d + ",slot_id TEXT NOT NULL REFERENCES pattern_slots(id) ON DELETE CASCADE" + d,
            VocabularyDatabaseTable.MappingEvidence => ",usage_id TEXT NOT NULL REFERENCES usages(id) ON DELETE CASCADE" + d,
            VocabularyDatabaseTable.MeaningRelations => ",target_id TEXT NOT NULL REFERENCES meaning_nodes(id) ON DELETE CASCADE" + d + ",relation TEXT NOT NULL CHECK(relation IN ('narrower','opposite','related')),CHECK(parent_id<>target_id)",
            VocabularyDatabaseTable.RetrievalCues => ",language TEXT NOT NULL,text TEXT NOT NULL",
            VocabularyDatabaseTable.CueTargets => ",meaning_id TEXT NOT NULL REFERENCES meaning_nodes(id) ON DELETE CASCADE" + d + ",UNIQUE(parent_id,meaning_id)",
            VocabularyDatabaseTable.ClassificationFrameworks => ",catalog_key TEXT NOT NULL UNIQUE",
            VocabularyDatabaseTable.Categories => ",framework_id TEXT REFERENCES classification_frameworks(id)" + d + ",catalog_key TEXT,axis TEXT NOT NULL CHECK(axis IN ('content','function')),UNIQUE(framework_id,catalog_key)",
            VocabularyDatabaseTable.CategoryParents => ",category_id TEXT NOT NULL REFERENCES categories(id)" + d + ",parent_category_id TEXT NOT NULL REFERENCES categories(id)" + d + ",UNIQUE(category_id,parent_category_id),CHECK(category_id<>parent_category_id)",
            VocabularyDatabaseTable.CategoryMemberships => ",meaning_id TEXT NOT NULL REFERENCES meaning_nodes(id)" + d + ",category_id TEXT NOT NULL REFERENCES categories(id)" + d + ",UNIQUE(meaning_id,category_id)",
            _ when Enum.IsDefined(table) => "", _ => throw Invalid(),
        };
    }
    private static void CreateMeaningIndexes(VocabularySQLiteConnection connection)
    {
        foreach (var sql in new[] { "CREATE INDEX usages_occurrence ON usages(occurrence_id,status)", "CREATE INDEX usages_sense ON usages(sense_id,status)",
            "CREATE UNIQUE INDEX meaning_preferred_language ON meaning_labels(parent_id,language) WHERE preferred='yes'", "CREATE INDEX meaning_label_text ON meaning_labels(language,text)",
            "CREATE INDEX meaning_mappings_node ON meaning_mappings(meaning_id,parent_id)", "CREATE INDEX meaning_relations_target ON meaning_relations(target_id)",
            "CREATE INDEX retrieval_cue_text ON retrieval_cues(language,text)", "CREATE INDEX cue_targets_meaning ON cue_targets(meaning_id)", "CREATE INDEX mapping_evidence_usage ON mapping_evidence(usage_id)" }) connection.Execute(sql);
    }
    private static void CreateClassificationIndexes(VocabularySQLiteConnection connection)
    {
        connection.Execute("CREATE INDEX category_parents_parent ON category_parents(parent_category_id)");
        connection.Execute("CREATE INDEX category_memberships_category ON category_memberships(category_id,meaning_id)");
    }
    private static void CreateReceipts(VocabularySQLiteConnection connection) => connection.Execute("CREATE TABLE cli_receipts (key TEXT PRIMARY KEY NOT NULL, fingerprint TEXT NOT NULL, result BLOB NOT NULL)");
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _connection.Dispose(); _fileLock?.Dispose(); } }
}


internal sealed record FrozenVocabularySnapshot(ulong Revision, VocabularyDocument Document);
