using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MySqlConnector;

namespace DfoGmTool.Services
{
    public sealed class GmSession
    {
        public string Token { get; init; }
        public int AccountId { get; init; }
        public string AccountName { get; init; }
        public int Role { get; set; }
        public DateTimeOffset ExpiresAt { get; init; }
    }

    // Server-side authentication/RBAC. Database and SSH credentials never leave this process.
    public sealed class GmAccessControl
    {
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(2);
        private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
        private const int FailureLimit = 8;
        private readonly string _connectionString;
        private readonly ConcurrentDictionary<string, GmSession> _sessions = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, FailureState> _failures = new(StringComparer.OrdinalIgnoreCase);

        public GmAccessControl(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("缺少数据库连接。", nameof(connectionString));
            _connectionString = connectionString;
            EnsureSchema();
        }

        public bool RequiresAuthentication => true;
        public bool IsAuthenticated(HttpContext context) => GetSession(context) != null;

        public GmSession GetSession(HttpContext context)
        {
            var token = ReadToken(context);
            if (string.IsNullOrWhiteSpace(token) || !_sessions.TryGetValue(token, out var session)) return null;
            if (session.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _sessions.TryRemove(token, out _);
                return null;
            }
            session.Role = LoadRole(session.AccountId);
            return session;
        }

        public object Login(HttpContext context, string accountName, string password)
        {
            accountName = (accountName ?? string.Empty).Trim();
            var remote = context?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var failureKey = remote + "\n" + accountName;
            if (IsRateLimited(failureKey, out var retry)) return new { success = false, error = $"尝试次数过多，请在 {retry} 秒后重试。" };

            var record = FindCredential(accountName);
            if (record == null || !VerifyPassword(password ?? string.Empty, record.PasswordVerifier))
            {
                RecordFailure(failureKey);
                Audit(record?.AccountId ?? 0, accountName, "login_failed", false, remote, null);
                return new { success = false, error = "账号或密码错误。" };
            }

            _failures.TryRemove(failureKey, out _);
            RemoveExpiredSessions();
            var session = new GmSession
            {
                Token = CreateSessionToken(),
                AccountId = record.AccountId,
                AccountName = record.AccountName,
                Role = LoadRole(record.AccountId),
                ExpiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime),
            };
            _sessions[session.Token] = session;
            Audit(session.AccountId, session.AccountName, "login", true, remote, null);
            return new { success = true, token = session.Token, expiresAt = session.ExpiresAt, accountId = session.AccountId, accountName = session.AccountName, role = session.Role };
        }

        public void Logout(HttpContext context)
        {
            var session = GetSession(context);
            if (session == null) return;
            _sessions.TryRemove(session.Token, out _);
            Audit(session.AccountId, session.AccountName, "logout", true, context?.Connection.RemoteIpAddress?.ToString(), null);
        }

        public bool OwnsCharacter(GmSession session, int characterId)
        {
            if (session == null || characterId <= 0) return false;
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM characters WHERE character_id=@cid AND account_id=@aid AND delete_flag=0;";
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@aid", session.AccountId);
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
        }

        public bool CanUseCharacter(GmSession session, int characterId) => session != null && (session.Role >= 3 || OwnsCharacter(session, characterId));
        public bool CanUseAccount(GmSession session, int accountId) => session != null && (session.Role >= 3 || session.AccountId == accountId);

        public object ListPermissions(GmSession session, string query, int limit)
        {
            if (session?.Role < 3) return Denied();
            limit = Math.Clamp(limit, 1, 200);
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT a.account_id,a.m_id,COALESCE(p.role_level,1),p.updated_at
FROM accounts a LEFT JOIN gm_account_permissions p ON p.account_id=a.account_id
WHERE (@q='' OR a.m_id LIKE CONCAT('%',@q,'%') OR CAST(a.account_id AS CHAR)=@q)
ORDER BY a.account_id LIMIT @limit;";
            command.Parameters.AddWithValue("@q", (query ?? string.Empty).Trim());
            command.Parameters.AddWithValue("@limit", limit);
            using var reader = command.ExecuteReader();
            var values = new List<object>();
            while (reader.Read()) values.Add(new { accountId = reader.GetInt32(0), accountName = reader.GetString(1), role = reader.GetInt32(2), updatedAt = reader.IsDBNull(3) ? null : reader.GetValue(3) });
            return new { success = true, accounts = values };
        }

        public object SetPermission(GmSession session, int accountId, int role, string remote)
        {
            if (session?.Role < 3) return Denied();
            if (role is < 1 or > 3) return new { success = false, error = "权限等级必须为 1、2 或 3。" };
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO gm_account_permissions(account_id,role_level,granted_by)
SELECT account_id,@role,@actor FROM accounts WHERE account_id=@id
ON DUPLICATE KEY UPDATE role_level=VALUES(role_level),granted_by=VALUES(granted_by),updated_at=CURRENT_TIMESTAMP;";
            command.Parameters.AddWithValue("@id", accountId);
            command.Parameters.AddWithValue("@role", role);
            command.Parameters.AddWithValue("@actor", session.AccountId);
            if (command.ExecuteNonQuery() == 0) return new { success = false, error = "账号不存在。" };
            Audit(session.AccountId, session.AccountName, "permission_set", true, remote, new { targetAccountId = accountId, role });
            return new { success = true, accountId, role };
        }

        public object SearchLogs(GmSession session, string category, string account, string character, string query, DateTime? from, DateTime? to, int limit)
        {
            if (session?.Role < 3) return Denied();
            limit = Math.Clamp(limit, 1, 500);
            var rows = new List<LogEntry>();
            LoadDatabaseLogs(rows, category, account, character, query, from, to, limit);
            LoadServerLog(rows, category, account, character, query, limit);
            return new { success = true, logs = rows.OrderByDescending(x => x.Timestamp).Take(limit).ToArray() };
        }

        public void AuditOperation(GmSession session, string action, bool success, HttpContext context, object details = null)
        {
            if (session != null) Audit(session.AccountId, session.AccountName, action, success, context?.Connection.RemoteIpAddress?.ToString(), details);
        }

        private void EnsureSchema()
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS gm_account_permissions(
 account_id BIGINT NOT NULL PRIMARY KEY,role_level TINYINT NOT NULL DEFAULT 1,granted_by BIGINT NULL,
 created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
 CONSTRAINT fk_gm_permission_account FOREIGN KEY(account_id) REFERENCES accounts(account_id) ON DELETE CASCADE,
 CONSTRAINT chk_gm_permission_role CHECK(role_level BETWEEN 1 AND 3));
CREATE TABLE IF NOT EXISTS gm_security_audit(
 audit_id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,account_id BIGINT NOT NULL DEFAULT 0,account_name VARCHAR(255) NOT NULL DEFAULT '',
 action_name VARCHAR(128) NOT NULL,success TINYINT NOT NULL,remote_address VARCHAR(128) NOT NULL DEFAULT '',details_json LONGTEXT NOT NULL,
 created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,INDEX idx_gm_security_account_time(account_id,created_at),INDEX idx_gm_security_action_time(action_name,created_at));";
            command.ExecuteNonQuery();
            var bootstrap = Environment.GetEnvironmentVariable("DFO_GM_BOOTSTRAP_ACCOUNT");
            if (string.IsNullOrWhiteSpace(bootstrap)) return;
            using var bootstrapCommand = connection.CreateCommand();
            bootstrapCommand.CommandText = @"INSERT INTO gm_account_permissions(account_id,role_level,granted_by)
SELECT account_id,3,NULL FROM accounts WHERE m_id=@name ON DUPLICATE KEY UPDATE role_level=GREATEST(role_level,3);";
            bootstrapCommand.Parameters.AddWithValue("@name", bootstrap.Trim());
            bootstrapCommand.ExecuteNonQuery();
        }

        private CredentialRecord FindCredential(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName)) return null;
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT a.account_id,a.m_id,c.password_verifier FROM accounts a JOIN gateway_credentials c ON c.account_id=a.account_id WHERE a.m_id=@name LIMIT 1;";
            command.Parameters.AddWithValue("@name", accountName);
            using var reader = command.ExecuteReader();
            return reader.Read() ? new CredentialRecord(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)) : null;
        }

        private int LoadRole(int accountId)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE((SELECT role_level FROM gm_account_permissions WHERE account_id=@id),1);";
            command.Parameters.AddWithValue("@id", accountId);
            return Math.Clamp(Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture), 1, 3);
        }

        private void LoadDatabaseLogs(List<LogEntry> rows, string category, string account, string character, string query, DateTime? from, DateTime? to, int limit)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT l.created_at,'inventory',l.action_name,a.m_id,c.name,CONCAT('item=',l.item_id,', count=',l.count_before,'→',l.count_after,', slot=',COALESCE(l.slot_index,-1))
FROM inventory_audit_log_v2 l LEFT JOIN accounts a ON a.account_id=l.account_id LEFT JOIN characters c ON c.character_id=l.character_id
WHERE (@category='' OR @category='inventory' OR (@category='consumption' AND l.count_delta<0))
AND (@account='' OR a.m_id LIKE CONCAT('%',@account,'%') OR CAST(l.account_id AS CHAR)=@account)
AND (@character='' OR c.name LIKE CONCAT('%',@character,'%') OR CAST(l.character_id AS CHAR)=@character)
AND (@query='' OR l.action_name LIKE CONCAT('%',@query,'%') OR l.payload_json LIKE CONCAT('%',@query,'%'))
AND (@from IS NULL OR l.created_at>=@from) AND (@to IS NULL OR l.created_at<=@to)
UNION ALL
SELECT s.created_at,'security',s.action_name,s.account_name,'',s.details_json FROM gm_security_audit s
WHERE (@category='' OR @category='security') AND (@account='' OR s.account_name LIKE CONCAT('%',@account,'%') OR CAST(s.account_id AS CHAR)=@account)
AND @character='' AND (@query='' OR s.action_name LIKE CONCAT('%',@query,'%') OR s.details_json LIKE CONCAT('%',@query,'%'))
AND (@from IS NULL OR s.created_at>=@from) AND (@to IS NULL OR s.created_at<=@to)
ORDER BY created_at DESC LIMIT @limit;";
            command.Parameters.AddWithValue("@category", (category ?? string.Empty).Trim().ToLowerInvariant());
            command.Parameters.AddWithValue("@account", (account ?? string.Empty).Trim());
            command.Parameters.AddWithValue("@character", (character ?? string.Empty).Trim());
            command.Parameters.AddWithValue("@query", (query ?? string.Empty).Trim());
            command.Parameters.AddWithValue("@from", from.HasValue ? from.Value : DBNull.Value);
            command.Parameters.AddWithValue("@to", to.HasValue ? to.Value : DBNull.Value);
            command.Parameters.AddWithValue("@limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(new LogEntry { Timestamp = Convert.ToDateTime(reader.GetValue(0), CultureInfo.InvariantCulture), Category = reader.GetString(1), Action = reader.GetString(2), Account = reader.IsDBNull(3) ? "" : reader.GetString(3), Character = reader.IsDBNull(4) ? "" : reader.GetString(4), Message = reader.IsDBNull(5) ? "" : reader.GetString(5) });
        }

        private static void LoadServerLog(List<LogEntry> rows, string category, string account, string character, string query, int limit)
        {
            var path = Environment.GetEnvironmentVariable("DFO_GM_SERVER_LOG_PATH");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var wanted = (category ?? "").Trim().ToLowerInvariant();
            if (wanted is not ("" or "server" or "dungeon" or "skill" or "clear")) return;
            var terms = new[] { account, character, query }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
            try
            {
                foreach (var line in File.ReadLines(path).Reverse().Take(20_000))
                {
                    if (terms.Any(term => line.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    var detected = DetectCategory(line);
                    if (wanted.Length != 0 && wanted != "server" && wanted != detected) continue;
                    rows.Add(new LogEntry { Timestamp = DateTime.MinValue, Category = detected, Action = "server_log", Message = line });
                    if (rows.Count >= limit) break;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string DetectCategory(string line)
        {
            if (line.Contains("skill", StringComparison.OrdinalIgnoreCase) || line.Contains("技能", StringComparison.OrdinalIgnoreCase)) return "skill";
            if (line.Contains("clear", StringComparison.OrdinalIgnoreCase) || line.Contains("通关", StringComparison.OrdinalIgnoreCase)) return "clear";
            if (line.Contains("dungeon", StringComparison.OrdinalIgnoreCase) || line.Contains("副本", StringComparison.OrdinalIgnoreCase)) return "dungeon";
            return "server";
        }

        private void Audit(int accountId, string accountName, string action, bool success, string remote, object details)
        {
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"INSERT INTO gm_security_audit(account_id,account_name,action_name,success,remote_address,details_json) VALUES(@id,@name,@action,@success,@remote,@details);";
                command.Parameters.AddWithValue("@id", accountId); command.Parameters.AddWithValue("@name", accountName ?? ""); command.Parameters.AddWithValue("@action", action ?? "");
                command.Parameters.AddWithValue("@success", success ? 1 : 0); command.Parameters.AddWithValue("@remote", remote ?? ""); command.Parameters.AddWithValue("@details", JsonSerializer.Serialize(details ?? new { }));
                command.ExecuteNonQuery();
            }
            catch { }
        }

        private MySqlConnection Open() { var connection = new MySqlConnection(_connectionString); connection.Open(); return connection; }
        private bool IsRateLimited(string key, out int retry)
        {
            retry = 0;
            if (!_failures.TryGetValue(key, out var state)) return false;
            if (DateTimeOffset.UtcNow - state.StartedAt > FailureWindow) { _failures.TryRemove(key, out _); return false; }
            if (state.Count < FailureLimit) return false;
            retry = Math.Max(1, (int)(FailureWindow - (DateTimeOffset.UtcNow - state.StartedAt)).TotalSeconds);
            return true;
        }
        private void RecordFailure(string key) => _failures.AddOrUpdate(key, _ => new FailureState(1, DateTimeOffset.UtcNow), (_, state) => DateTimeOffset.UtcNow - state.StartedAt > FailureWindow ? new FailureState(1, DateTimeOffset.UtcNow) : new FailureState(state.Count + 1, state.StartedAt));
        private void RemoveExpiredSessions() { var now = DateTimeOffset.UtcNow; foreach (var pair in _sessions) if (pair.Value.ExpiresAt <= now) _sessions.TryRemove(pair.Key, out _); }
        private static string ReadToken(HttpContext context) { var header = context?.Request.Headers.Authorization.ToString(); return header != null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header.Substring(7).Trim() : null; }
        private static string CreateSessionToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static bool VerifyPassword(string password, string encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded)) return false;
            var fields = encoded.Split('$');
            if (fields.Length != 4 || fields[0] != "pbkdf2-sha256" || !int.TryParse(fields[1], out var iterations) || iterations is < 100_000 or > 2_000_000) return false;
            try { var salt = Convert.FromBase64String(fields[2]); var expected = Convert.FromBase64String(fields[3]); if (salt.Length < 16 || expected.Length != 32) return false; var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length); return CryptographicOperations.FixedTimeEquals(actual, expected); }
            catch (FormatException) { return false; }
        }
        private static object Denied() => new { success = false, error = "当前账号没有此权限。" };
        private sealed record CredentialRecord(int AccountId, string AccountName, string PasswordVerifier);
        private sealed record FailureState(int Count, DateTimeOffset StartedAt);
        private sealed class LogEntry { public DateTime Timestamp { get; set; } public string Category { get; set; } public string Action { get; set; } public string Account { get; set; } public string Character { get; set; } public string Message { get; set; } }
    }
}
