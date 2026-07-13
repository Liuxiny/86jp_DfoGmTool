using System;

namespace DfoGmTool.Services
{
    public sealed class AccountProgressSnapshot
    {
        public HonorProgressSnapshot Honor { get; set; }
        public GrowthCapsuleProgressSnapshot GrowthCapsule { get; set; }
    }

    public sealed class HonorProgressSnapshot
    {
        public long TotalExp { get; set; }
        public long MaxTotalExp { get; set; }
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        public long CurrentLevelExp { get; set; }
        public long CurrentLevelExpCap { get; set; }
    }

    public sealed class GrowthCapsuleProgressSnapshot
    {
        public long TotalExp { get; set; }
        public long RequiredExp { get; set; }
    }

    public sealed class AccountProgressService
    {
        private readonly AccountProgressRepository _repository;
        private readonly AccountProgressPvfData _pvfData;

        public AccountProgressService(string databasePath, string schemaPath, string pvfPath)
        {
            _repository = new AccountProgressRepository(databasePath, schemaPath);
            _pvfData = new AccountProgressPvfData(pvfPath);
        }

        public bool TryLoad(int accountId, out AccountProgressSnapshot snapshot)
        {
            snapshot = null;
            if (!_repository.TryLoad(accountId, out var record))
                return false;

            snapshot = BuildSnapshot(record, _pvfData.Get());
            return true;
        }

        public bool TrySetHonorLevel(int accountId, int level, out AccountProgressSnapshot snapshot, out string error)
        {
            var definition = _pvfData.Get();
            if (!definition.Honor.TryGetTotalExpAtLevelStart(level, out var totalExp))
            {
                snapshot = null;
                error = "目标荣誉等级不在当前 PVF 定义中。";
                return false;
            }

            return TrySetHonorExp(accountId, totalExp, out snapshot, out error);
        }

        public bool TryMaxHonorLevel(int accountId, out AccountProgressSnapshot snapshot, out string error)
        {
            var definition = _pvfData.Get();
            return TrySetHonorExp(accountId, definition.Honor.MaxTotalExp, out snapshot, out error);
        }

        public bool TrySetGrowthCapsuleExp(int accountId, long requestedExp, out AccountProgressSnapshot snapshot, out string error)
        {
            var definition = _pvfData.Get();
            return TrySetGrowthCapsuleExp(accountId, requestedExp, definition, out snapshot, out error);
        }

        public bool TryMaxGrowthCapsuleExp(int accountId, out AccountProgressSnapshot snapshot, out string error)
        {
            var definition = _pvfData.Get();
            return TrySetGrowthCapsuleExp(accountId, definition.GrowthCapsule.RequiredExp, definition, out snapshot, out error);
        }

        private bool TrySetGrowthCapsuleExp(
            int accountId,
            long requestedExp,
            AccountProgressDefinition definition,
            out AccountProgressSnapshot snapshot,
            out string error)
        {
            if (requestedExp < 0)
            {
                snapshot = null;
                error = "能量胶囊经验不能为负数。";
                return false;
            }

            var cappedExp = Math.Min(requestedExp, definition.GrowthCapsule.RequiredExp);
            if (!_repository.TrySetGrowthCapsuleExp(accountId, cappedExp))
            {
                snapshot = null;
                error = "账号不存在。";
                return false;
            }

            return TryLoadAfterWrite(accountId, out snapshot, out error);
        }

        private bool TrySetHonorExp(
            int accountId,
            long totalExp,
            out AccountProgressSnapshot snapshot,
            out string error)
        {
            if (!_repository.TrySetHonorExp(accountId, totalExp))
            {
                snapshot = null;
                error = "账号不存在。";
                return false;
            }

            return TryLoadAfterWrite(accountId, out snapshot, out error);
        }

        private bool TryLoadAfterWrite(int accountId, out AccountProgressSnapshot snapshot, out string error)
        {
            if (!TryLoad(accountId, out snapshot))
            {
                error = "账号写入后无法重新读取。";
                return false;
            }

            error = null;
            return true;
        }

        private static AccountProgressSnapshot BuildSnapshot(
            AccountProgressRecord record,
            AccountProgressDefinition definition)
        {
            var honor = definition.Honor.Resolve(record.HonorExp);
            var growthCapsuleExp = Math.Min(Math.Max(0L, record.GrowthCapsuleExp), definition.GrowthCapsule.RequiredExp);
            return new AccountProgressSnapshot
            {
                Honor = new HonorProgressSnapshot
                {
                    TotalExp = honor.TotalExp,
                    MaxTotalExp = definition.Honor.MaxTotalExp,
                    Level = honor.Level,
                    MaxLevel = definition.Honor.MaxLevel,
                    CurrentLevelExp = honor.CurrentLevelExp,
                    CurrentLevelExpCap = honor.CurrentLevelExpCap,
                },
                GrowthCapsule = new GrowthCapsuleProgressSnapshot
                {
                    TotalExp = growthCapsuleExp,
                    RequiredExp = definition.GrowthCapsule.RequiredExp,
                },
            };
        }
    }
}
