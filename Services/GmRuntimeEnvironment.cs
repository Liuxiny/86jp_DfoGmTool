using System;
using System.Collections.Generic;
using System.Threading;
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
                    var databaseCompatibility = VerifyDataSource(config);

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

        private static DatabaseCompatibilityReport VerifyDataSource(
            GmConfig config)
        {
            var errors = new List<string>();
            DatabaseCompatibilityReport databaseCompatibility = null;
            AddVerificationError(
                errors,
                "数据库",
                () => databaseCompatibility =
                    DatabaseCompatibilityGuard.Validate(config.DatabasePath));
            AddVerificationError(errors, "PVF", () => VerifyPvf(config));
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            return databaseCompatibility;
        }

        private static void AddVerificationError(List<string> errors, string label, Action verify)
        {
            try
            {
                verify();
            }
            catch (Exception ex)
            {
                errors.Add(label + "校验失败: " + ex.GetBaseException().Message);
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
                MinimumSupportedSchemaVersion =
                    DatabaseCompatibilityGuard.MinimumSupportedVersion,
                MaximumSupportedSchemaVersion =
                    DatabaseCompatibilityGuard.MaximumSupportedVersion,
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
        public int MinimumSupportedSchemaVersion { get; set; }
        public int MaximumSupportedSchemaVersion { get; set; }
    }
}
