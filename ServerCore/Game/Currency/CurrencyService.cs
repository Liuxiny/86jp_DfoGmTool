using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Currency
{
    public static class CurrencyService
    {
        // ── Cube Fragment (晶块) ──────────────────────────────
        // 6 种小晶块是账号共享、固定 slot 的物品。
        // item_id → (accounts 列名, 固定 slot)
        private static readonly Dictionary<int, (string ColumnName, int Slot)> CubeFragmentMap = new Dictionary<int, (string, int)>
        {
            { 3033, ("cube_black", 354) },
            { 3034, ("cube_white", 355) },
            { 3035, ("cube_red",   356) },
            { 3036, ("cube_blue",  357) },
            { 3037, ("cube_clear", 358) },
            { 3262, ("cube_gold",  359) },
        };

        // 晶块固定 slot 范围 (FindEmptySlot 保护用)
        public const int CubeFragmentSlotStart = 354;
        public const int CubeFragmentSlotEnd = 359;

        public static bool IsCubeFragment(int itemId) => CubeFragmentMap.ContainsKey(itemId);

        public static int GetCubeFragmentSlot(int itemId)
        {
            if (CubeFragmentMap.TryGetValue(itemId, out var entry))
                return entry.Slot;
            return -1;
        }

        public static int GetCubeFragmentItemIdFromSlot(int slot)
        {
            foreach (var kv in CubeFragmentMap)
            {
                if (kv.Value.Slot == slot)
                    return kv.Key;
            }
            return -1;
        }

        public static bool IsCubeFragmentSlot(int slot)
        {
            return slot >= CubeFragmentSlotStart && slot <= CubeFragmentSlotEnd;
        }

        /// <summary>
        /// 读取账号的 6 种晶块数量, 返回 (itemId, slot, count) 列表。
        /// </summary>
        public static List<(int ItemId, int Slot, int Count)> LoadCubeFragments(SqliteConnection conn, SqliteTransaction tx, int accountId)
        {
            var result = new List<(int, int, int)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT cube_black, cube_white, cube_red, cube_blue, cube_clear, cube_gold FROM accounts WHERE account_id = @aid;";
                cmd.Parameters.AddWithValue("@aid", accountId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        result.Add((3033, 354, reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0))));
                        result.Add((3034, 355, reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1))));
                        result.Add((3035, 356, reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2))));
                        result.Add((3036, 357, reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3))));
                        result.Add((3037, 358, reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4))));
                        result.Add((3262, 359, reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5))));
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 累加指定晶块到账号。
        /// </summary>
        public static void AddCubeFragment(SqliteConnection conn, SqliteTransaction tx, int accountId, int itemId, int count)
        {
            if (!CubeFragmentMap.TryGetValue(itemId, out var entry))
                throw new ArgumentException($"itemId {itemId} is not a cube fragment");

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE accounts SET {entry.ColumnName} = {entry.ColumnName} + @count WHERE account_id = @aid;";
                cmd.Parameters.AddWithValue("@count", count);
                cmd.Parameters.AddWithValue("@aid", accountId);
                cmd.ExecuteNonQuery();
            }
        }



        /// <summary>
        /// 启动时迁移: 把 character_items slot 354-359 的旧晶块数量归集到 accounts 表, 然后删除旧行。
        /// 幂等: 只在 accounts 对应列为 0 且 character_items 有数据时才迁移。
        /// </summary>
        public static void MigrateCubeFragmentsFromCharacterItems(SqliteConnection conn)
        {
            // 检查是否有待迁移的数据(任何角色在 slot 354-359 有晶块)
            bool hasOldData;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT COUNT(*) FROM character_items
WHERE list_type = 0 AND slot_index >= 354 AND slot_index <= 359
  AND item_template_id IN (3033, 3034, 3035, 3036, 3037, 3262);";
                hasOldData = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
            if (!hasOldData)
                return;

            // 检查是否已经迁移过(accounts 表已有非零晶块)
            bool alreadyMigrated;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT COUNT(*) FROM accounts
WHERE cube_black != 0 OR cube_white != 0 OR cube_red != 0
   OR cube_blue != 0 OR cube_clear != 0 OR cube_gold != 0;";
                alreadyMigrated = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
            if (alreadyMigrated)
            {
                // 已迁移但旧行残留, 清理旧行
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
DELETE FROM character_items
WHERE list_type = 0 AND slot_index >= 354 AND slot_index <= 359
  AND item_template_id IN (3033, 3034, 3035, 3036, 3037, 3262);";
                    cmd.ExecuteNonQuery();
                }
                return;
            }

            // 对每个 account, 从其角色的 character_items 中读取晶块数据
            // (单账号项目: 取角色中各晶块的 MAX stack_count 做为账号值)
            foreach (var kv in CubeFragmentMap)
            {
                var itemId = kv.Key;
                var colName = kv.Value.ColumnName;
                var slot = kv.Value.Slot;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $@"
UPDATE accounts
SET {colName} = COALESCE((
    SELECT MAX(ci.stack_count)
    FROM character_items ci
    JOIN characters ch ON ch.character_id = ci.character_id
    WHERE ch.account_id = accounts.account_id
      AND ci.list_type = 0 AND ci.slot_index = @slot
      AND ci.item_template_id = @itemId
), 0)
WHERE {colName} = 0;";
                    cmd.Parameters.AddWithValue("@slot", slot);
                    cmd.Parameters.AddWithValue("@itemId", itemId);
                    cmd.ExecuteNonQuery();
                }
            }

            // 删除旧的 character_items 行
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
DELETE FROM character_items
WHERE list_type = 0 AND slot_index >= 354 AND slot_index <= 359
  AND item_template_id IN (3033, 3034, 3035, 3036, 3037, 3262);";
                cmd.ExecuteNonQuery();
            }
        }

        public static WalletSnapshot LoadWallet(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            var w = new WalletSnapshot();
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT stack_count FROM character_items WHERE character_id = @cid AND list_type = 0 AND slot_index = 0;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    w.Gold = Convert.ToInt32(result);
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
SELECT a.cera, a.token_cera, a.happy_token_cera, a.lucky_star
FROM accounts a
JOIN characters c ON c.account_id = a.account_id
WHERE c.character_id = @cid;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        w.Cera = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                        w.TokenCera = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                        w.HappyTokenCera = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
                        w.LuckyStar = reader.IsDBNull(3) ? (ushort)0 : NormalizeLuckyStar(Convert.ToInt32(reader.GetValue(3)));
                    }
                }
            }
            // 读取账号级晶块
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
SELECT a.cube_black, a.cube_white, a.cube_red, a.cube_blue, a.cube_clear, a.cube_gold
FROM accounts a
JOIN characters c ON c.account_id = a.account_id
WHERE c.character_id = @cid;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        w.CubeBlack = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                        w.CubeWhite = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                        w.CubeRed = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
                        w.CubeBlue = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));
                        w.CubeClear = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4));
                        w.CubeGold = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5));
                    }
                }
            }
            return w;
        }

        // ── Grant / TrySpend ──────────────────────────────────────────────
        // 货币写入唯一入口。发放=SQL原子增量; 扣费=条件扣减(余额不足返回false, 绝不clamp到0)。
        // 绝对值SET的旧 Update* 已全部删除, 任何路径不得整值覆盖钱包列。

        public static void GrantGold(SqliteConnection connection, SqliteTransaction transaction, int characterId, int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "GrantGold amount must be >= 0; use TrySpendGold to deduct");
            if (amount == 0)
                return;

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
UPDATE character_items
SET stack_count = stack_count + @amt,
    instance_value = instance_value + @amt
WHERE character_id = @cid AND list_type = 0 AND slot_index = 0;";
                cmd.Parameters.AddWithValue("@amt", amount);
                cmd.Parameters.AddWithValue("@cid", characterId);
                if (cmd.ExecuteNonQuery() > 0)
                    return;
            }

            // 金币行不存在(新角色): 建行, 初值即发放额
            InsertCurrencySlotRow(connection, transaction, characterId, 0, amount);
        }

        public static bool TrySpendGold(SqliteConnection connection, SqliteTransaction transaction, int characterId, int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "TrySpendGold amount must be >= 0");
            if (amount == 0)
                return true;

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
UPDATE character_items
SET stack_count = stack_count - @amt,
    instance_value = instance_value - @amt
WHERE character_id = @cid AND list_type = 0 AND slot_index = 0
  AND stack_count >= @amt;";
                cmd.Parameters.AddWithValue("@amt", amount);
                cmd.Parameters.AddWithValue("@cid", characterId);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public static void GrantCera(SqliteConnection connection, SqliteTransaction transaction, int characterId, int amount)
            => GrantAccountCurrency(connection, transaction, characterId, "cera", amount);

        public static bool TrySpendCera(SqliteConnection connection, SqliteTransaction transaction, int characterId, int amount)
            => TrySpendAccountCurrency(connection, transaction, characterId, "cera", amount);

        public static void GrantTokenCera(SqliteConnection connection, SqliteTransaction transaction, int characterId, int amount)
            => GrantAccountCurrency(connection, transaction, characterId, "token_cera", amount);

        public static bool TrySpendTokenCera(SqliteConnection connection, SqliteTransaction transaction, int characterId, int amount)
            => TrySpendAccountCurrency(connection, transaction, characterId, "token_cera", amount);

        public static void GrantHappyTokenCera(SqliteConnection connection, SqliteTransaction transaction, int characterId, int amount)
            => GrantAccountCurrency(connection, transaction, characterId, "happy_token_cera", amount);

        public static bool TrySpendHappyTokenCera(SqliteConnection connection, SqliteTransaction transaction, int characterId, int amount)
            => TrySpendAccountCurrency(connection, transaction, characterId, "happy_token_cera", amount);

        // 幸运星按账号ID直接寻址(accounts.lucky_star), 上限999在SQL内钳制
        public static void GrantLuckyStar(SqliteConnection connection, SqliteTransaction transaction, int accountId, int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "GrantLuckyStar amount must be >= 0; use TrySpendLuckyStar to deduct");
            if (amount == 0)
                return;

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
UPDATE accounts
SET lucky_star = MIN(999, lucky_star + @amt)
WHERE account_id = @aid;";
                cmd.Parameters.AddWithValue("@amt", amount);
                cmd.Parameters.AddWithValue("@aid", accountId);
                cmd.ExecuteNonQuery();
            }
        }

        public static bool TrySpendLuckyStar(SqliteConnection connection, SqliteTransaction transaction, int accountId, int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "TrySpendLuckyStar amount must be >= 0");
            if (amount == 0)
                return true;

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
UPDATE accounts
SET lucky_star = lucky_star - @amt
WHERE account_id = @aid AND lucky_star >= @amt;";
                cmd.Parameters.AddWithValue("@amt", amount);
                cmd.Parameters.AddWithValue("@aid", accountId);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        private static void GrantAccountCurrency(SqliteConnection connection, SqliteTransaction transaction, int characterId, string column, int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), amount, $"Grant {column} amount must be >= 0; use TrySpend to deduct");
            if (amount == 0)
                return;

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = $@"
UPDATE accounts
SET {column} = {column} + @amt
WHERE account_id = (SELECT account_id FROM characters WHERE character_id = @cid);";
                cmd.Parameters.AddWithValue("@amt", amount);
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.ExecuteNonQuery();
            }
        }

        private static bool TrySpendAccountCurrency(SqliteConnection connection, SqliteTransaction transaction, int characterId, string column, int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), amount, $"TrySpend {column} amount must be >= 0");
            if (amount == 0)
                return true;

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = $@"
UPDATE accounts
SET {column} = {column} - @amt
WHERE account_id = (SELECT account_id FROM characters WHERE character_id = @cid)
  AND {column} >= @amt;";
                cmd.Parameters.AddWithValue("@amt", amount);
                cmd.Parameters.AddWithValue("@cid", characterId);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        private static ushort NormalizeLuckyStar(int value)
        {
            if (value <= 0)
                return 0;
            if (value > 999)
                return 999;
            return (ushort)value;
        }

        // 前置条件: 同事务内已确认该槽位无行(UPDATE命中0行)。
        // 用普通INSERT而非OR REPLACE: REPLACE会把未列出的列(pet_serial_or_handle/equipment_lock_id/extra_json)清掉。
        private static void InsertCurrencySlotRow(SqliteConnection connection, SqliteTransaction transaction, int characterId, int slot, int value)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"INSERT INTO character_items
(owner_scope, owner_id, character_id, list_type, slot_index, item_template_id, item_kind, stack_count, instance_value, durability, seal_flag, option_value, expire_time, marker_16)
VALUES ('character', @cid, @cid, 0, @slot, 0, 'special', @val, @val, 0, 0, 0, 0, 0);";
                cmd.Parameters.AddWithValue("@val", value);
                cmd.Parameters.AddWithValue("@slot", slot);
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.ExecuteNonQuery();
            }
        }

    }

    public sealed class WalletSnapshot
    {
        public int Gold { get; set; }
        public int Cera { get; set; }

        // 技能点(character_items slot 2), 仅 InventoryDbPrimitives.LoadWallet 填充
        public int Sp { get; set; }

        public int TokenCera { get; set; }

        public int HappyTokenCera { get; set; }

        public ushort LuckyStar { get; set; }

        // 账号级晶块
        public int CubeBlack { get; set; }
        public int CubeWhite { get; set; }
        public int CubeRed { get; set; }
        public int CubeBlue { get; set; }
        public int CubeClear { get; set; }
        public int CubeGold { get; set; }
    }
}
