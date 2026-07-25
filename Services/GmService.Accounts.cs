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
    public sealed partial class GmService
    {
        public object ListAccounts()
        {
            var result = new List<object>();
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    // GROUP_CONCAT 角色名: 前端用于按角色名反查账号
                    cmd.CommandText = @"
SELECT a.account_id, a.m_id, a.cera, a.token_cera, a.lucky_star,
       COUNT(c.character_id), COALESCE(GROUP_CONCAT(c.name, char(10)), '')
FROM accounts a
LEFT JOIN characters c ON c.account_id = a.account_id AND c.delete_flag = 0
GROUP BY a.account_id
ORDER BY a.account_id;";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var joined = reader.GetString(6);
                            result.Add(new
                            {
                                accountId = reader.GetInt32(0),
                                name = reader.GetString(1),
                                cera = reader.GetInt64(2),
                                tokenCera = reader.GetInt64(3),
                                luckyStar = reader.GetInt64(4),
                                characterCount = reader.GetInt32(5),
                                characterNames = joined.Length == 0
                                    ? new string[0]
                                    : joined.Split('\n'),
                            });
                        }
                    }
                }
            }
            return new { accounts = result };
        }

        // ── 账号共享数据(货币/晶块/账号金库) ──

        public object GetAccountDetail(int accountId, PvfIndexService pvfIndex)
        {
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();

                object currencies = null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT m_id, cera, token_cera, happy_token_cera, lucky_star, seria_luck_value
FROM accounts WHERE account_id = @aid;";
                    cmd.Parameters.AddWithValue("@aid", accountId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return Error("账号不存在: " + accountId);
                        currencies = new
                        {
                            name = reader.GetString(0),
                            cera = reader.GetInt64(1),
                            tokenCera = reader.GetInt64(2),
                            happyTokenCera = reader.GetInt64(3),
                            luckyStar = reader.GetInt64(4),
                            seriaLuck = reader.GetInt64(5),
                        };
                    }
                }

                var cubes = new List<object>();
                foreach (var cube in CurrencyService.LoadCubeFragments(conn, null, accountId))
                {
                    cubes.Add(new
                    {
                        itemId = cube.ItemId,
                        slot = cube.Slot,
                        name = pvfIndex.ResolveItemName(cube.ItemId),
                        count = cube.Count,
                    });
                }

                var cargo = new List<object>();
                object cargoState = null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT selection_key, value32, item_count
FROM account_cargo_state
WHERE account_id = @aid;";
                    cmd.Parameters.AddWithValue("@aid", accountId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            cargoState = new
                            {
                                selectionKey = reader.GetInt32(0),
                                value32 = reader.GetInt32(1),
                                itemCount = reader.GetInt32(2),
                            };
                        }
                    }
                }

                foreach (var item in _inventory.LoadAccountCargo(accountId))
                {
                    var templateId = item.ItemTemplateId;
                    var kind = pvfIndex.ResolveItemKind(templateId);
                    cargo.Add(new
                    {
                        slot = (int)item.SlotIndex,
                        templateId,
                        name = pvfIndex.ResolveItemName(templateId),
                        kind,
                        count = item.Count,
                        durability = (int)item.Core.Durability,
                    });
                }

                if (!_accountProgress.TryLoad(accountId, out var progress))
                    return Error("账号不存在: " + accountId);

                return new { accountId, currencies, cubes, cargo, cargoState, progress };
            }
        }

        public object SetAccountHonorLevel(int accountId, int level)
        {
            if (!_accountProgress.TrySetHonorLevel(accountId, level, out var progress, out var error))
                return Error(error);

            return new { success = true, accountId, progress };
        }

        public object MaxAccountHonorLevel(int accountId)
        {
            if (!_accountProgress.TryMaxHonorLevel(accountId, out var progress, out var error))
                return Error(error);

            return new { success = true, accountId, progress };
        }

        public object SetGrowthCapsuleExp(int accountId, long exp)
        {
            if (!_accountProgress.TrySetGrowthCapsuleExp(accountId, exp, out var progress, out var error))
                return Error(error);

            return new { success = true, accountId, progress };
        }

        public object MaxGrowthCapsuleExp(int accountId)
        {
            if (!_accountProgress.TryMaxGrowthCapsuleExp(accountId, out var progress, out var error))
                return Error(error);

            return new { success = true, accountId, progress };
        }

        public object AdjustAccountCurrency(int accountId, string type, int amount, long? setValue = null)
        {
            type = (type ?? string.Empty).Trim().ToLowerInvariant();

            // 覆写模式: 按当前值折算成差额, 仍走服务端的加扣入口
            if (setValue.HasValue)
            {
                if (setValue.Value < 0)
                    return Error("数值不能为负");

                long current;
                using (var conn = new SqliteConnection(_config.ConnectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var column = type switch
                        {
                            "cera" => "cera",
                            "token" => "token_cera",
                            "luckystar" => "lucky_star",
                            "serialuck" => "seria_luck_value",
                            _ => null,
                        };
                        if (column == null)
                            return Error("不支持的类型: " + type);
                        cmd.CommandText = $"SELECT {column} FROM accounts WHERE account_id = @aid;";
                        cmd.Parameters.AddWithValue("@aid", accountId);
                        var result = cmd.ExecuteScalar();
                        if (result == null || result == DBNull.Value)
                            return Error("账号不存在: " + accountId);
                        current = Convert.ToInt64(result);
                    }
                }

                var delta = setValue.Value - current;
                if (delta == 0)
                    return new { success = true, accountId, type, amount = 0 };
                if (delta < int.MinValue || delta > int.MaxValue)
                    return Error("差额超出范围");
                amount = (int)delta;
            }

            if (amount == 0)
                return Error("amount 不能为 0");

            // cera/token 的服务端入口按角色定位账号, 借该账号第一个角色
            if (type == "cera" || type == "token")
            {
                int characterId;
                if (!TryGetFirstCharacterId(accountId, out characterId))
                    return Error("该账号没有角色, 无法调整点券(服务端入口按角色定位)");
                return AdjustCera(characterId, amount, type == "token" ? "token" : "cera");
            }

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    if (type == "luckystar")
                    {
                        if (amount > 0)
                            CurrencyService.GrantLuckyStar(conn, tx, accountId, amount);
                        else if (!CurrencyService.TrySpendLuckyStar(conn, tx, accountId, -amount))
                            return Error("扣减失败(幸运星不足)");
                    }
                    else if (type == "serialuck")
                    {
                        // 简单计数列, 服务端没有独立入口
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "UPDATE accounts SET seria_luck_value = MAX(0, seria_luck_value + @d) WHERE account_id = @aid;";
                            cmd.Parameters.AddWithValue("@d", amount);
                            cmd.Parameters.AddWithValue("@aid", accountId);
                            if (cmd.ExecuteNonQuery() == 0)
                                return Error("账号不存在: " + accountId);
                        }
                    }
                    else
                    {
                        return Error("不支持的类型: " + type + " (可用: cera/token/luckyStar/seriaLuck)");
                    }

                    tx.Commit();
                }
            }

            return new { success = true, accountId, type, amount };
        }

        public object AdjustCubeFragment(int accountId, int itemId, int amount, long? setValue = null)
        {
            if (!CurrencyService.IsCubeFragment(itemId))
                return Error(itemId + " 不是晶块(可用: 3033黑 3034白 3035红 3036蓝 3037透明 3262金)");

            if (setValue.HasValue)
            {
                if (setValue.Value < 0)
                    return Error("数值不能为负");

                long current = 0;
                using (var conn = new SqliteConnection(_config.ConnectionString))
                {
                    conn.Open();
                    foreach (var cube in CurrencyService.LoadCubeFragments(conn, null, accountId))
                    {
                        if (cube.ItemId == itemId) { current = cube.Count; break; }
                    }
                }

                var delta = setValue.Value - current;
                if (delta == 0)
                    return new { success = true, accountId, itemId, amount = 0 };
                if (delta < int.MinValue || delta > int.MaxValue)
                    return Error("差额超出范围");
                amount = (int)delta;
            }

            if (amount == 0)
                return Error("amount 不能为 0");

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    if (amount < 0)
                    {
                        var current = 0;
                        foreach (var cube in CurrencyService.LoadCubeFragments(conn, tx, accountId))
                        {
                            if (cube.ItemId == itemId) { current = cube.Count; break; }
                        }
                        if (current + amount < 0)
                            return Error("扣减失败(当前只有 " + current + ")");
                    }

                    CurrencyService.AddCubeFragment(conn, tx, accountId, itemId, amount);
                    tx.Commit();
                }
            }

            return new { success = true, accountId, itemId, amount };
        }

        // 与服务端 InventoryDbPrimitives.DeleteAccountCargoItem 同语义(该原语未公开, 表结构简单无关联行)
        public object DeleteAccountCargoAt(int accountId, int slot)
        {
            if (_inventory.DeleteAccountCargoAt(accountId, (short)slot) == 0)
                return Error("该槽位没有物品");
            return new { success = true, accountId, slot };
        }

        public object ClearAccountCargo(int accountId)
        {
            var deleted = _inventory.ClearAccountCargo(accountId);
            return new { success = true, accountId, deleted };
        }

        public object MaxAccountCargo(int accountId)
        {
            const int MaxCargoSelectionKey = 64;

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
UPDATE account_cargo_state
SET selection_key = @selectionKey
WHERE account_id = @aid;";
                    cmd.Parameters.AddWithValue("@selectionKey", MaxCargoSelectionKey);
                    cmd.Parameters.AddWithValue("@aid", accountId);
                    var updated = cmd.ExecuteNonQuery();
                    if (updated == 0)
                        return Error("账号仓库状态不存在: account_id=" + accountId);

                    return new
                    {
                        success = true,
                        accountId,
                        listType = (int)InventoryListType.AccountCargo,
                        listParam16 = MaxCargoSelectionKey,
                        affectedRows = updated,
                    };
                }
            }
        }
    }
}
