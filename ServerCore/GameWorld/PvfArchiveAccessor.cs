using GmPvfLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DfoGmTool.ServerCore.GameWorld
{
    internal static class PvfArchiveAccessor
    {
        private static readonly object Sync = new object();
        private static PvfArchive _archive;
        private static string _archivePath;

        internal static void Configure(string pvfPath)
        {
            if (string.IsNullOrWhiteSpace(pvfPath))
                throw new ArgumentException("PVF path cannot be null or empty.", nameof(pvfPath));

            var fullPath = Path.GetFullPath(pvfPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("PVF 文件不存在。", fullPath);

            lock (Sync)
            {
                _archive?.Dispose();
                _archive = null;
                _archivePath = fullPath;
            }
        }

        public static string ReadText(string relativePath)
        {
            var normalizedPath = NormalizeRelativePath(relativePath);
            lock (Sync)
            {
                var content = GetArchive().GetFileContent(normalizedPath);
                if (string.IsNullOrEmpty(content))
                    throw new FileNotFoundException($"PVF 归档中不存在文件: {normalizedPath}", normalizedPath);

                return content;
            }
        }

        private static PvfArchive GetArchive()
        {
            var path = _archivePath ?? GameWorldConfig.PvfArchivePath;
            if (_archive != null && string.Equals(_archivePath, path, StringComparison.OrdinalIgnoreCase))
                return _archive;

            _archive?.Dispose();
            _archive = PvfArchive.Open(path);
            _archivePath = path;
            return _archive;
        }

        // GM适配: 服务端原版经进程级 Lazy Archive 访问, 此处改经 GetArchive()+Sync 以兼容运行时切换 PVF
        public static IReadOnlyList<string> ReadAllText(string relativePath)
        {
            var normalizedPath = NormalizeRelativePath(relativePath);
            var result = new List<string>();
            lock (Sync)
            {
                var archive = GetArchive();
                foreach (var file in archive.Files)
                {
                    if (!string.Equals(file.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var content = archive.GetFileContent(file);
                    if (!string.IsNullOrEmpty(content))
                        result.Add(content);
                }
            }
            return result;
        }

        public static IReadOnlyList<string> FindPathsContaining(string fragment)
        {
            if (string.IsNullOrWhiteSpace(fragment))
                return Array.Empty<string>();
            lock (Sync)
            {
                return GetArchive().Files
                    .Select(file => string.IsNullOrEmpty(file.Path)
                        ? file.Name
                        : string.IsNullOrEmpty(file.Name)
                            ? file.Path
                            : file.Path.TrimEnd('/', '\\') + "/" + file.Name)
                    .Where(path => !string.IsNullOrEmpty(path)
                        && path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("relativePath cannot be null or empty.", nameof(relativePath));

            return relativePath.Replace('\\', '/').TrimStart('.', '/');
        }
    }
}
