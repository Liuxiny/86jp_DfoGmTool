using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    /// <summary>
    /// </summary>
    public class QuestFile : PvfModelBase
    {
        #region 基本信息

        public string Name { get; set; }
        public string Grade { get; set; }
        public string Difficulty { get; set; }
        public string Type { get; set; }
        public int SubType { get; set; } = -1;
        public int[] Level { get; set; }
        public string Job { get; set; }
        public int GrowType { get; set; } = -1;
        public int GrowNumber { get; set; } = -1;

        #endregion

        #region NPC

        public int NpcIndex { get; set; } = -1;
        public int CompleteNpcIndex { get; set; } = -1;

        #endregion

        #region 对话文本

        public string DependMessage { get; set; }
        public string ConditionMessage { get; set; }
        public string SolveMessage { get; set; }

        #endregion

        #region 奖励

        public string RewardType { get; set; }
        public string RewardIntData { get; set; }
        public string RewardSelectionIntData { get; set; }
        public int GoldMultiple { get; set; } = -1;

        #endregion

        #region 条件/数据

        public string IntData { get; set; }
        public string PreRequiredQuest { get; set; }

        #endregion

        #region 关联

        public System.Collections.Generic.List<string> PreRequiredQuestGroups { get; set; }
        public System.Collections.Generic.List<string> PreRequiredQuestAnswerGroups { get; set; }
        public string PreRequiredQuestAnswer { get; set; }
        public string Dialog { get; set; }
        public string MonsterRewardItem { get; set; }
        public List<MonsterRewardItemEntry> MonsterRewardItems { get; set; } = new List<MonsterRewardItemEntry>();
        public string EnemyRewardItem { get; set; }
        public List<EnemyRewardItemEntry> EnemyRewardItems { get; set; } = new List<EnemyRewardItemEntry>();
        public string DungeonInfo { get; set; }
        public string SubstitutiveNames { get; set; }
        public string RelationQuest { get; set; }
        public string ExceptionQuest { get; set; }
        public string CollisionQuest { get; set; }
        public System.Collections.Generic.List<string> CollisionQuestGroups { get; set; }
        public string AccountCollisionQuest { get; set; }
        public System.Collections.Generic.List<string> AccountCollisionQuestGroups { get; set; }
        public string ExposedByNpc { get; set; }
        public string FirstExposedByNpc { get; set; }
        public string TargetCharacter { get; set; }
        public string DependGiveItem { get; set; }
        public string ClearRewardItem { get; set; }
        public List<ClearRewardItemEntry> ClearRewardItems { get; set; } =
            new List<ClearRewardItemEntry>();

        #endregion

        #region 标记

        public bool CantGiveup { get; set; }
        public int JobChangeQuestValue { get; set; }
        public bool IsAccountQuest { get; set; }
        public bool IgnoreQuestLevel4Exp { get; set; }
        public bool IsEvent { get; set; }
        public int CreatureKind { get; set; } = -1;
        public int CreatureLevel { get; set; } = -1;
        public int ExpertJobType { get; set; } = -1;
        public int ExpertJobLevel { get; set; } = -1;

        #endregion
        #region 解析

        public static QuestFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new QuestFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var qst = new QuestFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    case "name": qst.Name = StripBacktick(data); break;
                    case "grade": qst.Grade = StripBacktick(data); break;
                    case "difficulty": qst.Difficulty = StripBacktick(data); break;
                    case "type": qst.Type = StripBacktick(data); break;
                    case "sub type": qst.SubType = ParseInt(data); break;
                    case "level": qst.Level = ParseIntPair(data); break;
                    case "job": qst.Job = StripBacktick(data); break;
                    case "grow type": qst.GrowType = ParseInt(data); break;
                    case "grow number": qst.GrowNumber = ParseInt(data); break;

                    // NPC
                    case "npc index": qst.NpcIndex = ParseInt(data); break;
                    case "complete npc index": qst.CompleteNpcIndex = ParseInt(data); break;

                    case "depend message": qst.DependMessage = StripBacktick(data); break;
                    case "condition message": qst.ConditionMessage = StripBacktick(data); break;
                    case "solve message": qst.SolveMessage = StripBacktick(data); break;

                    case "reward type": qst.RewardType = StripBacktick(data); break;
                    case "reward int data": qst.RewardIntData = data; break;
                    case "reward selection int data": qst.RewardSelectionIntData = data; break;
                    case "gold multiple": qst.GoldMultiple = ParseInt(data); break;

                    case "int data": qst.IntData = data; break;
                    case "pre required quest":
                        if (qst.PreRequiredQuestGroups == null)
                            qst.PreRequiredQuestGroups = new System.Collections.Generic.List<string>();
                        qst.PreRequiredQuestGroups.Add(data);
                        qst.PreRequiredQuest = string.IsNullOrEmpty(qst.PreRequiredQuest)
                            ? data : qst.PreRequiredQuest + " " + data;
                        break;
                    case "pre required quest answer":
                        if (qst.PreRequiredQuestAnswerGroups == null)
                            qst.PreRequiredQuestAnswerGroups = new System.Collections.Generic.List<string>();
                        qst.PreRequiredQuestAnswerGroups.Add(data);
                        qst.PreRequiredQuestAnswer = string.IsNullOrEmpty(qst.PreRequiredQuestAnswer)
                            ? data : qst.PreRequiredQuestAnswer + " " + data;
                        break;

                    case "dialog": qst.Dialog = data; break;
                    case "monster reward item":
                        qst.MonsterRewardItem = data;
                        qst.MonsterRewardItems = MonsterRewardItemEntry.ParseList(data);
                        break;
                    case "enemy reward item":
                        qst.EnemyRewardItem = data;
                        qst.EnemyRewardItems = EnemyRewardItemEntry.ParseList(data);
                        break;
                    case "dungeon info": qst.DungeonInfo = data; break;
                    case "substitutive names": qst.SubstitutiveNames = data; break;
                    case "relation quest": qst.RelationQuest = data; break;
                    case "exception quest": qst.ExceptionQuest = data; break;
                    case "collision quest":
                        if (qst.CollisionQuestGroups == null)
                            qst.CollisionQuestGroups = new System.Collections.Generic.List<string>();
                        qst.CollisionQuestGroups.Add(data);
                        qst.CollisionQuest = string.IsNullOrEmpty(qst.CollisionQuest)
                            ? data : qst.CollisionQuest + " " + data;
                        break;
                    case "account collision quest":
                        if (qst.AccountCollisionQuestGroups == null)
                            qst.AccountCollisionQuestGroups = new System.Collections.Generic.List<string>();
                        qst.AccountCollisionQuestGroups.Add(data);
                        qst.AccountCollisionQuest = string.IsNullOrEmpty(qst.AccountCollisionQuest)
                            ? data : qst.AccountCollisionQuest + " " + data;
                        break;
                    case "exposed by npc": qst.ExposedByNpc = data; break;
                    case "first exposed by npc": qst.FirstExposedByNpc = data; break;
                    case "target character": qst.TargetCharacter = data; break;
                    case "depend give item": qst.DependGiveItem = data; break;
                    case "clear reward item":
                        qst.ClearRewardItem = data;
                        qst.ClearRewardItems =
                            ClearRewardItemEntry.ParseList(data);
                        break;

                    case "cant giveup": qst.CantGiveup = true; break;
                    case "job change quest": qst.JobChangeQuestValue = ParseInt(data); break;
                    case "account quest": qst.IsAccountQuest = true; break;
                    case "ignore quest level 4 exp": qst.IgnoreQuestLevel4Exp = true; break;
                    case "event": if (ParseInt(data) != 0) qst.IsEvent = true; break;
                    case "creature kind": qst.CreatureKind = ParseInt(data); break;
                    case "creature level": qst.CreatureLevel = ParseInt(data); break;
                    case "expertjob level":
                        var ejParts = data.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                        if (ejParts.Length >= 1) { int ejt; if (int.TryParse(ejParts[0], out ejt)) qst.ExpertJobType = ejt; }
                        if (ejParts.Length >= 2) { int ejl; if (int.TryParse(ejParts[1], out ejl)) qst.ExpertJobLevel = ejl; }
                        break;
                }
            }

            return qst;
        }

        #endregion
    }

    public class MonsterRewardItemEntry
    {
        public int MonsterCode { get; set; }
        public int DungeonId { get; set; }
        public int Difficulty { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int DropRate { get; set; }
        public int MaxStack { get; set; }

        public static List<MonsterRewardItemEntry> ParseList(string data)
        {
            var result = new List<MonsterRewardItemEntry>();
            if (string.IsNullOrWhiteSpace(data)) return result;
            var tokens = data.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 6 < tokens.Length; i += 7)
            {
                if (int.TryParse(tokens[i], out var mc) &&
                    int.TryParse(tokens[i + 1], out var dg) &&
                    int.TryParse(tokens[i + 2], out var diff) &&
                    int.TryParse(tokens[i + 3], out var item) &&
                    int.TryParse(tokens[i + 4], out var cnt) &&
                    int.TryParse(tokens[i + 5], out var rate) &&
                    int.TryParse(tokens[i + 6], out var max))
                {
                    result.Add(new MonsterRewardItemEntry
                    {
                        MonsterCode = mc, DungeonId = dg, Difficulty = diff,
                        ItemId = item, Count = cnt, DropRate = rate, MaxStack = max
                    });
                }
            }
            return result;
        }
    }

    public class EnemyRewardItemEntry
    {
        public int EnemyCode { get; set; }
        public int EnemyType { get; set; }
        public int DungeonId { get; set; }
        public int Difficulty { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int DropRate { get; set; }
        public int MaxStack { get; set; }

        public static List<EnemyRewardItemEntry> ParseList(string data)
        {
            var result = new List<EnemyRewardItemEntry>();
            if (string.IsNullOrWhiteSpace(data)) return result;
            var tokens = data.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 7 < tokens.Length; i += 8)
            {
                if (int.TryParse(tokens[i], out var enemyCode) &&
                    int.TryParse(tokens[i + 1], out var enemyType) &&
                    int.TryParse(tokens[i + 2], out var dungeonId) &&
                    int.TryParse(tokens[i + 3], out var difficulty) &&
                    int.TryParse(tokens[i + 4], out var itemId) &&
                    int.TryParse(tokens[i + 5], out var count) &&
                    int.TryParse(tokens[i + 6], out var dropRate) &&
                    int.TryParse(tokens[i + 7], out var maxStack))
                {
                    result.Add(new EnemyRewardItemEntry
                    {
                        EnemyCode = enemyCode,
                        EnemyType = enemyType,
                        DungeonId = dungeonId,
                        Difficulty = difficulty,
                        ItemId = itemId,
                        Count = count,
                        DropRate = dropRate,
                        MaxStack = maxStack
                    });
                }
            }
            return result;
        }
    }

    public class ClearRewardItemEntry
    {
        public int DungeonId { get; set; }
        public int Difficulty { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int DropRate { get; set; }
        public int MaxStack { get; set; }

        public static List<ClearRewardItemEntry> ParseList(string data)
        {
            var result = new List<ClearRewardItemEntry>();
            if (string.IsNullOrWhiteSpace(data))
                return result;

            var tokens = data.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i + 5 < tokens.Length; i += 6)
            {
                if (!int.TryParse(tokens[i], out var dungeonId)
                    || !int.TryParse(tokens[i + 1], out var difficulty)
                    || !int.TryParse(tokens[i + 2], out var itemId)
                    || !int.TryParse(tokens[i + 3], out var count)
                    || !int.TryParse(tokens[i + 4], out var dropRate)
                    || !int.TryParse(tokens[i + 5], out var maxStack))
                {
                    continue;
                }

                result.Add(new ClearRewardItemEntry
                {
                    DungeonId = dungeonId,
                    Difficulty = difficulty,
                    ItemId = itemId,
                    Count = count,
                    DropRate = dropRate,
                    MaxStack = maxStack,
                });
            }

            return result;
        }
    }
}
