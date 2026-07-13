using System;
using System.Threading;
using DfoGmTool.ServerCore.GameWorld;
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

        public GmRuntimeEnvironment(GmConfig initialConfig)
        {
            if (initialConfig != null)
                Configure(initialConfig);
        }

        public object GetStatus()
        {
            _gate.EnterReadLock();
            try
            {
                return BuildStatus();
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
                    VerifyDatabase(config);
                    VerifyPvf(config);

                    // Construct the new services before replacing the live source.
                    var pvfIndex = new PvfIndexService(config.PvfPath);
                    var gm = new GmService(config, pvfIndex);

                    Environment.SetEnvironmentVariable("PVF_ARCHIVE_PATH", config.PvfPath);
                    Environment.SetEnvironmentVariable("INVENTORY_DATABASE_PATH", config.DatabasePath);
                    PvfArchiveAccessor.Configure(config.PvfPath);
                    PvfRuntimeCache.ResetForPvfChange();
                    GmService.ResetPvfStaticData();

                    _active = new ActiveEnvironment(config, gm, pvfIndex);
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

        private static void VerifyDatabase(GmConfig config)
        {
            using (var connection = new SqliteConnection(config.ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT 1 FROM sqlite_master LIMIT 1;";
                    command.ExecuteScalar();
                }
            }
        }

        private static void VerifyPvf(GmConfig config)
        {
            using (var archive = PvfArchive.Open(config.PvfPath))
            {
                if (string.IsNullOrWhiteSpace(archive.GetFileContent("stackable/stackable.lst")))
                    throw new InvalidOperationException("所选 PVF 缺少 stackable/stackable.lst。");
            }
        }

        private object BuildStatus()
        {
            var config = _active?.Config;
            var index = _active?.PvfIndex;
            var indexError = index?.BuildError;
            var ready = index != null && index.IsReady && string.IsNullOrWhiteSpace(indexError);
            return new
            {
                configured = config != null,
                ready,
                loading = config != null && !ready && string.IsNullOrWhiteSpace(indexError),
                database = config?.DatabasePath,
                pvf = config?.PvfPath,
                serverBin = config?.ServerBinDir,
                indexReady = index?.IsReady ?? false,
                indexError,
                error = config == null ? _startupError : indexError,
            };
        }

        private static object Failure(string error)
        {
            return new { success = false, error = error ?? "数据源加载失败。" };
        }

        private sealed class ActiveEnvironment
        {
            public ActiveEnvironment(GmConfig config, GmService gm, PvfIndexService pvfIndex)
            {
                Config = config;
                Gm = gm;
                PvfIndex = pvfIndex;
            }

            public GmConfig Config { get; }
            public GmService Gm { get; }
            public PvfIndexService PvfIndex { get; }
        }
    }
}
