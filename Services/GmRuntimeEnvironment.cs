using System;
using System.Threading;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.GameWorld;
using DfoGmTool.ServerCore.Infrastructure;
using GmPvfLib;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    // Owns the currently selected data source so all GM endpoints switch together.
    public sealed class GmRuntimeEnvironment
    {
        private readonly ReaderWriterLockSlim _gate = new ReaderWriterLockSlim();
        private ActiveEnvironment _active;
        private string _startupError;
        private bool _migrationRequired;
        private bool _migrationBlocked;
        private bool _databaseUnusable;

        public GmRuntimeEnvironment(GmConfig initialConfig)
        {
            if (initialConfig != null)
                Configure(initialConfig);
        }

        public RuntimeEnvironmentStatus GetStatus(bool includeSourceDetails = true)
        {
            _gate.EnterReadLock();
            try
            {
                return BuildStatus(includeSourceDetails);
            }
            finally
            {
                _gate.ExitReadLock();
            }
        }

        public object Configure(string databasePath, string pvfPath)
        {
            if (!GmConfig.TryCreate(databasePath, pvfPath, out var config, out var error))
                return Failure(error);

            return Configure(config);
        }

        public object Execute(Func<GmService, PvfIndexService, object> operation)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            _gate.EnterReadLock();
            try
            {
                if (_active == null)
                    return Failure("请先选择数据库和 PVF。" );
                if (!string.IsNullOrWhiteSpace(_active.PvfIndex.BuildError))
                    return Failure("PVF 加载失败: " + _active.PvfIndex.BuildError);
                if (!_active.PvfIndex.IsReady)
                    return Failure("PVF 正在加载，请稍候。" );

                return operation(_active.Gm, _active.PvfIndex);
            }
            finally
            {
                _gate.ExitReadLock();
            }
        }

        private object Configure(GmConfig config)
        {
            _gate.EnterWriteLock();
            try
            {
                try
                {
                    _migrationRequired = false;
                    _migrationBlocked = false;
                    _databaseUnusable = false;
                    DatabaseCompatibilityReport databaseCompatibility;
                    try
                    {
                        databaseCompatibility = DatabaseCompatibilityGuard.Validate(config.DatabasePath);
                    }
                    catch (Exception databaseError)
                    {
                        var classified = ClassifyRejectedDatabase(config, databaseError);
                        if (classified != null)
                            return classified;
                        throw new InvalidOperationException(
                            "数据库校验失败: " + databaseError.GetBaseException().Message,
                            databaseError);
                    }

                    try
                    {
                        VerifyPvf(config);
                    }
                    catch (Exception pvfError)
                    {
                        throw new InvalidOperationException(
                            "PVF校验失败: " + pvfError.GetBaseException().Message,
                            pvfError);
                    }

                    // Construct the new services before replacing the live source.
                    var pvfIndex = new PvfIndexService(config.PvfPath);
                    var gm = new GmService(config, pvfIndex);

                    Environment.SetEnvironmentVariable("PVF_ARCHIVE_PATH", config.PvfPath);
                    Environment.SetEnvironmentVariable("INVENTORY_DATABASE_PATH", config.DatabasePath);
                    PvfArchiveAccessor.Configure(config.PvfPath);
                    PvfRuntimeCache.ResetForPvfChange();
                    GmService.ResetPvfStaticData();
                    PvfRuntimeCache.WarmForPvfChange();

                    _active = new ActiveEnvironment(
                        config,
                        gm,
                        pvfIndex,
                        databaseCompatibility);
                    _startupError = null;
                    pvfIndex.WarmInBackground();
                    return new { success = true, status = BuildStatus() };
                }
                catch (Exception ex)
                {
                    var error = ex.GetBaseException().Message;
                    if (_active == null)
                        _startupError = error;
                    return Failure(error);
                }
            }
            finally
            {
                _gate.ExitWriteLock();
            }
        }

        private object ClassifyRejectedDatabase(
            GmConfig config,
            Exception databaseError)
        {
            SqliteConnection.ClearAllPools();
            var preview = new A12ToA21MigrationService(
                config.DatabasePath,
                config.PvfPath).Preview();
            var guardMessage = databaseError.GetBaseException().Message;
            var previewMessage = preview.Error ?? string.Empty;
            if (preview.Success)
            {
                ReleaseRejectedSource(
                    migrationRequired: true,
                    migrationBlocked: false,
                    databaseUnusable: false,
                    message: "已识别可迁移旧库，数据库已释放，请预览/升级。" );
                SqliteConnection.ClearAllPools();
                return new
                {
                    success = true,
                    migrationRequired = true,
                    migrationBlocked = false,
                    databaseUnusable = false,
                    error = _startupError,
                    preview,
                    diagnostic = new { databaseGuardError = guardMessage },
                    status = BuildStatus()
                };
            }

            if (IsMigrationBlockedProbe(guardMessage, previewMessage))
            {
                ReleaseRejectedSource(
                    migrationRequired: true,
                    migrationBlocked: true,
                    databaseUnusable: false,
                    message: previewMessage);
                SqliteConnection.ClearAllPools();
                return new
                {
                    success = true,
                    migrationRequired = true,
                    migrationBlocked = true,
                    databaseUnusable = false,
                    error = _startupError,
                    preview,
                    diagnostic = new { databaseGuardError = guardMessage },
                    status = BuildStatus()
                };
            }

            if (!IsUnusableDatabaseProbe(guardMessage, previewMessage))
                return null;

            ReleaseRejectedSource(
                migrationRequired: false,
                migrationBlocked: false,
                databaseUnusable: true,
                message: "数据库不可用；请移除该文件等待服务端自动生成，或选择正确数据库。" );
            SqliteConnection.ClearAllPools();
            return new
            {
                success = true,
                migrationRequired = false,
                migrationBlocked = false,
                databaseUnusable = true,
                error = _startupError,
                preview,
                diagnostic = new { databaseGuardError = guardMessage },
                status = BuildStatus()
            };
        }

        private void ReleaseRejectedSource(
            bool migrationRequired,
            bool migrationBlocked,
            bool databaseUnusable,
            string message)
        {
            _active = null;
            _migrationRequired = migrationRequired;
            _migrationBlocked = migrationBlocked;
            _databaseUnusable = databaseUnusable;
            _startupError = message;
        }

        private static bool IsMigrationBlockedProbe(string guardMessage, string previewMessage)
        {
            var message = (guardMessage ?? string.Empty) + "\n" + (previewMessage ?? string.Empty);
            return message.IndexOf("未合并的 WAL", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("被占用的 WAL", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("被占用的 SHM", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUnusableDatabaseProbe(string guardMessage, string previewMessage)
        {
            var message = (guardMessage ?? string.Empty) + "\n" + (previewMessage ?? string.Empty);
            return message.IndexOf("database disk image is malformed", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("not a database", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("unable to open database", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("缺少 A12 核心表", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("缺少 A12 旧物品表", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("此 S4A12 数据库结构版本不再支持", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("数据库文件不存在", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("无法打开数据库", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("拒绝访问", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void VerifyPvf(GmConfig config)
        {
            using (var archive = PvfArchive.Open(config.PvfPath))
            {
                if (string.IsNullOrWhiteSpace(archive.GetFileContent("stackable/stackable.lst")))
                    throw new InvalidOperationException("所选 PVF 缺少 stackable/stackable.lst。");
            }
        }

        private RuntimeEnvironmentStatus BuildStatus(bool includeSourceDetails = true)
        {
            var config = _active?.Config;
            var index = _active?.PvfIndex;
            var indexError = index?.BuildError;
            var ready = index != null && index.IsReady && string.IsNullOrWhiteSpace(indexError);
            return new RuntimeEnvironmentStatus
            {
                Configured = config != null,
                Ready = ready,
                Loading = config != null && !ready && string.IsNullOrWhiteSpace(indexError),
                Database = includeSourceDetails ? config?.DatabasePath : null,
                Pvf = includeSourceDetails ? config?.PvfPath : null,
                ServerBin = includeSourceDetails ? config?.ServerBinDir : null,
                IndexReady = index?.IsReady ?? false,
                IndexError = includeSourceDetails ? indexError : null,
                Error = includeSourceDetails ? (config == null ? _startupError : indexError) : null,
                HasError = !string.IsNullOrWhiteSpace(config == null ? _startupError : indexError),
                SchemaVersion = _active?.DatabaseCompatibility.SchemaVersion,
                BaselineId = _active?.DatabaseCompatibility.BaselineId,
                MetadataSchemaVersion = _active?.DatabaseCompatibility.MetadataSchemaVersion,
                StructureCompatible = _active?.DatabaseCompatibility.StructureCompatible,
                MigrationRequired = _migrationRequired,
                MigrationBlocked = _migrationBlocked,
                DatabaseUnusable = _databaseUnusable,
            };
        }

        private static object Failure(string error)
        {
            return new { success = false, error = error ?? "数据源加载失败。" };
        }

        private sealed class ActiveEnvironment
        {
            public ActiveEnvironment(
                GmConfig config,
                GmService gm,
                PvfIndexService pvfIndex,
                DatabaseCompatibilityReport databaseCompatibility)
            {
                Config = config;
                Gm = gm;
                PvfIndex = pvfIndex;
                DatabaseCompatibility = databaseCompatibility;
            }

            public GmConfig Config { get; }
            public GmService Gm { get; }
            public PvfIndexService PvfIndex { get; }
            public DatabaseCompatibilityReport DatabaseCompatibility { get; }
        }
    }

    public sealed class RuntimeEnvironmentStatus
    {
        public bool Configured { get; set; }
        public bool Ready { get; set; }
        public bool Loading { get; set; }
        public string Database { get; set; }
        public string Pvf { get; set; }
        public string ServerBin { get; set; }
        public bool IndexReady { get; set; }
        public string IndexError { get; set; }
        public string Error { get; set; }
        public bool HasError { get; set; }
        public long? SchemaVersion { get; set; }
        public string BaselineId { get; set; }
        public long? MetadataSchemaVersion { get; set; }
        public bool? StructureCompatible { get; set; }
        public bool MigrationRequired { get; set; }
        public bool MigrationBlocked { get; set; }
        public bool DatabaseUnusable { get; set; }
    }
}
