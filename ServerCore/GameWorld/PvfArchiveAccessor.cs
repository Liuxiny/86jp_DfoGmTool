using GmPvfLib;
using System;
using System.IO;

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

        private static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("relativePath cannot be null or empty.", nameof(relativePath));

            return relativePath.Replace('\\', '/').TrimStart('.', '/');
        }
    }
}
