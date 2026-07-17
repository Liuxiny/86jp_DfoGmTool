using System;
using System.Collections.Generic;
using System.Linq;
using DfoGmTool.ServerCore.Game.TitleBook;
using DfoGmTool.ServerCore.Game.Characters;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.Dungeon;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.Quests;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    // 所有写操作都走服务端自己的业务代码(SqliteAssetService/CharacterProgressService/QuestRepository),
    // GM 工具不自己拼物品数据。读操作用只读 SQL。
    // partial 按域拆分: Accounts(账号/货币/晶块/金库) Characters(角色/属性/转职)
    // Inventory(背包/发放/删除/钱包) Quests(任务总览/完成链) TitleBook(称号簿)
    public sealed partial class GmService
    {
        private static readonly string[] JobNames =
        {
            "鬼剑士", "格斗家", "神枪手", "魔法师", "圣职者",
            "女神枪手", "暗夜使者", "男格斗家", "男魔法师", "黑暗武士",
            "缔造者", "女鬼剑士", "守护者",
        };

        private readonly GmConfig _config;
        private readonly PvfIndexService _pvfIndex;
        private readonly SqliteInventoryStore _store;
        private readonly SqliteAssetService _assetService;
        private readonly SupplementalItemExpirationService _supplementalItemExpiration;
        private readonly Lazy<TitleBookMutationService> _titleBookMutation;
        private readonly AccountProgressService _accountProgress;

        internal static void ResetPvfStaticData()
        {
            lock (_titleBookLock)
                _titleBookSlots = null;
        }

        public GmService(GmConfig config, PvfIndexService pvfIndex)
        {
            _config = config;
            _pvfIndex = pvfIndex;
            _store = new SqliteInventoryStore(config.DatabasePath, config.SchemaPath);
            _assetService = new SqliteAssetService(config.DatabasePath, config.SchemaPath, _store);
            _supplementalItemExpiration = new SupplementalItemExpirationService(config.ConnectionString);
            _titleBookMutation = new Lazy<TitleBookMutationService>(
                () => new TitleBookMutationService(config.ConnectionString));
            _accountProgress = new AccountProgressService(config.DatabasePath, config.SchemaPath, config.PvfPath);
        }

        // 最终职业名(觉醒>转职>基础), PVF 索引没就绪时回退基础职业表
        private string DisplayJobName(int job, int growType)
        {
            var resolved = _pvfIndex.ResolveJobName(job, growType);
            if (!string.IsNullOrEmpty(resolved))
                return resolved;
            return job >= 0 && job < JobNames.Length ? JobNames[job] : "职业" + job;
        }

        private bool TryGetFirstCharacterId(int accountId, out int characterId)
        {
            characterId = 0;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT character_id FROM characters WHERE account_id = @aid ORDER BY character_id LIMIT 1;";
                    cmd.Parameters.AddWithValue("@aid", accountId);
                    var value = cmd.ExecuteScalar();
                    if (value == null || value == DBNull.Value)
                        return false;
                    characterId = Convert.ToInt32(value);
                    return true;
                }
            }
        }

        private bool TryGetAccountId(int characterId, out int accountId)
        {
            accountId = 0;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT account_id FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    var value = cmd.ExecuteScalar();
                    if (value == null || value == DBNull.Value)
                        return false;
                    accountId = Convert.ToInt32(value);
                    return true;
                }
            }
        }

        private static object Error(string message)
        {
            return new { success = false, error = message };
        }
    }
}
