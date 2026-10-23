using System.Text;
using Cotabby.Core.Vocabulary;
using SQLitePCL;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Archive-owned synchronous native SQLite boundary adapted from the Mac connection. Statements
/// never escape or survive an await. SQLitePCL owns safe native handles; borrowed column spans are
/// copied before stepping/finalizing. A named connection retains the Windows regular-file identity
/// lease until SQLite closes; its database owner adds a cross-process metadata/media lock.
/// </summary>
internal sealed class VocabularySQLiteConnection : IDisposable
{
    internal const int MaximumValueBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Lazy<bool> Provider = new(() => { Batteries_V2.Init(); return true; });
    private sqlite3? _handle;
    private readonly VocabularyWindowsFile? _file;

    internal abstract record Value
    {
        private protected Value() { }
        internal sealed record Text(string Data) : Value;
        internal sealed record Integer(long Data) : Value;
        internal sealed record Blob(byte[] Data) : Value;
        internal sealed record Null : Value;
    }

    private VocabularySQLiteConnection(sqlite3 handle, VocabularyWindowsFile? file) { _handle = handle; _file = file; }
    internal static VocabularySQLiteConnection OpenMemory() => Open(":memory:", null);
    internal static VocabularySQLiteConnection OpenNamed(string path)
    {
        var file = VocabularyWindowsFile.Open(path);
        try { return Open(file.Path, file); } catch { file.Dispose(); throw; }
    }
    private static VocabularySQLiteConnection Open(string path, VocabularyWindowsFile? file)
    {
        _ = Provider.Value;
        _ = Utf8.GetByteCount(path);
        // SQLITE_OPEN_NOFOLLOW (0x01000000) supplements the retained Windows handle; the Win32
        // leaf/ID checks are still required because VFS handling of this flag is platform-specific.
        int flags = raw.SQLITE_OPEN_READWRITE | raw.SQLITE_OPEN_FULLMUTEX | 0x01000000;
        if (file is null) flags |= raw.SQLITE_OPEN_CREATE;
        // SQLite's default Windows VFS has its own short path budget even for \\?\ paths.
        // Select the registered long-path VFS with normal locking, never the nolock variant.
        int status = raw.sqlite3_open_v2(path, out var handle, flags, file is null ? null : "win32-longpath");
        if (status != raw.SQLITE_OK || handle is null)
        { handle?.Dispose(); throw Invalid(); }
        var connection = new VocabularySQLiteConnection(handle, file);
        try
        {
            Check(raw.sqlite3_busy_timeout(handle, 2000));
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_LENGTH, MaximumValueBytes);
            connection.VerifyFile();
            connection.Execute("PRAGMA foreign_keys=ON");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    internal long TotalChanges => Scalar("SELECT total_changes()");
    internal bool IsNamed => _file is not null;
    internal void VerifyFile() => _file?.Verify();
    internal long Scalar(string sql) => Rows(sql) is [{ Length: > 0 } row] && row[0] is Value.Integer value ? value.Data : throw Invalid();
    internal T Transaction<T>(bool write, Func<T> body)
    {
        VerifyFile(); Execute(write ? "BEGIN IMMEDIATE" : "BEGIN");
        try { T result = body(); VerifyFile(); Execute("COMMIT"); return result; }
        catch { try { Execute("ROLLBACK"); } catch (VocabularyException) { } throw; }
    }
    internal void Execute(string sql, params Value[] values) => _ = Rows(sql, values);
    internal Value[][] Rows(string sql, params Value[] values)
    {
        var handle = _handle ?? throw Invalid();
        int status = raw.sqlite3_prepare_v2(handle, sql, out var statement);
        // A failed prepare may still return a handle. Dispose it on every path, including bind
        // and step failures; native buffers cannot outlive their owner or a transaction rollback.
        using (statement)
        {
            Check(status);
            if (statement is null || statement.IsInvalid) throw Invalid();
            if (raw.sqlite3_bind_parameter_count(statement) != values.Length)
                throw new VocabularyException(VocabularyFailure.InvalidContent);
            try
            {
                for (int index = 0; index < values.Length; index++) Bind(statement, index + 1, values[index]);
                var rows = new List<Value[]>();
                while (true)
                {
                    status = raw.sqlite3_step(statement);
                    if (status == raw.SQLITE_DONE) return rows.ToArray();
                    Check(status);
                    if (status != raw.SQLITE_ROW) throw Invalid();
                    var row = new Value[raw.sqlite3_column_count(statement)];
                    for (int index = 0; index < row.Length; index++)
                    {
                        // Inspect the original SQL type before requesting any conversion. TEXT
                        // is read with its explicit byte count, preserving embedded NULs and
                        // rejecting malformed UTF-8 rather than manufacturing replacement text.
                        row[index] = raw.sqlite3_column_type(statement, index) switch
                        {
                            raw.SQLITE_NULL => new Value.Null(),
                            raw.SQLITE_INTEGER => new Value.Integer(raw.sqlite3_column_int64(statement, index)),
                            raw.SQLITE_TEXT => new Value.Text(Utf8.GetString(raw.sqlite3_column_blob(statement, index))),
                            raw.SQLITE_BLOB => new Value.Blob(raw.sqlite3_column_blob(statement, index).ToArray()),
                            _ => throw Invalid(),
                        };
                    }
                    rows.Add(row);
                }
            }
            catch (ArgumentException) { throw Invalid(); }
        }
    }
    private static void Bind(sqlite3_stmt statement, int index, Value value)
    {
        int status;
        switch (value)
        {
            case Value.Null: status = raw.sqlite3_bind_null(statement, index); break;
            case Value.Integer integer: status = raw.sqlite3_bind_int64(statement, index, integer.Data); break;
            case Value.Text { Data: not null } text:
                if (Utf8.GetByteCount(text.Data) > MaximumValueBytes) throw new VocabularyException(VocabularyFailure.OversizedArchive);
                // The span overload uses SQLITE_TRANSIENT, copying before this buffer is freed.
                // An empty span can carry a null pointer, which SQLite would bind as SQL NULL.
                // The string overload supplies a terminating byte for the empty TEXT value.
                status = text.Data.Length == 0 ? raw.sqlite3_bind_text(statement, index, "")
                    : raw.sqlite3_bind_text(statement, index, Utf8.GetBytes(text.Data)); break;
            case Value.Blob { Data: not null } blob:
                if (blob.Data.Length > MaximumValueBytes) throw new VocabularyException(VocabularyFailure.OversizedArchive);
                status = blob.Data.Length == 0 ? raw.sqlite3_bind_zeroblob(statement, index, 0) : raw.sqlite3_bind_blob(statement, index, blob.Data); break;
            default: throw Invalid();
        }
        Check(status);
    }
    private static void Check(int status)
    {
        if (status is raw.SQLITE_OK or raw.SQLITE_ROW or raw.SQLITE_DONE) return;
        throw new VocabularyException((status & 0xff) is raw.SQLITE_BUSY or raw.SQLITE_LOCKED
            ? VocabularyFailure.StorageBusy : VocabularyFailure.InvalidArchive);
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
    public void Dispose() { _handle?.Dispose(); _handle = null; _file?.Dispose(); }
}
