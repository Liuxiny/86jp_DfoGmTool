using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DfoGmTool
{
    // Host-level options are intentionally separate from the game data source model.
    public sealed class GmToolHostConfig
    {
        private const string ConfigFileName = "config.ini";
        private const int DefaultListenPort = 5050;
        private const int MinimumRemotePasswordLength = 8;
        private static readonly HashSet<string> SupportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "allow_remote_access",
            "listen_port",
            "remote_password",
            "database_path",
            "pvf_path",
        };

        private readonly List<string> _validationErrors = new List<string>();
        private readonly HashSet<string> _invalidKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string ConfigPath { get; }
        public bool AllowRemoteAccess { get; private set; }
        public int ListenPort { get; private set; } = DefaultListenPort;
        public string RemotePassword { get; private set; }
        public string DatabasePath { get; private set; }
        public string PvfPath { get; private set; }

        public string ListenUrl => AllowRemoteAccess
            ? "http://0.0.0.0:" + ListenPort
            : "http://localhost:" + ListenPort;

        private GmToolHostConfig(string configPath)
        {
            ConfigPath = configPath;
        }

        public static GmToolHostConfig LoadOrCreate()
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
            EnsureConfigFile(configPath);

            var config = new GmToolHostConfig(configPath);
            if (!File.Exists(configPath))
                return config;

            var values = ReadValues(configPath, config._validationErrors, config._invalidKeys);
            config.Apply(values, config._validationErrors);
            return config;
        }

        public GmConfig ResolveInitialSource(string[] args)
        {
            var errors = new List<string>(_validationErrors);
            if (!AllowRemoteAccess)
            {
                ThrowIfInvalid(errors);
                return GmConfig.TryResolve(args);
            }

            errors.AddRange(ValidateRemoteSettings());
            var hasDatabasePath = IsFullyQualifiedPath(DatabasePath);
            var hasPvfPath = IsFullyQualifiedPath(PvfPath);
            GmConfig config = null;
            if (hasDatabasePath && hasPvfPath)
            {
                if (!GmConfig.TryCreate(DatabasePath, PvfPath, out config, out var sourceError))
                    AddErrorLines(errors, sourceError);
            }
            else
            {
                AddFileValidationError(errors, DatabasePath, "数据库", hasDatabasePath);
                AddFileValidationError(errors, PvfPath, "PVF", hasPvfPath);
            }

            ThrowIfInvalid(errors);
            return config;
        }

        private void Apply(Dictionary<string, string> values, List<string> errors)
        {
            if (values.TryGetValue("allow_remote_access", out var allowRemoteRaw))
            {
                if (!TryParseBoolean(allowRemoteRaw, out var allowRemote))
                    errors.Add("allow_remote_access 必须为 true、false、1 或 0。");
                else
                    AllowRemoteAccess = allowRemote;
            }

            if (values.TryGetValue("listen_port", out var portRaw) && !string.IsNullOrWhiteSpace(portRaw))
            {
                if (!int.TryParse(portRaw, out var port) || port < 1 || port > 65535)
                    errors.Add("listen_port 必须为 1 到 65535 的整数。");
                else
                    ListenPort = port;
            }

            values.TryGetValue("remote_password", out var password);
            values.TryGetValue("database_path", out var databasePath);
            values.TryGetValue("pvf_path", out var pvfPath);
            RemotePassword = password?.Trim();
            DatabasePath = databasePath?.Trim();
            PvfPath = pvfPath?.Trim();
        }

        private List<string> ValidateRemoteSettings()
        {
            var errors = new List<string>();
            if (!_invalidKeys.Contains("remote_password"))
            {
                if (string.IsNullOrWhiteSpace(RemotePassword))
                    errors.Add("远程访问已启用，必须填写 remote_password。");
                else if (RemotePassword.Length < MinimumRemotePasswordLength)
                    errors.Add("remote_password 至少需要 " + MinimumRemotePasswordLength + " 个字符。");
            }

            ValidatePathSetting("database_path", DatabasePath, _invalidKeys.Contains("database_path"), errors);
            ValidatePathSetting("pvf_path", PvfPath, _invalidKeys.Contains("pvf_path"), errors);
            return errors;
        }

        private static void ValidatePathSetting(string key, string value, bool isInvalid, List<string> errors)
        {
            if (isInvalid)
                return;
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add("远程访问已启用，必须填写 " + key + "。");
                return;
            }
            if (!IsFullyQualifiedPath(value))
                errors.Add(key + " 必须为服务器上的完整绝对路径。");
        }

        private static bool IsFullyQualifiedPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);
        }

        private static void AddFileValidationError(List<string> errors, string path, string label, bool hasFullPath)
        {
            if (!hasFullPath)
                return;

            if (GmConfig.TryGetExistingFilePath(path, label, out _, out var error))
                return;

            AddErrorLines(errors, error);
        }

        private static Dictionary<string, string> ReadValues(
            string configPath,
            List<string> errors,
            HashSet<string> invalidKeys)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var lineNumber = 0;
                foreach (var rawLine in File.ReadLines(configPath))
                {
                    lineNumber++;
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                        continue;

                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        errors.Add("第 " + lineNumber + " 行格式无效，应使用 key=value。");
                        continue;
                    }

                    var key = line.Substring(0, separator).Trim();
                    if (key.Length == 0)
                    {
                        errors.Add("第 " + lineNumber + " 行配置键为空。");
                        continue;
                    }
                    if (!SupportedKeys.Contains(key))
                    {
                        errors.Add("第 " + lineNumber + " 行包含不支持的配置键: " + key + "。");
                        continue;
                    }
                    if (values.ContainsKey(key))
                    {
                        errors.Add("第 " + lineNumber + " 行重复配置键: " + key + "。");
                        continue;
                    }
                    if (!TryNormalizeValue(line.Substring(separator + 1), out var value))
                    {
                        errors.Add("第 " + lineNumber + " 行配置值的引号必须成对。");
                        invalidKeys.Add(key);
                        continue;
                    }
                    values[key] = value;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("读取 config.ini 失败: " + ex.Message, ex);
            }

            return values;
        }

        private static bool TryNormalizeValue(string value, out string normalized)
        {
            normalized = TrimValueBoundaryCharacters(value);
            if (normalized.Length == 0)
                return true;

            var first = normalized[0];
            var last = normalized[normalized.Length - 1];
            var startsWithQuote = first == '"' || first == '\'';
            var endsWithQuote = last == '"' || last == '\'';
            if (!startsWithQuote && !endsWithQuote)
                return true;
            if (normalized.Length < 2 || first != last)
                return false;

            normalized = TrimValueBoundaryCharacters(normalized.Substring(1, normalized.Length - 2));
            return true;
        }

        private static string TrimValueBoundaryCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var start = 0;
            var end = value.Length;
            while (start < end && IsIgnoredBoundaryCharacter(value[start]))
                start++;
            while (end > start && IsIgnoredBoundaryCharacter(value[end - 1]))
                end--;

            return start == 0 && end == value.Length
                ? value
                : value.Substring(start, end - start);
        }

        private static bool IsIgnoredBoundaryCharacter(char value)
        {
            return char.IsWhiteSpace(value)
                || char.GetUnicodeCategory(value) == UnicodeCategory.Format;
        }

        private static void ThrowIfInvalid(List<string> errors)
        {
            if (errors == null || errors.Count == 0)
                return;

            throw new InvalidOperationException("config.ini 配置错误:\r\n- " + string.Join("\r\n- ", errors));
        }

        private static void AddErrorLines(List<string> errors, string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return;

            var lines = error.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
                errors.Add(line.Trim());
        }

        private static bool TryParseBoolean(string value, out bool result)
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1")
            {
                result = true;
                return true;
            }
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || value == "0")
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static void EnsureConfigFile(string configPath)
        {
            if (File.Exists(configPath))
                return;

            try
            {
                using (var source = typeof(GmToolHostConfig).Assembly.GetManifestResourceStream("DfoGmTool.config.ini"))
                {
                    if (source == null)
                        throw new InvalidOperationException("找不到内置 config.ini 模板。");
                    using (var destination = File.Create(configPath))
                        source.CopyTo(destination);
                }
                Console.WriteLine("已创建配置文件: " + configPath);
            }
            catch (Exception ex)
            {
                // Pure local mode remains usable even when the output folder is read-only.
                Console.WriteLine("无法创建 config.ini: " + ex.Message);
            }
        }

    }
}
