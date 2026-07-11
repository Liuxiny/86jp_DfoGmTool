using System;
using System.IO;

namespace DfoGmTool.ServerCore.Infrastructure
{
    public static class ServerPaths
    {
        private static readonly string BaseDirectory = AppContext.BaseDirectory;

        public static string DatabasePath
        {
            get
            {
                var configured = Environment.GetEnvironmentVariable("INVENTORY_DATABASE_PATH");
                return string.IsNullOrWhiteSpace(configured)
                    ? Path.Combine(BaseDirectory, "Data", "inventory.db")
                    : Path.IsPathRooted(configured)
                        ? configured
                        : Path.Combine(BaseDirectory, configured);
            }
        }

        public static string SchemaFilePath => Path.Combine(BaseDirectory, "Sqlite", "item_schema.sql");

        public static string ChannelInfoFilePath => Path.Combine(BaseDirectory, "channel_info.etc");
    }
}
