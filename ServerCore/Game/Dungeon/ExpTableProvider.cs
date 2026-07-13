using System;
using System.Collections.Generic;
using DfoGmTool.ServerCore.GameWorld;

namespace DfoGmTool.ServerCore.Game.Dungeon
{
    public static class ExpTableProvider
    {
        // 服务端等级上限(86 版)。
        public const int MaxLevel = 86;

        private static readonly object _lock = new object();
        private static int[] _levelThresholds;
        private static int[] _monsterBaseExp;
        private static int[] _monsterGold;
        private static int[] _monsterGoldVariance;

        internal static void ResetForPvfChange()
        {
            lock (_lock)
            {
                _levelThresholds = null;
                _monsterBaseExp = null;
                _monsterGold = null;
                _monsterGoldVariance = null;
            }
        }

        // 按经验阈值表结算连续升级, 返回结算后的等级。
        // 四条经验入口(打怪/通关结算/教程/交任务)共用, 升级规则只在这里改。
        public static byte ApplyLevelUps(int level, uint totalExp)
        {
            while (level < MaxLevel && totalExp >= (uint)GetLevelThreshold(level))
                level++;
            return (byte)level;
        }

        public static int GetLevelThreshold(int level)
        {
            EnsureLoaded();
            if (level < 1 || level > _levelThresholds.Length) return int.MaxValue;
            return _levelThresholds[level - 1];
        }

        public static int GetMonsterBaseExp(int monsterLevel)
        {
            EnsureLoaded();
            if (monsterLevel < 1 || monsterLevel > _monsterBaseExp.Length) return 0;
            return _monsterBaseExp[monsterLevel - 1];
        }

        public static int GetExpRewardBase(int level)
        {
            EnsureLoaded();
            if (level < 1 || level > _monsterBaseExp.Length) return 0;
            return _monsterBaseExp[level - 1];
        }

        public static int GetMonsterGold(int monsterLevel, out int variancePercent)
        {
            EnsureLoaded();
            if (monsterLevel < 1 || monsterLevel > _monsterGold.Length)
            {
                variancePercent = 0;
                return 0;
            }
            variancePercent = monsterLevel <= _monsterGoldVariance.Length
                ? _monsterGoldVariance[monsterLevel - 1] : 10;
            return _monsterGold[monsterLevel - 1];
        }

        private static void EnsureLoaded()
        {
            if (_levelThresholds != null) return;
            lock (_lock)
            {
                if (_levelThresholds != null) return;
                LoadAll();
            }
        }

        private static void LoadAll()
        {
            _levelThresholds = ParseExpTable();
            ParseQuestParameter(out _monsterBaseExp, out _monsterGold, out _monsterGoldVariance);
            FileLogger.Log($"[ExpTableProvider] Loaded: {_levelThresholds.Length} level thresholds, {_monsterBaseExp.Length} monster exp, {_monsterGold.Length} monster gold");
        }

        private static int[] ParseExpTable()
        {
            try
            {
                var text = PvfArchiveAccessor.ReadText("character/ExpTable.tbl");
                if (string.IsNullOrEmpty(text)) return Array.Empty<int>();

                var tokens = text.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var result = new int[tokens.Length];
                for (int i = 0; i < tokens.Length; i++)
                {
                    if (long.TryParse(tokens[i], out var v))
                        result[i] = v > int.MaxValue ? int.MaxValue : (int)v;
                }
                return result;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[ExpTableProvider] ERROR loading ExpTable.tbl: {ex.Message}");
                return Array.Empty<int>();
            }
        }

        private static void ParseQuestParameter(out int[] expTable, out int[] goldTable, out int[] goldVariance)
        {
            expTable = Array.Empty<int>();
            goldTable = Array.Empty<int>();
            goldVariance = Array.Empty<int>();

            try
            {
                var text = PvfArchiveAccessor.ReadText("n_quest/questParameter.etc");
                if (!string.IsNullOrEmpty(text))
                    expTable = ParseExpRewardTable(text);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[ExpTableProvider] ERROR loading questParameter.etc: {ex.Message}");
            }

            try
            {
                var text = PvfArchiveAccessor.ReadText("Etc/ItemDropInfo_Common.etc");
                if (!string.IsNullOrEmpty(text))
                    ParseGoldDropRefTable(text, out goldTable, out goldVariance);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[ExpTableProvider] ERROR loading ItemDropInfo_Common.etc: {ex.Message}");
            }
        }

        private static int[] ParseExpRewardTable(string text)
        {
            var startTag = "[exp reward table]";
            var endTag = "[gold reward table]";
            var start = text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return Array.Empty<int>();
            start += startTag.Length;

            var end = text.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = text.Length;

            var content = text.Substring(start, end - start).Trim();
            var tokens = content.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            
            var result = new List<int>();
            for (int i = 0; i < tokens.Length; i += 2)
            {
                if (int.TryParse(tokens[i], out var v))
                    result.Add(v);
            }
            return result.ToArray();
        }

        private static void ParseGoldDropRefTable(string text, out int[] goldTable, out int[] varianceTable)
        {
            goldTable = new int[200];
            varianceTable = new int[200];

            var startTag = "[gold drop ref table]";
            var start = text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return;
            start += startTag.Length;

            var end = text.IndexOf("[", start, StringComparison.Ordinal);
            if (end < 0) end = text.Length;

            var content = text.Substring(start, end - start).Trim();
            var tokens = content.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i + 2 < tokens.Length; i += 3)
            {
                if (int.TryParse(tokens[i], out int level) &&
                    int.TryParse(tokens[i + 1], out int gold) &&
                    int.TryParse(tokens[i + 2], out int variance) &&
                    level >= 1 && level <= 200)
                {
                    goldTable[level - 1] = gold;
                    varianceTable[level - 1] = variance;
                }
            }
        }
    }
}
