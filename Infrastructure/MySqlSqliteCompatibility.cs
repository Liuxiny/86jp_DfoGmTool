using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using MySqlConnector;

// The original GM business layer was written against the small Sqlite API surface.
// Production uses MySQL, so this compatibility layer keeps the reviewed business
// operations while translating their parameterised SQL. It intentionally supports
// MySQL only; the desktop client never receives this connection string.
namespace Microsoft.Data.Sqlite
{
    public enum SqliteOpenMode { ReadWrite, ReadOnly, ReadWriteCreate, Memory }
    public enum SqliteType { Blob, Integer, Real, Text }

    public sealed class SqliteConnectionStringBuilder
    {
        private string _dataSource = string.Empty;
        public SqliteConnectionStringBuilder() { }
        public SqliteConnectionStringBuilder(string value) => _dataSource = value ?? string.Empty;
        public string DataSource { get => _dataSource; set => _dataSource = value ?? string.Empty; }
        public SqliteOpenMode Mode { get; set; }
        public bool ForeignKeys { get; set; }
        public string ConnectionString => _dataSource;
        public override string ToString() => ConnectionString;
    }

    public sealed class SqliteConnection : IDisposable
    {
        private readonly MySqlConnection _inner;
        public SqliteConnection(string connectionString)
        {
            if (!IsMySql(connectionString))
                throw new NotSupportedException("此安全部署仅允许连接 MySQL 数据源。");
            ConnectionString = connectionString ?? string.Empty;
            _inner = new MySqlConnection(ConnectionString);
        }

        internal MySqlConnection Inner => _inner;
        public string ConnectionString { get; }
        public ConnectionState State => _inner.State;
        public static bool IsMySql(string value) => Regex.IsMatch(
            value ?? string.Empty,
            @"(^|;)(server|host|database|user\s*id|uid|password|pwd|port)\s*=",
            RegexOptions.IgnoreCase);

        public void Open()
        {
            _inner.Open();
            ExecuteSessionCommand("SET time_zone = '+00:00'");
            ExecuteSessionCommand("SET SESSION sql_mode = 'STRICT_TRANS_TABLES,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO,ONLY_FULL_GROUP_BY'");
        }

        private void ExecuteSessionCommand(string sql)
        {
            using var command = _inner.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public void Close() => _inner.Close();
        public SqliteTransaction BeginTransaction() => new SqliteTransaction(_inner.BeginTransaction(), this);
        public SqliteTransaction BeginTransaction(bool deferred) => BeginTransaction();
        public SqliteCommand CreateCommand() => new SqliteCommand(_inner.CreateCommand(), this);
        public void Dispose() => _inner.Dispose();
        public static void ClearAllPools() => MySqlConnection.ClearAllPools();
    }

    public sealed class SqliteTransaction : IDisposable
    {
        internal SqliteTransaction(DbTransaction inner, SqliteConnection connection)
        {
            Inner = inner;
            Connection = connection;
        }
        internal DbTransaction Inner { get; }
        public SqliteConnection Connection { get; }
        public void Commit() => Inner.Commit();
        public void Rollback() => Inner.Rollback();
        public void Dispose() => Inner.Dispose();
    }

    public sealed class SqliteCommand : IDisposable
    {
        private readonly DbCommand _inner;
        private readonly SqliteConnection _connection;
        private SqliteTransaction _transaction;
        private bool _prepared;

        internal SqliteCommand(DbCommand inner, SqliteConnection connection)
        {
            _inner = inner;
            _connection = connection;
            Parameters = new SqliteParameterCollection(inner);
        }
        public SqliteCommand(string text, SqliteConnection connection) : this(connection.Inner.CreateCommand(), connection) => CommandText = text;
        public SqliteCommand(string text, SqliteConnection connection, SqliteTransaction transaction) : this(text, connection) => Transaction = transaction;
        public string CommandText
        {
            get => _inner.CommandText;
            set { _inner.CommandText = value; _prepared = false; }
        }
        public SqliteParameterCollection Parameters { get; }
        public SqliteTransaction Transaction
        {
            get => _transaction;
            set { _transaction = value; _inner.Transaction = value?.Inner; }
        }
        public DbParameter CreateParameter() => _inner.CreateParameter();
        public int ExecuteNonQuery() { Prepare(); try { return _inner.ExecuteNonQuery(); } catch (DbException ex) { throw SqliteException.FromDb(ex); } }
        public object ExecuteScalar() { Prepare(); try { return _inner.ExecuteScalar(); } catch (DbException ex) { throw SqliteException.FromDb(ex); } }
        public SqliteDataReader ExecuteReader() { Prepare(); try { return new SqliteDataReader(_inner.ExecuteReader()); } catch (DbException ex) { throw SqliteException.FromDb(ex); } }
        public void Dispose() => _inner.Dispose();
        private void Prepare()
        {
            if (_prepared) return;
            _inner.CommandText = MySqlTranslator.Translate(_inner.CommandText);
            _prepared = true;
        }
    }

    public sealed class SqliteParameterCollection
    {
        private readonly DbCommand _command;
        private readonly DbParameterCollection _inner;
        internal SqliteParameterCollection(DbCommand command) { _command = command; _inner = command.Parameters; }
        public DbParameter AddWithValue(string name, object value)
        {
            var parameter = _command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            _inner.Add(parameter);
            return parameter;
        }
        public DbParameter Add(string name, SqliteType type)
        {
            var parameter = _command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type switch
            {
                SqliteType.Blob => DbType.Binary,
                SqliteType.Integer => DbType.Int64,
                SqliteType.Real => DbType.Double,
                _ => DbType.String,
            };
            _inner.Add(parameter);
            return parameter;
        }
        public int Add(DbParameter parameter) => _inner.Add(parameter);
        public void Clear() => _inner.Clear();
    }

    public sealed class SqliteDataReader : IDataReader, IDataRecord
    {
        private readonly DbDataReader _inner;
        internal SqliteDataReader(DbDataReader inner) => _inner = inner;
        public int FieldCount => _inner.FieldCount;
        public bool Read() => _inner.Read();
        public bool IsDBNull(int i) => _inner.IsDBNull(i);
        public object GetValue(int i) => _inner.GetValue(i);
        public int GetValues(object[] values) => _inner.GetValues(values);
        public string GetString(int i) { var value = _inner.GetValue(i); return value is byte[] bytes ? System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0') : Convert.ToString(value, CultureInfo.InvariantCulture); }
        public int GetInt32(int i) => Convert.ToInt32(_inner.GetValue(i), CultureInfo.InvariantCulture);
        public long GetInt64(int i) => Convert.ToInt64(_inner.GetValue(i), CultureInfo.InvariantCulture);
        public short GetInt16(int i) => Convert.ToInt16(_inner.GetValue(i), CultureInfo.InvariantCulture);
        public byte GetByte(int i) => Convert.ToByte(_inner.GetValue(i), CultureInfo.InvariantCulture);
        public bool GetBoolean(int i) => Convert.ToBoolean(_inner.GetValue(i), CultureInfo.InvariantCulture);
        public Guid GetGuid(int i) => (Guid)_inner.GetValue(i);
        public DateTime GetDateTime(int i) => Convert.ToDateTime(_inner.GetValue(i), CultureInfo.InvariantCulture);
        public decimal GetDecimal(int i) => Convert.ToDecimal(_inner.GetValue(i), CultureInfo.InvariantCulture);
        public double GetDouble(int i) => Convert.ToDouble(_inner.GetValue(i), CultureInfo.InvariantCulture);
        public float GetFloat(int i) => Convert.ToSingle(_inner.GetValue(i), CultureInfo.InvariantCulture);
        public char GetChar(int i) => Convert.ToChar(_inner.GetValue(i), CultureInfo.InvariantCulture);
        public long GetBytes(int i, long o, byte[] b, int bo, int l) => _inner.GetBytes(i, o, b, bo, l);
        public long GetChars(int i, long o, char[] b, int bo, int l) => _inner.GetChars(i, o, b, bo, l);
        public IDataReader GetData(int i) => throw new NotSupportedException();
        public string GetName(int i) => _inner.GetName(i);
        public int GetOrdinal(string name) => _inner.GetOrdinal(name);
        public Type GetFieldType(int i) => _inner.GetFieldType(i);
        public string GetDataTypeName(int i) => _inner.GetDataTypeName(i);
        public object this[int i] => _inner.GetValue(i);
        public object this[string name] => _inner[name];
        public DataTable GetSchemaTable() => _inner.GetSchemaTable();
        public bool NextResult() => _inner.NextResult();
        public int Depth => 0;
        public bool IsClosed => _inner.IsClosed;
        public int RecordsAffected => _inner.RecordsAffected;
        public void Close() => _inner.Close();
        public void Dispose() => _inner.Dispose();
    }

    public class SqliteException : Exception
    {
        public SqliteException(string message, Exception inner = null) : base(message, inner) { }
        public int SqliteErrorCode { get; internal set; }
        public int SqliteExtendedErrorCode { get; internal set; }
        internal static SqliteException FromDb(DbException exception)
        {
            if (exception is MySqlException mysql)
            {
                var compatible = mysql.Number == 1062 ? 19 : mysql.Number is 1205 or 1213 ? 5 : mysql.Number;
                return new SqliteException(mysql.Message, mysql) { SqliteErrorCode = compatible, SqliteExtendedErrorCode = mysql.Number };
            }
            return new SqliteException(exception.Message, exception);
        }
    }

    internal static class MySqlTranslator
    {
        public static string Translate(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return sql;
            var value = sql.Trim();
            if (Regex.IsMatch(value, @"^PRAGMA\s+(foreign_keys|journal_mode|busy_timeout)", RegexOptions.IgnoreCase)) return "SELECT 1";
            if (Regex.IsMatch(value, @"^PRAGMA\s+user_version", RegexOptions.IgnoreCase)) return "SELECT COALESCE((SELECT version FROM schema_migrations ORDER BY version DESC LIMIT 1), 0)";
            var tableInfo = Regex.Match(value, @"^PRAGMA\s+table_info\((?<table>[A-Za-z0-9_]+)\)\s*;?$", RegexOptions.IgnoreCase);
            if (tableInfo.Success)
            {
                var table = tableInfo.Groups["table"].Value;
                return $"SELECT ORDINAL_POSITION - 1 AS cid, COLUMN_NAME AS name, COLUMN_TYPE AS type, IF(IS_NULLABLE='NO',1,0) AS notnull, COLUMN_DEFAULT AS dflt_value, IF(COLUMN_KEY='PRI',1,0) AS pk FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='{table}' ORDER BY ORDINAL_POSITION";
            }
            value = Regex.Replace(value, @"SELECT\s+COUNT\s*\(\s*\*\s*\)\s+FROM\s+sqlite_master\s+WHERE\s+type\s*=\s*'table'\s+AND\s+name\s+NOT\s+LIKE\s+'sqlite_%'\s*;?", "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_TYPE='BASE TABLE'", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"SELECT\s+COUNT\s*\(\s*\*\s*\)\s+FROM\s+sqlite_master\s+WHERE\s+type\s*=\s*'table'\s+AND\s+name\s*=\s*(?<p>@[A-Za-z0-9_]+)\s*;?", "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_TYPE='BASE TABLE' AND TABLE_NAME=${p}", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"datetime\s*\(\s*'now'\s*\)", "CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"datetime\s*\(\s*'now'\s*,\s*'-30 days'\s*\)", "DATE_SUB(CURRENT_TIMESTAMP, INTERVAL 30 DAY)", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"CAST\s*\(\s*(?<e>[A-Za-z0-9_`.]+)\s+AS\s+BLOB\s*\)", "CAST(${e} AS BINARY)", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\bMAX\s*\(\s*(?<a>[^(),]+?)\s*,\s*(?<b>[^()]+?)\s*\)", "GREATEST(${a}, ${b})", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\bMIN\s*\(\s*(?<a>[^(),]+?)\s*,\s*(?<b>[^()]+?)\s*\)", "LEAST(${a}, ${b})", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\blast_insert_rowid\s*\(\s*\)", "LAST_INSERT_ID()", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\bINSERT\s+OR\s+IGNORE\s+INTO\b", "INSERT IGNORE INTO", RegexOptions.IgnoreCase);
            value = TranslateInsertOrReplace(value);
            value = Regex.Replace(value, @"\bINSERT\s+INTO\s+(?<table>`?[A-Za-z0-9_]+`?)\s+DEFAULT\s+VALUES\b", "INSERT INTO ${table} VALUES ()", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\bCREATE\s+UNIQUE\s+INDEX\s+IF\s+NOT\s+EXISTS\b", "CREATE UNIQUE INDEX", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\bCREATE\s+INDEX\s+IF\s+NOT\s+EXISTS\b", "CREATE INDEX", RegexOptions.IgnoreCase);
            var doNothing = Regex.Match(value, @"\s+ON\s+CONFLICT(?:\s*\([^)]*\))?\s+DO\s+NOTHING\s*;?", RegexOptions.IgnoreCase);
            if (doNothing.Success)
            {
                value = value.Remove(doNothing.Index, doNothing.Length);
                value = Regex.Replace(value, @"\bINSERT\s+INTO\b", "INSERT IGNORE INTO", RegexOptions.IgnoreCase);
            }
            var upsert = Regex.Match(value, @"\s+ON\s+CONFLICT(?:\s*\([^)]*\))?\s+DO\s+UPDATE\s+SET\s+(?<set>.+?)\s*;?$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (upsert.Success)
            {
                var set = Regex.Replace(upsert.Groups["set"].Value, @"excluded\.([A-Za-z0-9_]+)", "VALUES($1)", RegexOptions.IgnoreCase);
                // SQLite may attach a WHERE to the update. Existing GM statements
                // also encode the condition in GREATEST/CASE assignments, so
                // removing the final predicate preserves values on the false path.
                set = Regex.Replace(set, @"\s+WHERE\s+.+$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                value = value.Substring(0, upsert.Index) + " ON DUPLICATE KEY UPDATE " + set;
            }
            return value;
        }

        private static string TranslateInsertOrReplace(string sql)
        {
            if (!Regex.IsMatch(sql, @"\bINSERT\s+OR\s+REPLACE\s+INTO\b", RegexOptions.IgnoreCase)) return sql;
            var match = Regex.Match(sql,
                @"^\s*INSERT\s+OR\s+REPLACE\s+INTO\s+(?<table>`?[A-Za-z0-9_]+`?)\s*\((?<columns>[^)]*)\)\s*VALUES\s*\((?<values>.*)\)\s*;?\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) throw new InvalidOperationException("不支持的 INSERT OR REPLACE 形状，已拒绝执行以避免误删关联数据。");
            var columns = match.Groups["columns"].Value.Split(',').Select(x => x.Trim()).Where(x => x.Length != 0).ToArray();
            var updates = string.Join(", ", columns.Select(column => $"{column}=VALUES({column})"));
            return $"INSERT INTO {match.Groups["table"].Value} ({match.Groups["columns"].Value}) VALUES ({match.Groups["values"].Value}) ON DUPLICATE KEY UPDATE {updates}";
        }
    }
}
