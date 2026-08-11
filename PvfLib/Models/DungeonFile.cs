using System;
using System.Collections.Generic;
using System.Globalization;

namespace GmPvfLib
{
    /// <summary>
    /// 迷宫变体信息（对应 [maze info] 分隔的块）
    /// </summary>
    public class RidableObject
    {
        public int MapX { get; set; }
        public int MapY { get; set; }
        public int ObjectIndex { get; set; }
        public int PosX { get; set; }
        public int PosY { get; set; }
        /// <summary>阵营: 100=[monster]敌方, 200=[neutral]中立, 0=[character]友方</summary>
        public int Faction { get; set; }
    }

    public class RidableObjectScript
    {
        public int SelectCount { get; set; }
        public bool Regenerate { get; set; }
        public int MinimapIcon { get; set; } = -1;
        public List<RidableObject> Objects { get; set; } = new List<RidableObject>();
    }

    public class ClearConditionEntry
    {
        public int Type { get; set; }
        public int TargetId { get; set; }
        public int Count { get; set; }
        public int GroupId { get; set; }
        public int GroupRequired { get; set; }
    }

    public class DungeonRequiredItem
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
        public bool ConsumeOnEntry { get; set; }
    }

    public class DeathTowerStage
    {
        public int Stage { get; set; }
        public int MapId { get; set; }
    }

    public class TournamentRewardExperience
    {
        public int CompletedRounds { get; set; }
        public long Experience { get; set; }
    }

    public class TournamentResultCard
    {
        public int ResultKey { get; set; }
        public int GoldWeight { get; set; }
        public int ItemWeight { get; set; }
        public int EmptyWeight { get; set; }
    }

    public class TournamentRewardItemRate
    {
        public int ItemId { get; set; }
        public int Weight { get; set; }
        public int Count { get; set; }
    }

    public class WarpMapConditionEntry
    {
        public int SourceX { get; set; }
        public int SourceY { get; set; }
        public int DestinationX { get; set; }
        public int DestinationY { get; set; }
    }

    public class NamedMonsterMapPosition
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class MazeMinimapIconInfo
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string Image { get; set; }
        public int IconIndex { get; set; }
        public int Flag { get; set; }
    }

    public class MazeInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public string Greed { get; set; }                   // 网格模式字符串
        public string MapSpecification { get; set; }        // 原始格式
        public List<MapSpecificationItem> MapSpecifications { get; set; } = new List<MapSpecificationItem>();
        public int[] StartMap { get; set; }                 // [x, y, ?, ?]
        public int[] BossMap { get; set; }                  // 多个参数
        public int[] HitCount { get; set; }
        public int SealDoorAppearRate { get; set; } = -1;
        public int SealDoorMapIndex { get; set; } = -1;
        public int[] SealDoorPos { get; set; }
        public int[] QuestConnection { get; set; }          // [flag, questId, value]
        public RidableObjectScript RidableScript { get; set; }
        public List<RidableObjectScript> RidableScripts { get; set; } =
            new List<RidableObjectScript>();
        public List<ClearConditionEntry> ClearConditions { get; set; } = new List<ClearConditionEntry>();
        public List<MazeMinimapIconInfo> MinimapIcons { get; set; } = new List<MazeMinimapIconInfo>();
        public int EventMonsterRandomMap { get; set; } = -1;

        public string BossMapSpecification { get; set; }
        public string LayeredMapSpecification { get; set; }

        /// <summary>该 maze 块的所有 ScriptNode 子节点（可用于访问未类型化的字段）</summary>
        public List<ScriptNode> Nodes { get; set; } = new List<ScriptNode>();
    }

    /// <summary>
    /// PVF 中的 .dgn 地下城文件。
    /// 核心字段类型化，不常用字段通过 Root/Content 属性或 GetValue 方法动态访问。
    /// </summary>
    public class DungeonFile : PvfModelBase
    {
        #region 常用字段

        public string Name { get; set; }
        public string Explain { get; set; }
        public string CutsceneImage { get; set; }
        public int CutsceneImageParam { get; set; } = -1;
        public string MinimapImage { get; set; }
        public string EnteringTitle { get; set; }
        public int MinimumRequiredLevel { get; set; } = -1;
        public int BasisLevel { get; set; } = -1;
        public float ExperienceIncreasingPoint { get; set; } = -1;
        public int BackgroundPos { get; set; } = -1;
        public string DungeonType { get; set; }
        public int[] Champion { get; set; }
        public int[] PathgateObject { get; set; }
        public string WorldmapPatternInfo { get; set; }
        public string WorldmapInfo { get; set; }
        public int Difficulty { get; set; } = -1;
        public int[] DifficultyLevel { get; set; }
        public int[] DesignateDungeonDifficulty { get; set; }
        public bool NoFatigue { get; set; }
        public int[] NamedMonster { get; set; }
        public int[] RecommendedLevel { get; set; }         // [min, max]
        public int LimitPartyCount { get; set; } = -1;
        public int[] QuestConnection { get; set; }          // DGN 顶层 [flag, questId, value]

        // 进本/经济
        public int HellDungeon { get; set; } = -1;
        public int EscapeHell { get; set; } = -1;
        public int CoinLimit { get; set; } = -1;
        public int HellCoinLimit { get; set; } = -1;
        public int CharacterCoinLimit { get; set; } = -1;
        public int PartyMemberCoinLimit { get; set; } = -1;
        public int LimitInoutCount { get; set; } = -1;
        public int LimitEscapeCharacter { get; set; } = -1;
        public int GoldCardUse { get; set; } = -1;
        public int JoinCostGold { get; set; } = -1;
        public int GoldDropProb { get; set; } = -1;
        public int Fatigue { get; set; } = -1;
        public int FatigueResult { get; set; } = -1;
        public int ProhibitPractice { get; set; } = -1;
        public int SharedDifficultDungeonIndex { get; set; } = -1;
        public int ImpossibleDungeonClassification { get; set; } = -1;
        public int AiCharacterAppearRate { get; set; } = -1;
        public int DummyAppearCount { get; set; } = -1;
        public int PartyNumCheck { get; set; } = -1;
        public int QuestNpcDungeon { get; set; } = -1;
        public int HerosmodeEnable { get; set; } = -1;
        public int HerosmodeRequiredQuest { get; set; } = -1;
        public int EventDungeonDifficulty { get; set; } = -1;
        public int EventDungeonCof { get; set; } = -1;
        public int AdjustMobExpByLevel { get; set; } = -1;
        public int BloodDungeonType { get; set; } = -1;
        public int BloodMaxRound { get; set; } = -1;
        public int MobLevelCharacLevelReplaceFlag { get; set; } = -1;

        // 塔
        public int TowerOfDespair { get; set; } = -1;
        public int TowerFpCubepiece { get; set; } = -1;
        public int TowerLimitOfStackableItem { get; set; } = -1;
        public int TowerMaxClearItemNum { get; set; } = -1;
        public int TowerItemDrop { get; set; } = -1;
        public int TowerRandomMapIndexes { get; set; } = -1;

        // 战场/竞技
        public int WarroomMapIndex { get; set; } = -1;
        public int MaxMonster { get; set; } = -1;
        public int SpawnStepMax { get; set; } = -1;
        public int BattleSpawnTime { get; set; } = -1;
        public int PlayerKc { get; set; } = -1;
        public int TournamentRoundFatigue { get; set; } = -1;
        public float TournamentClearRewardGoldRate { get; set; } = -1f;
        public int MonsterRandomAppearOnly { get; set; } = -1;
        public int RemainMonsterCountVisible { get; set; } = -1;

        // bool 标记
        public bool DefenseDungeon { get; set; }
        public bool BloodDungeon { get; set; }
        public bool DimensionDungeon { get; set; }
        public bool TournamentDungeon { get; set; }
        public bool PowerwarDungeon { get; set; }
        public bool RiskDungeon { get; set; }
        public bool AncientDungeon { get; set; }
        public bool EventDungeon { get; set; }
        public bool CrackOfDimensionDungeon { get; set; }
        public bool DisableExit { get; set; }
        public bool IndividualMapMovement { get; set; }
        public bool OpenDoorEvenEnemy { get; set; }
        public bool MoveMapEvenEnemy { get; set; }
        public bool EnterWithoutFatigue { get; set; }
        public bool UseFatigueOnlyStartDungeon { get; set; }
        public bool SpecialDungeon { get; set; }
        public bool KronosDungeon { get; set; }
        public bool SaoDungeon { get; set; }
        public bool NoCheckEnterBossRoom { get; set; }
        public bool NoGiveupPanalty { get; set; }
        public bool BossMarkDisable { get; set; }
        public bool EnableApcStackable { get; set; }
        public bool MovableEvenBossDie { get; set; }
        public bool IgnoreClearEffectAndSound { get; set; }
        public bool DontKillMobWhenBossDie { get; set; }
        public bool RemoveDungeonScoreAndRank { get; set; }
        public bool NoRevivalTimerLimit { get; set; }
        public bool MultiStartPoint { get; set; }
        public bool IgnoreDefaultDungeonClear { get; set; }

        // string
        public string RevisionTable { get; set; }
        public string MonsterapcDiffTable { get; set; }
        public string EnteringTitleNext { get; set; }
        public string MonsterLevelRivision { get; set; }
        public string DungeonTypeForExtraDrop { get; set; }
        public string MinimapIcon { get; set; }

        // float
        public float KillCountConst { get; set; } = -1f;
        public float MoveSpeed { get; set; } = -1f;

        // 复杂/原始字符串
        public string RequiredItem { get; set; }
        public string EventRequiredItem { get; set; }
        public string AddedRequiredItem { get; set; }
        public List<DungeonRequiredItem> RequiredItems { get; set; } =
            new List<DungeonRequiredItem>();
        public List<DungeonRequiredItem> EventRequiredItems { get; set; } =
            new List<DungeonRequiredItem>();
        public List<DungeonRequiredItem> AddedRequiredItems { get; set; } =
            new List<DungeonRequiredItem>();
        public string CoinInfo { get; set; }
        public string Schedule { get; set; }
        public string EventMonster { get; set; }
        public string EventMonster2 { get; set; }
        public string EventMonster3 { get; set; }
        public string EventMonsterRandomMap { get; set; }
        public string MonsterDifficultyBonus { get; set; }
        public string TowerDialog { get; set; }
        public string TowerStage { get; set; }
        public string TowerRecovery { get; set; }
        public string TowerHighSkillInitialCoolTime { get; set; }
        public string TowerHighSkillInitialCoolTimeRate { get; set; }
        public string DeathTowerMapIndexes { get; set; }
        public List<DeathTowerStage> DeathTowerStages { get; set; } =
            new List<DeathTowerStage>();
        public bool DeathTowerMapIndexesMalformed { get; set; }
        public string ResultCard { get; set; }
        public string RewardItemRate { get; set; }
        public List<TournamentResultCard> TournamentResultCards { get; set; } =
            new List<TournamentResultCard>();
        public List<TournamentRewardItemRate> TournamentRewardItemRates { get; set; } =
            new List<TournamentRewardItemRate>();
        public bool TournamentResultCardMalformed { get; set; }
        public bool TournamentRewardItemRateMalformed { get; set; }
        public string TournamentClearRewardExp { get; set; }
        public List<TournamentRewardExperience> TournamentClearRewardExperiences { get; set; } =
            new List<TournamentRewardExperience>();
        public bool TournamentRewardExperienceMalformed { get; set; }
        public string ClearMap { get; set; }
        public string ClearRewardItem { get; set; }
        public string BossRoomEntranceCondition { get; set; }
        public string NamedMonsterMapPos { get; set; }
        public List<NamedMonsterMapPosition> NamedMonsterMapPositions { get; set; } =
            new List<NamedMonsterMapPosition>();
        public string WarpMapCondition { get; set; }
        public List<WarpMapConditionEntry> WarpMapConditions { get; set; } =
            new List<WarpMapConditionEntry>();
        public string DungeonMinimapIconSetting { get; set; }
        public string RealdungeonCheckup { get; set; }
        public string CommonPassiveObject { get; set; }
        public string OnClearAddPassiveObject { get; set; }
        public string AppendageDestoryObject { get; set; }
        public string LinkedDungeon { get; set; }
        public string ClearAction { get; set; }
        public string PointByType { get; set; }
        public string DungeonExpBonusMonster { get; set; }
        public string ClearPartyBuffCard { get; set; }
        public string AdvanceAltarType { get; set; }
        public string AdvanceAltarMap { get; set; }
        public string AdvanceAltarClearReward { get; set; }
        public string AdvanceAltarSurvivalMap { get; set; }
        public string AdvanceAltarSurvivalClearReward { get; set; }
        public string RankType { get; set; }
        public string FeverTime { get; set; }
        public string LostLandParameters { get; set; }
        public string LimitTime { get; set; }
        public string NateramTimeAttackInfo { get; set; }
        public string VillageAttackRevengeDungeon { get; set; }
        public string TimeSpiral { get; set; }
        public string SpiciesDungeon { get; set; }
        public string SummonMonster { get; set; }
        public string MonsterTypeSpawnProb { get; set; }
        public string MonsterTypeSpawnCost { get; set; }
        public string MonsterTypeSpawnIntervalRate { get; set; }
        public string MonsterSpawnBaseInterval { get; set; }
        public string MonsterSpawnRandomInterval { get; set; }
        public string SpawnCommonMonsterIndex { get; set; }
        public string SpawnCommonChampionIndex { get; set; }
        public string SpawnSuperChampionIndex { get; set; }
        public string SpawnBossIndex { get; set; }
        public string SpawnStepResourcePool { get; set; }
        public string CommonMonsterItemDropProb { get; set; }
        public string CommonChampionItemDropProb { get; set; }
        public string SuperChampionItemDropProb { get; set; }
        public string BossItemDropProb { get; set; }
        public string CommonMonsterExpConst { get; set; }
        public string CommonChampionExpConst { get; set; }
        public string SuperChampionExpConst { get; set; }
        public string BossExpConst { get; set; }
        public string CommonMonsterItemDropList { get; set; }
        public string CommonChampionItemDropList { get; set; }
        public string SuperChampionItemDropList { get; set; }
        public string BossItemDropList { get; set; }
        public string MonsterExpBonusPerUserDecrease { get; set; }
        public string ResultExpBonusPerUserDecrease { get; set; }
        public string Evil { get; set; }
        public string EvilHighLevel { get; set; }
        public string EvilRate { get; set; }
        public string Easy { get; set; }
        public string Medium { get; set; }
        public string Hard { get; set; }
        public string Round { get; set; }
        public string List { get; set; }
        public string DgnType { get; set; }
        public string ObjectType { get; set; }
        public string HideGrid { get; set; }
        public string OnGuideMovieDungeon { get; set; }
        public string RecommendParty { get; set; }
        public string RecommendEquipment { get; set; }
        public string MinimapBossIcon { get; set; }
        public string SwayEffect { get; set; }
        public string FreeDifficulty { get; set; }
        public string EntryDungeon { get; set; }
        public string AdjustHpGauge { get; set; }
        public string NecessaryParty { get; set; }
        public string UseFatigueOnlyStartDungeonData { get; set; }

        #endregion

        public IReadOnlyList<SpecialPassiveObjectItemGroup>
            SpecialPassiveObjectItemGroups { get; private set; } =
                Array.Empty<SpecialPassiveObjectItemGroup>();
        public bool SpecialPassiveObjectItemDefinitionPresent { get; private set; }
        public bool SpecialPassiveObjectItemDefinitionMalformed { get; private set; }

        /// <summary>迷宫变体列表（以 [maze info] 为分隔）</summary>
        public List<MazeInfo> Mazes { get; set; } = new List<MazeInfo>();
        #region 解析

        // 已知仅属于迷宫变体的标签（出现在 [maze info] 之后）
        private static readonly HashSet<string> MazeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "size", "greed", "map specification", "start map", "boss map",
            "hit count", "seal door appear rate", "seal door map index", "seal door pos", "quest connection",
            "randomized object creation", "clear condition",
            "boss map specification", "layered map specification",
            "minimap icon"
        };

        public static DungeonFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content)) return new DungeonFile { Content = content ?? "" };

            var root = new ScriptParser().Parse(content);
            var dgn = new DungeonFile { Root = root, Content = content };

            // 遍历所有根节点，按已知迷宫标签区分元数据和迷宫数据
            var metaNodes = new List<ScriptNode>();
            List<ScriptNode> currentMaze = null;
            var previousNodeWasMazeMinimapIcon = false;

            foreach (var child in root.Children)
            {
                if (child.Tag.Equals("maze info", StringComparison.OrdinalIgnoreCase))
                {
                    // 保存前一个迷宫块
                    if (currentMaze != null && currentMaze.Count > 0)
                        dgn.Mazes.Add(BuildMazeInfo(currentMaze, content));
                    currentMaze = new List<ScriptNode>();
                    previousNodeWasMazeMinimapIcon = false;
                }
                else if (currentMaze != null
                    && previousNodeWasMazeMinimapIcon
                    && child.Tag.Equals("event monster random map", StringComparison.OrdinalIgnoreCase))
                {
                    // Some DGN files use this as global event-monster metadata after the
                    // last maze. Only the direct minimap-icon sequence is maze-scoped.
                    currentMaze.Add(child);
                    previousNodeWasMazeMinimapIcon = false;
                }
                else if (currentMaze != null && MazeTags.Contains(child.Tag))
                {
                    currentMaze.Add(child);
                    previousNodeWasMazeMinimapIcon = child.Tag.Equals(
                        "minimap icon",
                        StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    metaNodes.Add(child);
                    previousNodeWasMazeMinimapIcon = false;
                }
            }

            // 保存最后一个迷宫块
            if (currentMaze != null && currentMaze.Count > 0)
                dgn.Mazes.Add(BuildMazeInfo(currentMaze, content));

            ExtractMetadata(dgn, metaNodes, content);
            return dgn;
        }

        private static void ExtractMetadata(DungeonFile dgn, List<ScriptNode> nodes, string text)
        {
            foreach (var node in nodes)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(text).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    case "name":
                        dgn.Name = StripBacktick(data);
                        break;
                    case "explain":
                        dgn.Explain = StripBacktick(data);
                        break;
                    case "cutscene image":
                        ParseCutsceneImage(data, dgn);
                        break;
                    case "minimap image":
                        dgn.MinimapImage = StripBacktick(data);
                        break;
                    case "entering title":
                        dgn.EnteringTitle = StripBacktick(data);
                        break;
                    case "minimum required level":
                        dgn.MinimumRequiredLevel = ParseInt(data);
                        break;
                    case "basis level":
                        dgn.BasisLevel = ParseInt(data);
                        break;
                    case "experience increasing point":
                        float f;
                        if (float.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                            dgn.ExperienceIncreasingPoint = f;
                        break;
                    case "background pos":
                        dgn.BackgroundPos = ParseInt(data);
                        break;
                    case "dungeon type":
                        dgn.DungeonType = StripBacktick(data);
                        break;
                    case "champion":
                        dgn.Champion = ParseIntArray(data);
                        break;
                    case "pathgate object":
                        dgn.PathgateObject = ParseIntArray(data);
                        break;
                    case "worldmap pattern info":
                        dgn.WorldmapPatternInfo = data;
                        break;
                    case "worldmap info":
                        dgn.WorldmapInfo = data;
                        break;
                    case "difficulty":
                        dgn.Difficulty = ParseInt(data);
                        break;
                    case "difficulty level":
                        dgn.DifficultyLevel = ParseIntArray(data);
                        break;
                    case "designate dungeon difficulty":
                        dgn.DesignateDungeonDifficulty = ParseIntArray(data);
                        break;
                    case "no fatigue":
                        dgn.NoFatigue = true;
                        break;
                    case "named monster":
                        dgn.NamedMonster = ParseIntArray(data);
                        break;
                    case "recommended level":
                        dgn.RecommendedLevel = ParseIntArray(data);
                        break;
                    case "limit party count":
                        dgn.LimitPartyCount = ParseInt(data);
                        break;
                    case "quest connection":
                        dgn.QuestConnection = ParseIntArray(data);
                        break;
                    case "special passive object item":
                        ParseSpecialPassiveObjectItem(data, dgn);
                        break;

                    // --- int ---
                    case "hell dungeon": dgn.HellDungeon = ParseInt(data); break;
                    case "escape hell": dgn.EscapeHell = ParseInt(data); break;
                    case "coin limit": dgn.CoinLimit = ParseInt(data); break;
                    case "hell coin limit": dgn.HellCoinLimit = ParseInt(data); break;
                    case "character coin limit": dgn.CharacterCoinLimit = ParseInt(data); break;
                    case "party member coin limit": dgn.PartyMemberCoinLimit = ParseInt(data); break;
                    case "limit inout count": dgn.LimitInoutCount = ParseInt(data); break;
                    case "limit escape character": dgn.LimitEscapeCharacter = ParseInt(data); break;
                    case "gold card use": dgn.GoldCardUse = ParseInt(data); break;
                    case "join cost gold": dgn.JoinCostGold = ParseInt(data); break;
                    case "gold drop prob": dgn.GoldDropProb = ParseInt(data); break;
                    case "fatigue": dgn.Fatigue = ParseInt(data); break;
                    case "fatigue result": dgn.FatigueResult = ParseInt(data); break;
                    case "prohibit practice": dgn.ProhibitPractice = ParseInt(data); break;
                    case "shared difficult dungeon index": dgn.SharedDifficultDungeonIndex = ParseInt(data); break;
                    case "impossible dungeon classification": dgn.ImpossibleDungeonClassification = ParseInt(data); break;
                    case "ai character appear rate": dgn.AiCharacterAppearRate = ParseInt(data); break;
                    case "dummy appear count": dgn.DummyAppearCount = ParseInt(data); break;
                    case "party num check": dgn.PartyNumCheck = ParseInt(data); break;
                    case "quest npc dungeon": dgn.QuestNpcDungeon = ParseInt(data); break;
                    case "herosmode enable": dgn.HerosmodeEnable = ParseInt(data); break;
                    case "herosmode required quest": dgn.HerosmodeRequiredQuest = ParseInt(data); break;
                    case "event dungeon difficulty": dgn.EventDungeonDifficulty = ParseInt(data); break;
                    case "event dungeon cof": dgn.EventDungeonCof = ParseInt(data); break;
                    case "adjust mob exp by level": dgn.AdjustMobExpByLevel = ParseInt(data); break;
                    case "blood max round": dgn.BloodMaxRound = ParseInt(data); break;
                    case "mob level charac level replace flag": dgn.MobLevelCharacLevelReplaceFlag = ParseInt(data); break;
                    case "tower of despair":
                        dgn.TowerOfDespair = string.IsNullOrWhiteSpace(data) ? 1 : ParseInt(data);
                        break;
                    case "tower fp cubepiece": dgn.TowerFpCubepiece = ParseInt(data); break;
                    case "tower limit of stackable item": dgn.TowerLimitOfStackableItem = ParseInt(data); break;
                    case "tower max clear item num": dgn.TowerMaxClearItemNum = ParseInt(data); break;
                    case "tower item drop": dgn.TowerItemDrop = ParseInt(data); break;
                    case "warroom map index": dgn.WarroomMapIndex = ParseInt(data); break;
                    case "max monster": dgn.MaxMonster = ParseInt(data); break;
                    case "spawn step max": dgn.SpawnStepMax = ParseInt(data); break;
                    case "battle spawn time": dgn.BattleSpawnTime = ParseInt(data); break;
                    case "player kc": dgn.PlayerKc = ParseInt(data); break;
                    case "tournament round fatigue": dgn.TournamentRoundFatigue = ParseInt(data); break;
                    case "tournament clear reward gold rate":
                        dgn.TournamentClearRewardGoldRate = ParseFloat(data);
                        break;
                    case "monster random appear only": dgn.MonsterRandomAppearOnly = ParseInt(data); break;
                    case "remain monster count visible": dgn.RemainMonsterCountVisible = ParseInt(data); break;

                    // --- bool ---
                    case "defense dungeon": dgn.DefenseDungeon = true; break;
                    case "blood dungeon":
                        dgn.BloodDungeon = true;
                        dgn.BloodDungeonType = string.IsNullOrWhiteSpace(data)
                            ? 1
                            : ParseInt(data);
                        break;
                    case "dimension dungeon": dgn.DimensionDungeon = true; break;
                    case "tournament dungeon": dgn.TournamentDungeon = true; break;
                    case "powerwar dungeon": dgn.PowerwarDungeon = true; break;
                    case "risk dungeon": dgn.RiskDungeon = true; break;
                    case "ancient dungeon": dgn.AncientDungeon = true; break;
                    case "event dungeon": dgn.EventDungeon = true; break;
                    case "crack of dimension dungeon": dgn.CrackOfDimensionDungeon = true; break;
                    case "disable exit": dgn.DisableExit = true; break;
                    case "individual map movement": dgn.IndividualMapMovement = true; break;
                    case "open door even enemy": dgn.OpenDoorEvenEnemy = true; break;
                    case "move map even enemy": dgn.MoveMapEvenEnemy = true; break;
                    case "enter without fatigue": dgn.EnterWithoutFatigue = true; break;
                    case "special dungeon": dgn.SpecialDungeon = true; break;
                    case "kronos dungeon": dgn.KronosDungeon = true; break;
                    case "sao dungeon": dgn.SaoDungeon = true; break;
                    case "no check enter boss room": dgn.NoCheckEnterBossRoom = true; break;
                    case "no giveup panalty": dgn.NoGiveupPanalty = true; break;
                    case "boss mark disable": dgn.BossMarkDisable = true; break;
                    case "enable apc stackable": dgn.EnableApcStackable = true; break;
                    case "movable even boss die": dgn.MovableEvenBossDie = true; break;
                    case "ignore clear effect and sound": dgn.IgnoreClearEffectAndSound = true; break;
                    case "dont kill mob when boss die": dgn.DontKillMobWhenBossDie = true; break;
                    case "remove dungeon score and rank": dgn.RemoveDungeonScoreAndRank = true; break;
                    case "no revival timer limit": dgn.NoRevivalTimerLimit = true; break;
                    case "multi start point": dgn.MultiStartPoint = true; break;
                    case "ignore default dungeon clear": dgn.IgnoreDefaultDungeonClear = true; break;

                    // --- string ---
                    case "revision table": dgn.RevisionTable = StripBacktick(data); break;
                    case "monsterapc diff table": dgn.MonsterapcDiffTable = StripBacktick(data); break;
                    case "entering title next": dgn.EnteringTitleNext = StripBacktick(data); break;
                    case "monster level rivision": dgn.MonsterLevelRivision = StripBacktick(data); break;
                    case "dungeon type for extra drop": dgn.DungeonTypeForExtraDrop = StripBacktick(data); break;

                    // --- float ---
                    case "kill count const":
                        if (float.TryParse(data?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var kcf))
                            dgn.KillCountConst = kcf;
                        break;
                    case "move speed":
                        if (float.TryParse(data?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var msf))
                            dgn.MoveSpeed = msf;
                        break;

                    // --- complex raw string ---
                    case "required item":
                        dgn.RequiredItem = AppendRequiredItems(
                            dgn.RequiredItem,
                            dgn.RequiredItems,
                            data);
                        break;
                    case "event required item":
                        dgn.EventRequiredItem = AppendRequiredItems(
                            dgn.EventRequiredItem,
                            dgn.EventRequiredItems,
                            data);
                        break;
                    case "added required item":
                        dgn.AddedRequiredItem = AppendRequiredItems(
                            dgn.AddedRequiredItem,
                            dgn.AddedRequiredItems,
                            data);
                        break;
                    case "coin info": dgn.CoinInfo = data; break;
                    case "schedule": dgn.Schedule = data; break;
                    case "event monster": dgn.EventMonster = data; break;
                    case "event monster2": dgn.EventMonster2 = data; break;
                    case "event monster3": dgn.EventMonster3 = data; break;
                    case "event monster random map": dgn.EventMonsterRandomMap = data; break;
                    case "monster difficulty bonus": dgn.MonsterDifficultyBonus = data; break;
                    case "tower dialog": dgn.TowerDialog = data; break;
                    case "tower stage": dgn.TowerStage = data; break;
                    case "tower recovery": dgn.TowerRecovery = data; break;
                    case "tower high skill initial cool time": dgn.TowerHighSkillInitialCoolTime = data; break;
                    case "tower high skill initial cool time rate": dgn.TowerHighSkillInitialCoolTimeRate = data; break;
                    case "death tower map indexes":
                        dgn.DeathTowerMapIndexes = JoinDirectData(node, text);
                        if (TryParseDeathTowerStages(
                                dgn.DeathTowerMapIndexes,
                                out var towerStages))
                        {
                            dgn.DeathTowerStages.AddRange(towerStages);
                        }
                        else
                        {
                            dgn.DeathTowerMapIndexesMalformed = true;
                        }
                        break;
                    case "tower random map indexes": dgn.TowerRandomMapIndexes = ParseInt(data); break;
                    case "result card":
                        dgn.ResultCard = JoinDirectData(node, text);
                        if (TryParseTournamentResultCards(
                                node,
                                text,
                                out var resultCards))
                        {
                            dgn.TournamentResultCards.AddRange(resultCards);
                        }
                        else
                        {
                            dgn.TournamentResultCardMalformed = true;
                        }

                        foreach (var rewardItemRate in node.GetChildren(
                            "reward item rate"))
                        {
                            dgn.RewardItemRate = JoinDirectData(
                                rewardItemRate,
                                text);
                            if (TryParseTournamentRewardItemRates(
                                    rewardItemRate,
                                    text,
                                    out var nestedRates))
                            {
                                dgn.TournamentRewardItemRates.AddRange(
                                    nestedRates);
                            }
                            else
                            {
                                dgn.TournamentRewardItemRateMalformed = true;
                            }
                        }
                        break;
                    case "reward item rate":
                        dgn.RewardItemRate = JoinDirectData(node, text);
                        if (TryParseTournamentRewardItemRates(
                                node,
                                text,
                                out var rewardItemRates))
                        {
                            dgn.TournamentRewardItemRates.AddRange(
                                rewardItemRates);
                        }
                        else
                        {
                            dgn.TournamentRewardItemRateMalformed = true;
                        }
                        break;
                    case "tournament clear reward exp":
                        dgn.TournamentClearRewardExp = data;
                        if (TryParseTournamentRewardExperience(
                                data,
                                out var tournamentRewards))
                        {
                            dgn.TournamentClearRewardExperiences.AddRange(
                                tournamentRewards);
                        }
                        else
                        {
                            dgn.TournamentRewardExperienceMalformed = true;
                        }
                        break;
                    case "clear map": dgn.ClearMap = data; break;
                    case "clear reward item": dgn.ClearRewardItem = data; break;
                    case "boss room entrance condition": dgn.BossRoomEntranceCondition = ReadRawNodeData(node, text, data); break;
                    case "named monster map pos":
                        dgn.NamedMonsterMapPos = data;
                        dgn.NamedMonsterMapPositions = ParseNamedMonsterMapPositions(data);
                        break;
                    case "warp map condition":
                        dgn.WarpMapCondition = node.GetContent(text).Trim();
                        dgn.WarpMapConditions = ParseWarpMapConditions(node, text);
                        break;
                    case "dungeon minimap icon setting": dgn.DungeonMinimapIconSetting = ReadRawNodeData(node, text, data); break;
                    case "realdungeon checkup": dgn.RealdungeonCheckup = data; break;
                    case "common passive object": dgn.CommonPassiveObject = data; break;
                    case "on clear add passive object": dgn.OnClearAddPassiveObject = data; break;
                    case "appendage destory object": dgn.AppendageDestoryObject = data; break;
                    case "linked dungeon": dgn.LinkedDungeon = ReadRawNodeData(node, text, data); break;
                    case "clear action": dgn.ClearAction = data; break;
                    case "point by type": dgn.PointByType = data; break;
                    case "dungeon exp bonus monster": dgn.DungeonExpBonusMonster = data; break;
                    case "clear_party_buff_card": dgn.ClearPartyBuffCard = data; break;
                    case "advance altar type": dgn.AdvanceAltarType = data; break;
                    case "advance altar map": dgn.AdvanceAltarMap = data; break;
                    case "advance altar clear reward": dgn.AdvanceAltarClearReward = data; break;
                    case "advance altar survival map": dgn.AdvanceAltarSurvivalMap = data; break;
                    case "advance altar survival clear reward": dgn.AdvanceAltarSurvivalClearReward = data; break;
                    case "rank type": dgn.RankType = data; break;
                    case "fever time": dgn.FeverTime = data; break;
                    case "lost land parameters": dgn.LostLandParameters = data; break;
                    case "limit time": dgn.LimitTime = data; break;
                    case "nateram time attack info": dgn.NateramTimeAttackInfo = data; break;
                    case "village attack revenge dungeon": dgn.VillageAttackRevengeDungeon = data; break;
                    case "time spiral": dgn.TimeSpiral = data; break;
                    case "spicies dungeon": dgn.SpiciesDungeon = data; break;
                    case "summon monster": dgn.SummonMonster = data; break;
                    case "monster type spawn prob": dgn.MonsterTypeSpawnProb = data; break;
                    case "monster type spawn cost": dgn.MonsterTypeSpawnCost = data; break;
                    case "monster type spawn interval rate": dgn.MonsterTypeSpawnIntervalRate = data; break;
                    case "monster spawn base interval": dgn.MonsterSpawnBaseInterval = data; break;
                    case "monster spawn random interval": dgn.MonsterSpawnRandomInterval = data; break;
                    case "spawn common monster index": dgn.SpawnCommonMonsterIndex = data; break;
                    case "spawn common champion index": dgn.SpawnCommonChampionIndex = data; break;
                    case "spawn super champion index": dgn.SpawnSuperChampionIndex = data; break;
                    case "spawn boss index": dgn.SpawnBossIndex = data; break;
                    case "spawn step resource pool": dgn.SpawnStepResourcePool = data; break;
                    case "common monster item drop prob": dgn.CommonMonsterItemDropProb = data; break;
                    case "common champion item drop prob": dgn.CommonChampionItemDropProb = data; break;
                    case "super champion item drop prob": dgn.SuperChampionItemDropProb = data; break;
                    case "boss item drop prob": dgn.BossItemDropProb = data; break;
                    case "common monster exp const": dgn.CommonMonsterExpConst = data; break;
                    case "common champion exp const": dgn.CommonChampionExpConst = data; break;
                    case "super champion exp const": dgn.SuperChampionExpConst = data; break;
                    case "boss exp const": dgn.BossExpConst = data; break;
                    case "common monster item drop list": dgn.CommonMonsterItemDropList = data; break;
                    case "common champion item drop list": dgn.CommonChampionItemDropList = data; break;
                    case "super champion item drop list": dgn.SuperChampionItemDropList = data; break;
                    case "boss item drop list": dgn.BossItemDropList = data; break;
                    case "monster exp bonus per user decrease": dgn.MonsterExpBonusPerUserDecrease = data; break;
                    case "result exp bonus per user decrease": dgn.ResultExpBonusPerUserDecrease = data; break;
                    case "evil": dgn.Evil = data; break;
                    case "evil high level": dgn.EvilHighLevel = data; break;
                    case "evil rate": dgn.EvilRate = data; break;
                    case "easy": dgn.Easy = data; break;
                    case "medium": dgn.Medium = data; break;
                    case "hard": dgn.Hard = data; break;
                    case "round": dgn.Round = data; break;
                    case "list": dgn.List = data; break;
                    case "type": dgn.DgnType = data; break;
                    case "object type": dgn.ObjectType = data; break;
                    case "hide grid": dgn.HideGrid = data; break;
                    case "on guide movie dungeon": dgn.OnGuideMovieDungeon = data; break;
                    case "recommend party": dgn.RecommendParty = data; break;
                    case "recommend equipment": dgn.RecommendEquipment = data; break;
                    case "minimap boss icon": dgn.MinimapBossIcon = data; break;
                    case "sway effect": dgn.SwayEffect = data; break;
                    case "free difficulty": dgn.FreeDifficulty = data; break;
                    case "entry dungeon": dgn.EntryDungeon = data; break;
                    case "adjust hp gauge": dgn.AdjustHpGauge = data; break;
                    case "necessary party": dgn.NecessaryParty = data; break;
                    case "use fatigue only start dungeon":
                        dgn.UseFatigueOnlyStartDungeon = true;
                        dgn.UseFatigueOnlyStartDungeonData = data;
                        break;
                    case "minimap icon": dgn.MinimapIcon = data; break;
                }
            }
        }

        private static void ParseSpecialPassiveObjectItem(string data, DungeonFile dgn)
        {
            if (dgn == null)
                return;

            if (dgn.SpecialPassiveObjectItemDefinitionPresent)
            {
                DisableSpecialPassiveObjectItems(dgn);
                return;
            }

            dgn.SpecialPassiveObjectItemDefinitionPresent = true;
            if (string.IsNullOrWhiteSpace(data))
                return;

            var tokens = ScriptValueTokenizer.Tokenize(data);
            var values = new int[tokens.Count];
            for (var index = 0; index < tokens.Count; index++)
            {
                if (!int.TryParse(
                        tokens[index],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out values[index]))
                {
                    DisableSpecialPassiveObjectItems(dgn);
                    return;
                }
            }

            var groups = new List<SpecialPassiveObjectItemGroup>();
            var position = 0;
            while (position < values.Length)
            {
                if (values.Length - position < 3)
                {
                    DisableSpecialPassiveObjectItems(dgn);
                    return;
                }

                var groupIndex = values[position++];
                var levelOverride = values[position++];
                var itemCount = values[position++];
                if (groupIndex != groups.Count
                    || levelOverride < -1
                    || itemCount < 0
                    || (long)itemCount * 2 > values.Length - position)
                {
                    DisableSpecialPassiveObjectItems(dgn);
                    return;
                }

                var items = new SpecialPassiveObjectItem[itemCount];
                for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
                {
                    var itemId = values[position++];
                    var weight = values[position++];
                    if (itemId <= 0 || weight < 0)
                    {
                        DisableSpecialPassiveObjectItems(dgn);
                        return;
                    }

                    items[itemIndex] = new SpecialPassiveObjectItem(
                        itemId,
                        weight);
                }

                groups.Add(new SpecialPassiveObjectItemGroup(
                    groupIndex,
                    levelOverride,
                    items));
            }

            dgn.SpecialPassiveObjectItemGroups = groups.ToArray();
        }

        private static void DisableSpecialPassiveObjectItems(DungeonFile dgn)
        {
            dgn.SpecialPassiveObjectItemGroups =
                Array.Empty<SpecialPassiveObjectItemGroup>();
            dgn.SpecialPassiveObjectItemDefinitionMalformed = true;
        }

        private static string AppendRequiredItems(
            string rawValue,
            List<DungeonRequiredItem> target,
            string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return rawValue;

            rawValue = string.IsNullOrWhiteSpace(rawValue)
                ? data
                : rawValue + " " + data;

            var values = ParseIntArray(data);
            for (var index = 0; index + 2 < values.Length; index += 3)
            {
                var itemId = values[index];
                var count = values[index + 1];
                var consumeOnEntry = values[index + 2] == 1;
                if (itemId <= 0 || count <= 0)
                    continue;

                var duplicate = target.Exists(item =>
                    item.ItemId == itemId
                    && item.Count == count
                    && item.ConsumeOnEntry == consumeOnEntry);
                if (duplicate)
                    continue;

                target.Add(new DungeonRequiredItem
                {
                    ItemId = itemId,
                    Count = count,
                    ConsumeOnEntry = consumeOnEntry,
                });
            }

            return rawValue;
        }

        private static bool TryParseDeathTowerStages(
            string data,
            out List<DeathTowerStage> stages)
        {
            stages = new List<DeathTowerStage>();
            var values = ParseIntArray(data);
            if (values == null || values.Length < 3)
                return false;

            var stageCount = values[0];
            if (stageCount <= 0 || values.Length != 1 + stageCount * 2)
                return false;

            var seenStages = new HashSet<int>();
            for (var offset = 1; offset < values.Length; offset += 2)
            {
                var stage = values[offset];
                var mapId = values[offset + 1];
                if (stage <= 0 || mapId <= 0 || !seenStages.Add(stage))
                    return false;

                stages.Add(new DeathTowerStage
                {
                    Stage = stage,
                    MapId = mapId,
                });
            }

            stages.Sort((left, right) => left.Stage.CompareTo(right.Stage));
            for (var index = 0; index < stages.Count; index++)
            {
                if (stages[index].Stage != index + 1)
                    return false;
            }

            return true;
        }

        private static float ParseFloat(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return -1f;

            var tokens = data.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length > 0
                && float.TryParse(
                    tokens[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                ? value
                : -1f;
        }

        private static string JoinDirectData(ScriptNode node, string text)
        {
            if (node?.DataItems == null || node.DataItems.Count == 0)
                return string.Empty;

            var values = new List<string>(node.DataItems.Count);
            foreach (var item in node.DataItems)
            {
                var value = item.GetContent(text).Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }
            return string.Join(" ", values);
        }

        private static bool TryParseTournamentResultCards(
            ScriptNode node,
            string text,
            out List<TournamentResultCard> result)
        {
            result = new List<TournamentResultCard>();
            if (!TryParseTournamentIntRows(node, text, 4, out var values))
                return false;

            for (var index = 0; index < values.Count; index += 4)
            {
                result.Add(new TournamentResultCard
                {
                    ResultKey = values[index],
                    GoldWeight = values[index + 1],
                    ItemWeight = values[index + 2],
                    EmptyWeight = values[index + 3],
                });
            }
            return result.Count > 0;
        }

        private static bool TryParseTournamentRewardItemRates(
            ScriptNode node,
            string text,
            out List<TournamentRewardItemRate> result)
        {
            result = new List<TournamentRewardItemRate>();
            if (!TryParseTournamentIntRows(node, text, 3, out var values))
                return false;

            for (var index = 0; index < values.Count; index += 3)
            {
                result.Add(new TournamentRewardItemRate
                {
                    ItemId = values[index],
                    Weight = values[index + 1],
                    Count = values[index + 2],
                });
            }
            return result.Count > 0;
        }

        private static bool TryParseTournamentIntRows(
            ScriptNode node,
            string text,
            int width,
            out List<int> values)
        {
            values = new List<int>();
            if (node?.DataItems == null
                || node.DataItems.Count == 0
                || width <= 0)
            {
                return false;
            }

            foreach (var item in node.DataItems)
            {
                var tokens = item.GetContent(text).Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokens)
                {
                    if (!int.TryParse(
                            token,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var value))
                    {
                        values.Clear();
                        return false;
                    }
                    values.Add(value);
                }
            }

            if (values.Count == 0 || values.Count % width != 0)
            {
                values.Clear();
                return false;
            }
            return true;
        }

        private static bool TryParseTournamentRewardExperience(
            string data,
            out List<TournamentRewardExperience> result)
        {
            result = new List<TournamentRewardExperience>();
            if (string.IsNullOrWhiteSpace(data))
                return false;

            var values = data.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 0 || values.Length % 2 != 0)
                return false;

            var rounds = new HashSet<int>();
            for (var index = 0; index < values.Length; index += 2)
            {
                if (!int.TryParse(values[index], out var completedRounds)
                    || !long.TryParse(values[index + 1], out var experience)
                    || completedRounds < 0
                    || experience < 0
                    || !rounds.Add(completedRounds))
                {
                    result.Clear();
                    return false;
                }

                result.Add(new TournamentRewardExperience
                {
                    CompletedRounds = completedRounds,
                    Experience = experience,
                });
            }

            return result.Count > 0;
        }

        private static List<WarpMapConditionEntry> ParseWarpMapConditions(
            ScriptNode node,
            string text)
        {
            var result = new List<WarpMapConditionEntry>();
            int? sourceX = null;
            int? sourceY = null;

            for (var childIndex = 0; childIndex < node.Children.Count; childIndex++)
            {
                var child = node.Children[childIndex];
                var childData = child.DataItems.Count > 0
                    ? child.GetFirstDataContent(text)
                    : (childIndex == node.Children.Count - 1
                        && node.DataItems.Count > 0
                        ? node.GetFirstDataContent(text)
                        : child.GetContent(text));
                var values = ParseIntArray(childData);
                if (values == null || values.Length < 2)
                    continue;

                if (child.Tag.Equals("source grid pos", StringComparison.OrdinalIgnoreCase))
                {
                    sourceX = values[0];
                    sourceY = values[1];
                    continue;
                }

                if (!child.Tag.Equals("dest grid pos", StringComparison.OrdinalIgnoreCase)
                    || !sourceX.HasValue
                    || !sourceY.HasValue)
                {
                    continue;
                }

                result.Add(new WarpMapConditionEntry
                {
                    SourceX = sourceX.Value,
                    SourceY = sourceY.Value,
                    DestinationX = values[0],
                    DestinationY = values[1],
                });
                sourceX = null;
                sourceY = null;
            }

            return result;
        }

        private static List<NamedMonsterMapPosition> ParseNamedMonsterMapPositions(
            string data)
        {
            var result = new List<NamedMonsterMapPosition>();
            var values = ParseIntArray(data);
            for (var index = 0; index + 1 < values.Length; index += 2)
            {
                result.Add(new NamedMonsterMapPosition
                {
                    X = values[index],
                    Y = values[index + 1],
                });
            }

            return result;
        }

        private static MazeInfo BuildMazeInfo(List<ScriptNode> nodes, string text)
        {
            var maze = new MazeInfo { Nodes = nodes };
            foreach (var node in nodes)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(text).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    case "size":
                        var sz = ParseIntArray(data);
                        if (sz.Length >= 2) { maze.Width = sz[0]; maze.Height = sz[1]; }
                        break;
                    case "greed":
                        maze.Greed = StripBacktick(ReadAllNodeData(node, text));
                        break;
                    case "map specification":
                        maze.MapSpecification = string.IsNullOrEmpty(maze.MapSpecification)
                            ? data
                            : maze.MapSpecification + " " + data;
                        maze.MapSpecifications.AddRange(ParseMapSpecifications(data));
                        break;
                    case "start map":
                        maze.StartMap = ParseIntArray(data);
                        break;
                    case "boss map":
                        maze.BossMap = ParseIntArray(data);
                        break;
                    case "hit count":
                        maze.HitCount = ParseIntArray(data);
                        break;
                    case "seal door appear rate":
                        maze.SealDoorAppearRate = ParseInt(data);
                        break;
                    case "seal door map index":
                        maze.SealDoorMapIndex = ParseInt(data);
                        break;
                    case "seal door pos":
                        maze.SealDoorPos = ParseIntArray(data);
                        break;
                    case "quest connection":
                        maze.QuestConnection = ParseIntArray(data);
                        break;
                    case "randomized object creation":
                        maze.RidableScript = ParseRidableObjectScript(node, text);
                        maze.RidableScripts.Add(maze.RidableScript);
                        break;
                    case "clear condition":
                        maze.ClearConditions = ParseClearConditions(node, text);
                        break;
                    case "boss map specification":
                        maze.BossMapSpecification = string.IsNullOrEmpty(maze.BossMapSpecification)
                            ? data : maze.BossMapSpecification + " " + data;
                        break;
                    case "layered map specification":
                        maze.LayeredMapSpecification = string.IsNullOrEmpty(maze.LayeredMapSpecification)
                            ? data : maze.LayeredMapSpecification + " " + data;
                        break;
                    case "minimap icon":
                        maze.MinimapIcons.AddRange(ParseMazeMinimapIcons(data));
                        break;
                    case "event monster random map":
                        var eventMapValues = ParseIntArray(data);
                        if (eventMapValues.Length > 0)
                            maze.EventMonsterRandomMap = eventMapValues[0];
                        break;
                }
            }
            return maze;
        }

        private static List<MazeMinimapIconInfo> ParseMazeMinimapIcons(string data)
        {
            var result = new List<MazeMinimapIconInfo>();
            var values = ParseStringArray(data);
            for (var index = 0; index + 4 < values.Length; index += 5)
            {
                if (!int.TryParse(values[index], out var x)
                    || !int.TryParse(values[index + 1], out var y)
                    || !int.TryParse(values[index + 3], out var iconIndex)
                    || !int.TryParse(values[index + 4], out var flag))
                {
                    continue;
                }

                result.Add(new MazeMinimapIconInfo
                {
                    X = x,
                    Y = y,
                    Image = StripBacktick(values[index + 2]),
                    IconIndex = iconIndex,
                    Flag = flag,
                });
            }

            return result;
        }

        private static RidableObjectScript ParseRidableObjectScript(ScriptNode node, string text)
        {
            var script = new RidableObjectScript();
            foreach (var child in node.Children)
            {
                var tag = child.Tag.ToLowerInvariant();
                var childData = child.DataItems.Count > 0 ? (child.GetFirstDataContent(text) ?? "").Trim() : "";
                switch (tag)
                {
                    case "select":
                        script.SelectCount = ParseInt(childData);
                        break;
                    case "regenerate":
                        script.Regenerate = ParseInt(childData) != 0;
                        break;
                    case "minimap icon":
                        script.MinimapIcon = ParseInt(childData);
                        break;
                    case "object":
                        var obj = new RidableObject();
                        foreach (var sub in child.Children)
                        {
                            var subTag = sub.Tag.ToLowerInvariant();
                            var subData = sub.DataItems.Count > 0 ? (sub.GetFirstDataContent(text) ?? "").Trim() : "";
                            switch (subTag)
                            {
                                case "map":
                                    var mapVals = ParseIntArray(subData);
                                    if (mapVals != null && mapVals.Length >= 2) { obj.MapX = mapVals[0]; obj.MapY = mapVals[1]; }
                                    break;
                                case "index":
                                    obj.ObjectIndex = ParseInt(subData);
                                    break;
                                case "pos":
                                    var posVals = ParseIntArray(subData);
                                    if (posVals != null && posVals.Length >= 2) { obj.PosX = posVals[0]; obj.PosY = posVals[1]; }
                                    break;
                                case "monster":
                                    obj.Faction = 100;
                                    break;
                                case "neutral":
                                    obj.Faction = 200;
                                    break;
                                case "character":
                                    obj.Faction = 0;
                                    break;
                            }
                        }
                        // [pos] 的数据可能被 ScriptParser 归入 [object] 的 DataItems
                        if (obj.PosX == 0 && obj.PosY == 0 && child.DataItems.Count > 0)
                        {
                            var objData = (child.GetFirstDataContent(text) ?? "").Trim();
                            var fallbackPos = ParseIntArray(objData);
                            if (fallbackPos != null && fallbackPos.Length >= 2)
                            {
                                obj.PosX = fallbackPos[0];
                                obj.PosY = fallbackPos[1];
                            }
                        }
                        if (obj.ObjectIndex > 0)
                            script.Objects.Add(obj);
                        break;
                }
            }
            return script;
        }

        private static readonly Dictionary<string, int> ClearConditionTypeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "destroy object", 0 },
            { "clear map", 1 },
            { "seeking", 1 },
            { "hunt monster", 2 },
            { "hunt apc", 3 },
            { "hunt boss", 4 },
        };

        private static List<ClearConditionEntry> ParseClearConditions(ScriptNode node, string text)
        {
            var result = new List<ClearConditionEntry>();
            var nextGroupId = 1;
            foreach (var child in node.Children)
            {
                int type;
                if (!ClearConditionTypeMap.TryGetValue(child.Tag, out type))
                    continue;
                // ScriptParser 已知局限: 无结束标签的子节点在父节点末尾时数据被归入父节点 DataItems
                // (ParseNodeContent inclusive endLine vs ParseNode exclusive endLine 不匹配)
                var data = child.DataItems.Count > 0
                    ? (child.GetFirstDataContent(text) ?? "").Trim()
                    : (node.DataItems.Count > 0 ? (node.GetFirstDataContent(text) ?? "").Trim() : "");

                if (child.Tag.Equals("clear map", StringComparison.OrdinalIgnoreCase)
                    && TryParseClearMapList(data, nextGroupId, result))
                {
                    nextGroupId++;
                    continue;
                }

                var vals = ParseIntArray(data);
                if (vals == null || vals.Length == 0) continue;
                for (int i = 0; i + 1 < vals.Length; i += 2)
                {
                    result.Add(new ClearConditionEntry { Type = type, TargetId = vals[i], Count = vals[i + 1] });
                }
            }
            return result;
        }

        private static bool TryParseClearMapList(
            string data,
            int groupId,
            List<ClearConditionEntry> result)
        {
            var parts = ParseStringArray(data);
            if (parts.Length == 0
                || !StripBacktick(parts[0]).Equals(
                    "list",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var values = ParseIntArray(data);
            if (values == null || values.Length < 3)
                return false;

            var mapCount = values[0];
            if (mapCount <= 0 || values.Length < mapCount + 2)
                return false;

            var required = values[mapCount + 1];
            if (required <= 0)
                required = 1;

            var seenMapIds = new HashSet<int>();
            for (var i = 0; i < mapCount; i++)
            {
                var mapId = values[i + 1];
                if (mapId <= 0 || !seenMapIds.Add(mapId))
                    continue;

                result.Add(new ClearConditionEntry
                {
                    Type = 1,
                    TargetId = mapId,
                    Count = 1,
                    GroupId = groupId,
                    GroupRequired = required,
                });
            }

            return seenMapIds.Count > 0;
        }

        private static List<MapSpecificationItem> ParseMapSpecifications(string data)
        {
            var result = new List<MapSpecificationItem>();
            var values = ParseStringArray(data);
            var index = 0;

            while (index < values.Length - 3)
            {
                var type = StripBacktick(values[index]);
                if (type == "map" || type == "boss")
                {
                    if (int.TryParse(values[index + 1], out var x) &&
                        int.TryParse(values[index + 2], out var y) &&
                        int.TryParse(values[index + 3], out var mapIndex))
                    {
                        var item = new MapSpecificationItem
                        {
                            Type = type,
                            X = x,
                            Y = y,
                            Index = mapIndex,
                        };

                        // map/boss 均可有多候选 mapId: `boss 9 0 17145 17148` 或 `map 1 0 20322 20323`
                        var candidates = new System.Collections.Generic.List<int> { mapIndex };
                        index += 4;
                        while (index < values.Length && int.TryParse(values[index], out var extra))
                        {
                            candidates.Add(extra);
                            index++;
                        }
                        if (candidates.Count > 1)
                            item.MapCandidates = candidates.ToArray();

                        result.Add(item);
                        continue;
                    }
                }
                else if (type == "layered")
                {
                    if (int.TryParse(values[index + 1], out var lx) &&
                        int.TryParse(values[index + 2], out var ly))
                    {
                        index += 3;
                        var ids = new List<int>();
                        while (index < values.Length && int.TryParse(values[index], out var id))
                        {
                            ids.Add(id);
                            index++;
                        }
                        if (ids.Count > 0)
                        {
                            result.Add(new MapSpecificationItem
                            {
                                Type = "layered",
                                X = lx,
                                Y = ly,
                                Index = ids[0],
                                LayeredMapIds = ids.ToArray(),
                            });
                        }
                        continue;
                    }
                }
                else
                {
                    // 兼容任务地下城等使用非标准类型（如 quest）的 map specification：只要格式为 type x y mapId... 就保留
                    if (int.TryParse(values[index + 1], out var ux) &&
                        int.TryParse(values[index + 2], out var uy) &&
                        int.TryParse(values[index + 3], out var uMapIndex))
                    {
                        var item = new MapSpecificationItem
                        {
                            Type = type,
                            X = ux,
                            Y = uy,
                            Index = uMapIndex,
                        };
                        var candidates = new System.Collections.Generic.List<int> { uMapIndex };
                        index += 4;
                        while (index < values.Length && int.TryParse(values[index], out var extra))
                        {
                            candidates.Add(extra);
                            index++;
                        }
                        if (candidates.Count > 1)
                            item.MapCandidates = candidates.ToArray();

                        result.Add(item);
                        continue;
                    }
                }

                index++;
            }

            return result;
        }

        private static string[] ParseStringArray(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return Array.Empty<string>();

            return data.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        #endregion

        #region 辅助

        private static string ReadAllNodeData(ScriptNode node, string text)
        {
            if (node?.DataItems == null || node.DataItems.Count == 0)
                return string.Empty;

            var rows = new List<string>();
            foreach (var item in node.DataItems)
            {
                var row = item.GetContent(text)?.Trim();
                if (!string.IsNullOrWhiteSpace(row))
                    rows.Add(row);
            }

            return string.Join("\n", rows);
        }

        private static string ReadRawNodeData(ScriptNode node, string text, string data)
        {
            if (!string.IsNullOrWhiteSpace(data))
                return data;
            if (node == null || node.Children == null || node.Children.Count == 0)
                return data ?? string.Empty;
            return node.GetContent(text).Trim();
        }

        private static void ParseCutsceneImage(string data, DungeonFile dgn)
        {
            if (string.IsNullOrEmpty(data)) return;
            // 格式: `path` number
            var parts = data.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1)
                dgn.CutsceneImage = StripBacktick(parts[0]);
            if (parts.Length >= 2)
            {
                int v;
                if (int.TryParse(parts[1], out v))
                    dgn.CutsceneImageParam = v;
            }
        }

        #endregion
    }

    public class MapSpecificationItem
    {
        public string Type { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        public int Index { get; set; }

        public int[] LayeredMapIds { get; set; }

        /// <summary>多候选 mapId (加权随机池), 如 `boss 9 0 17145 17148` 或 `map 1 0 20322 20323`</summary>
        public int[] MapCandidates { get; set; }
    }

    public sealed class SpecialPassiveObjectItemGroup
    {
        public SpecialPassiveObjectItemGroup(
            int groupIndex,
            int levelOverride,
            IReadOnlyList<SpecialPassiveObjectItem> items)
        {
            GroupIndex = groupIndex;
            LevelOverride = levelOverride;
            Items = items ?? Array.Empty<SpecialPassiveObjectItem>();
        }

        public int GroupIndex { get; }
        public int LevelOverride { get; }
        public IReadOnlyList<SpecialPassiveObjectItem> Items { get; }
    }

    public readonly struct SpecialPassiveObjectItem
    {
        public SpecialPassiveObjectItem(int itemId, int weight)
        {
            ItemId = itemId;
            Weight = weight;
        }

        public int ItemId { get; }
        public int Weight { get; }
    }
}
