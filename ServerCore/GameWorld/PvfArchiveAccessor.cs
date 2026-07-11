using GmPvfLib;
using System;
using System.IO;

namespace DfoGmTool.ServerCore.GameWorld
{
    internal static class PvfArchiveAccessor
    {
        private static readonly Lazy<PvfArchive> Archive = new Lazy<PvfArchive>(() => PvfArchive.Open(GameWorldConfig.PvfArchivePath));

        public static string ReadText(string relativePath)
        {
            var normalizedPath = NormalizeRelativePath(relativePath);
            var content = Archive.Value.GetFileContent(normalizedPath);
            if (string.IsNullOrEmpty(content))
                throw new FileNotFoundException($"PVF 归档中不存在文件: {normalizedPath}", normalizedPath);

            return content;
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("relativePath cannot be null or empty.", nameof(relativePath));

            return relativePath.Replace('\\', '/').TrimStart('.', '/');
        }
    }
}
