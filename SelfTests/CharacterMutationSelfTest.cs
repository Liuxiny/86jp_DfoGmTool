using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using DfoGmTool.ServerCore.Game.Dungeon;
using DfoGmTool.ServerCore.GameWorld;
using DfoGmTool.ServerCore.Infrastructure;
using DfoGmTool.Services;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.SelfTests
{
    internal static class CharacterMutationSelfTest
    {
        private const int AccountId = 926014;
        private const int CharacterId = 926014;
        private static int _failures;

        internal static int Run()
        {
            _failures = 0;
            Console.WriteLine("=== CHARACTER_MUTATIONS selftest ===");

            var tempDb = Path.Combine(Path.GetTempPath(), "dfogm-character-mutations-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var schema = Path.Combine(AppContext.BaseDirectory, "ServerCore", "Sqlite", "item_schema.sql");
                var pvf = ResolveLatestServerPvf();
                if (pvf == null)
                {
                    Check("latest server PVF exists", false);
                    return 1;
                }

                SqliteDatabaseBootstrap.Initialize(tempDb, schema);
                SeedCharacter(tempDb);

                if (!GmConfig.TryCreate(tempDb, pvf, out var config, out var error))
                {
                    Check("GM config can load temp db and PVF", false, error);
                    return 1;
                }

                PvfArchiveAccessor.Configure(pvf);
                PvfRuntimeCache.ResetForPvfChange();
                GmService.ResetPvfStaticData();

                var pvfIndex = new PvfIndexService(pvf);
                pvfIndex.WarmInBackground();
                WaitForIndex(pvfIndex);

                var gm = new GmService(config, pvfIndex);
                CheckLevelAndExperience(gm, tempDb);
                CheckSpTpSync(gm, tempDb);
                CheckJobGrowAndSkillReset(gm, tempDb);
                CheckDeleteCharacterSeedFallback(gm, tempDb);

                Console.WriteLine(_failures == 0
                    ? "CharacterMutationSelfTest OK"
                    : $"CharacterMutationSelfTest FAIL: {_failures}");
                return _failures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("CharacterMutationSelfTest EXCEPTION: " + ex);
                return 1;
            }
            finally
            {
                if (_failures == 0)
                {
                    try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
                }
                else
                {
                    Console.Error.WriteLine("Preserved temp db: " + tempDb);
                }
            }
        }

        private static void CheckLevelAndExperience(GmService gm, string dbPath)
        {
            var result = gm.SetLevel(CharacterId, 50);
            Check("SetLevel returns success", IsSuccess(result));

            using (var conn = Open(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT level, exp FROM characters WHERE character_id=@cid;";
                cmd.Parameters.AddWithValue("@cid", CharacterId);
                using (var reader = cmd.ExecuteReader())
                {
                    Check("character row exists after SetLevel", reader.Read());
                    var level = reader.GetInt32(0);
                    var exp = (uint)reader.GetInt64(1);
                    Check("level is persisted as requested", level == 50);
                    Check("exp threshold resolves back to requested level", ExpTableProvider.ApplyLevelUps(1, exp) == 50);
                    Check("level 50 exp equals threshold 49", exp == ExpTableProvider.GetLevelThreshold(49));
                }
            }
        }

        private static void CheckSpTpSync(GmService gm, string dbPath)
        {
            var before = LoadSkillPoints(dbPath);
            var result = gm.AdjustSpTpSynced(CharacterId, 100, 5);
            Check("AdjustSpTpSynced returns success", IsSuccess(result));
            var after = LoadSkillPoints(dbPath);
            var bonusSp = LoadInt(dbPath, "SELECT bonus_sp FROM characters WHERE character_id=926014");
            var bonusTp = LoadInt(dbPath, "SELECT bonus_tp FROM characters WHERE character_id=926014");
            var tail0 = LoadInt(dbPath, "SELECT tail0 FROM character_skill_tail WHERE character_id=926014");
            var tail1 = LoadInt(dbPath, "SELECT tail1 FROM character_skill_tail WHERE character_id=926014");

            Check("bonus SP increased", bonusSp == 110, "got " + bonusSp);
            Check("bonus TP increased", bonusTp == 8, "got " + bonusTp);
            Check("skill point row total SP updated", after.TotalSp == before.TotalSp + 100);
            Check("skill point row remaining SP updated", after.RemainingSp == before.RemainingSp + 100);
            Check("skill point row TP updated", after.TotalTp == before.TotalTp + 5 && after.RemainingTp == before.RemainingTp + 5);
            Check("skill tail mirrors TP", tail0 == after.RemainingTp, "got " + tail0 + ", want " + after.RemainingTp);
            Check("skill tail keeps latest server compatibility zero", tail1 == 0, "got " + tail1);
        }

        private static void CheckJobGrowAndSkillReset(GmService gm, string dbPath)
        {
            var result = gm.SetGrowTypeFixed(CharacterId, 0, 1, 1);
            Check("SetGrowTypeFixed returns success", IsSuccess(result));
            var growType = LoadInt(dbPath, "SELECT grow_type FROM characters WHERE character_id=926014");
            var oldSkills = LoadInt(dbPath, "SELECT COUNT(*) FROM character_skills WHERE character_id=926014 AND skill_id=999");
            var skill33 = LoadInt(dbPath, "SELECT COUNT(*) FROM character_skills WHERE character_id=926014 AND skill_id=33");
            var skill197 = LoadInt(dbPath, "SELECT COUNT(*) FROM character_skills WHERE character_id=926014 AND skill_id=197");
            var flag101 = LoadInt(dbPath, "SELECT COUNT(*) FROM character_invisible_falgs WHERE character_id=926014 AND slot_index=101 AND flag_value=1");
            Check("grow_type packed as first + awakening", growType == 17, "got " + growType);
            Check("old skill residue removed", oldSkills == 0, "got " + oldSkills);
            Check("awakening grant skill 33 exists", skill33 > 0, "got " + skill33);
            Check("awakening grant skill 197 exists", skill197 > 0, "got " + skill197);
            Check("default promoted quest flag set", flag101 == 1, "got " + flag101);
            Check("skill points reset to full after class change",
                LoadInt(dbPath, "SELECT total_sp - remaining_sp FROM character_skill_points WHERE character_id=926014") == 0);

            var invalid = gm.SetGrowTypeFixed(CharacterId, 0, 0, 1);
            Check("invalid awakening without first grow is rejected", !IsSuccess(invalid));
        }

        private static void CheckDeleteCharacterSeedFallback(GmService gm, string dbPath)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, @"
INSERT INTO characters(character_id, account_id, name, job, grow_type, level, exp)
VALUES(926015, 926014, 'character-delete-seed-fallback', 0, 0, 1, 0);");
                Exec(conn, tx, "UPDATE get_userinfo_template SET seed_character_id = 926014 WHERE id = 1;");
                tx.Commit();
            }

            var result = gm.DeleteCharacterPermanently(CharacterId, "删除角色");
            Check("DeleteCharacterPermanently returns success", IsSuccess(result));
            Check("deleted character row removed",
                LoadInt(dbPath, "SELECT COUNT(1) FROM characters WHERE character_id=926014") == 0);
            Check("delete replacement seed uses same account survivor",
                LoadInt(dbPath, "SELECT seed_character_id FROM get_userinfo_template WHERE id=1") == 926015);
            Check("same account survivor remains active",
                LoadInt(dbPath, "SELECT COUNT(1) FROM characters WHERE character_id=926015 AND delete_flag=0") == 1);
        }

        private static void SeedCharacter(string dbPath)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "INSERT INTO accounts(account_id, m_id, password_hash) VALUES(926014, 'character-mutation-selftest', '');");
                Exec(conn, tx, @"
INSERT INTO characters(character_id, account_id, name, job, grow_type, level, exp, bonus_sp, bonus_tp)
VALUES(926014, 926014, 'character-mutation-selftest', 0, 0, 60, 0, 10, 3);");
                Exec(conn, tx, "INSERT INTO character_subtype1_fields(character_id) VALUES(926014);");
                Exec(conn, tx, "INSERT INTO character_init_flags(character_id) VALUES(926014);");
                Exec(conn, tx, "INSERT INTO character_skill_tail(character_id, tail0, tail1) VALUES(926014, 3, 0);");
                Exec(conn, tx, @"
INSERT INTO character_skills(character_id, page_index, page_header, slot, skill_id, level) VALUES
(926014, 0, 1000, 5, 999, 1),
(926014, 1, 1000, 5, 999, 1);");
                tx.Commit();
            }
        }

        private static string ResolveLatestServerPvf()
        {
            foreach (var root in EnumerateSearchRoots())
            {
                var candidates = new[]
                {
                    Path.Combine(root, "Codes", "ServerS4A12_260714", "Server", "DfoServer", "bin", "Debug", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "Codes", "ServerS4A12_260714", "Server", "DfoServer", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "Codes", "ServerS4A12_260714", "dist", "linux-x64", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "ServerS4A12_260714", "Server", "DfoServer", "bin", "Debug", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "ServerS4A12_260714", "Server", "DfoServer", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "ServerS4A12_260714", "dist", "linux-x64", "Data", "Pvf", "Script.pvf"),
                };
                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                        return path;
                }
            }
            return null;
        }

        private static string[] EnumerateSearchRoots()
        {
            var roots = new List<string>();
            AddRoot(roots, Directory.GetCurrentDirectory());
            AddRoot(roots, AppContext.BaseDirectory);

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                AddRoot(roots, dir.FullName);

            return roots.ToArray();
        }

        private static void AddRoot(List<string> roots, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }
            if (!roots.Contains(path))
                roots.Add(path);
        }

        private static void WaitForIndex(PvfIndexService index)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!index.IsReady && string.IsNullOrWhiteSpace(index.BuildError) && DateTime.UtcNow < deadline)
                Thread.Sleep(100);
            Check("PVF index ready", index.IsReady && string.IsNullOrWhiteSpace(index.BuildError), index.BuildError);
        }

        private static SqliteConnection Open(string dbPath)
        {
            var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
            conn.Open();
            return conn;
        }

        private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
        {
            using (var cmd = new SqliteCommand(sql, conn, tx))
                cmd.ExecuteNonQuery();
        }

        private static int LoadInt(string dbPath, string sql)
        {
            using (var conn = Open(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static (int TotalSp, int RemainingSp, int TotalTp, int RemainingTp) LoadSkillPoints(string dbPath)
        {
            using (var conn = Open(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT total_sp, remaining_sp, total_tp, remaining_tp FROM character_skill_points WHERE character_id=926014;";
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                        return (0, 0, 0, 0);
                    return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
                }
            }
        }

        private static bool IsSuccess(object result)
        {
            if (result == null)
                return false;
            var prop = result.GetType().GetProperty("success", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return prop != null && Convert.ToBoolean(prop.GetValue(result));
        }

        private static void Check(string name, bool condition, string error = null)
        {
            if (condition)
            {
                Console.WriteLine("PASS " + name);
                return;
            }

            _failures++;
            Console.Error.WriteLine("FAIL " + name + (string.IsNullOrWhiteSpace(error) ? string.Empty : ": " + error));
        }
    }
}
