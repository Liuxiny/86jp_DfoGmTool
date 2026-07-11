using System;
using System.IO;

namespace DfoGmTool.ServerCore.GameWorld
{
    public class GameWorldConfig
    {
        private static readonly string[] DefaultPvfRelativePaths =
        {
            Path.Combine("Data", "Pvf", "Script.pvf"),
            @"Data\Pvf\Script.pvf"
        };

        public static string PvfArchivePath
        {
            get
            {
                var envPath = Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH");
                var configuredPath = ResolveConfiguredPath(envPath);
                if (!string.IsNullOrWhiteSpace(configuredPath))
                    return configuredPath;

                foreach (var relativePath in DefaultPvfRelativePaths)
                {
                    configuredPath = ResolveConfiguredPath(relativePath);
                    if (!string.IsNullOrWhiteSpace(configuredPath))
                        return configuredPath;
                }

                var fallbackArchive = FindFirstArchive(Path.Combine(AppContext.BaseDirectory, "Data", "Pvf"));
                if (!string.IsNullOrWhiteSpace(fallbackArchive))
                    return fallbackArchive;

                var legacyArchive = FindFirstArchive(AppContext.BaseDirectory);
                if (!string.IsNullOrWhiteSpace(legacyArchive))
                    return legacyArchive;

                throw new FileNotFoundException(
                    "未找到 PVF 文件。请将 Script.pvf 放到 Data/Pvf/Script.pvf，或设置环境变量 PVF_ARCHIVE_PATH。");
            }
        }

        private static string ResolveConfiguredPath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return string.Empty;

            var fullPath = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
            return File.Exists(fullPath) ? fullPath : string.Empty;
        }

        private static string FindFirstArchive(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return string.Empty;

            var archives = Directory.GetFiles(directoryPath, "*.pvf");
            return archives.Length > 0 ? archives[0] : string.Empty;
        }
    }
}
