using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GmPvfLib
{
    public class MapBackgroundAnimation
    {
        public string Filename { get; set; }
        public string Layer { get; set; }
        public string Order { get; set; }
    }

    public enum MonsterType
    {
        Normal,
        Champion,
        SuperChampion,
        Boss,
        MaxValue
    }

    public class MonsterInfo
    {
        public int? MonsterId { get; set; }
        public int? NpcId { get; set; }
        public int? AutoLv { get; set; }
        public int? Lv { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Z { get; set; }
        public int? ConditionalParam0 { get; set; }
        public int? ConditionalParam1 { get; set; }
        public int? ConditionalParam2 { get; set; }
        public int? RandomDropCnt { get; set; }
        public int? SpecifyDropCnt { get; set; }
        public string Fixed { get; set; }
        public MonsterType Type { get; set; }
    }

    public class PassiveObjectInfo
    {
        public int ObjectCode { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Flags { get; set; }
    }

    public class HellPartyMapEntry
    {
        public int GroupId { get; set; }
        public int Rate { get; set; }
        public int Order { get; set; }
    }

    public class SpecialPassiveObjectInfo
    {
        public int ObjectCode { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Flags { get; set; }
        public List<SpecialPassiveObjectSpawnInfo> Spawns { get; set; } = new List<SpecialPassiveObjectSpawnInfo>();
        public List<HellPartyMapEntry> HellPartyEntries { get; set; } = new List<HellPartyMapEntry>();
    }

    public class SpecialPassiveObjectSpawnInfo
    {
        public string Kind { get; set; }
        public int Code { get; set; }
        public int Level { get; set; }
        public int Param0 { get; set; }
        public int Param1 { get; set; }
        public int Param2 { get; set; }
    }

    public class EventMonsterPositionInfo
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    public class MapNpcInfo
    {
        public int NpcId { get; set; }
        public string Direction { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Flags { get; set; }
    }

    public enum ApcFaction
    {
        Character = 0,
        Monster = 100,
        Neutral = 200,
    }

    public enum ApcAIType
    {
        Normal = 5,
        Champion = 6,
        Boss = 8,
    }

    public class AICharacterInfo
    {
        public int Code { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Direction { get; set; }
        public ApcFaction? Faction { get; set; }
        public ApcAIType AIType { get; set; }
    }

    public class TournamentEnemyInfo
    {
        public int PartyCount { get; set; }
        public bool IsApc { get; set; }
        public int Code { get; set; }
        public int Strength { get; set; }
        public string Name { get; set; }
    }

    public class TournamentStartAreaInfo
    {
        public int PartyCount { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Direction { get; set; }
    }

    /// <summary>
    /// </summary>
    public class MapFile : PvfModelBase
    {
        public string MapName { get; set; }
        public int[] PlayerNumber { get; set; }
        public int[] PvpStartArea { get; set; }
        public int DungeonId { get; set; } = -1;
        public string Type { get; set; }
        public string Greed { get; set; }
        public List<string> Tiles { get; set; } = new List<string>();
        public int FarSightScroll { get; set; } = -1;
        public int MiddleSightScroll { get; set; } = -1;
        public int NearSightScroll { get; set; } = -1;
        public List<MapBackgroundAnimation> BackgroundAnimations { get; set; } = new List<MapBackgroundAnimation>();
        public int[] PathgatePos { get; set; }
        public List<string> Sounds { get; set; } = new List<string>();
        public int AnimationObjectCount { get; set; } = -1;
        public int PassiveObjectCount { get; set; } = -1;
        public List<PassiveObjectInfo> PassiveObjects { get; set; } = new List<PassiveObjectInfo>();
        public int SpecialPassiveObjectCount { get; set; } = -1;
        public List<SpecialPassiveObjectInfo> SpecialPassiveObjects { get; set; } = new List<SpecialPassiveObjectInfo>();
        public int MonsterCount { get; set; } = -1;
        public List<MonsterInfo> Monsters { get; set; } = new List<MonsterInfo>();
        public List<MonsterInfo> MonsterConditionMonsters { get; set; } = new List<MonsterInfo>();
        public List<MonsterInfo> ConditionalSummonMonsters { get; set; } = new List<MonsterInfo>();
        public int EventMonsterPositionCount { get; set; } = -1;
        public List<EventMonsterPositionInfo> EventMonsterPositions { get; set; } = new List<EventMonsterPositionInfo>();
        public int NpcCount { get; set; } = -1;
        public List<MapNpcInfo> Npcs { get; set; } = new List<MapNpcInfo>();
        public string MonsterSpecificAI { get; set; }
        public string Buff { get; set; }
        public List<AICharacterInfo> AICharacters { get; set; } = new List<AICharacterInfo>();
        public List<TournamentEnemyInfo> TournamentEnemyCandidates { get; set; } =
            new List<TournamentEnemyInfo>();
        public List<TournamentStartAreaInfo> TournamentStartAreas { get; set; } =
            new List<TournamentStartAreaInfo>();
        public bool TournamentDefinitionMalformed { get; set; }

        // --- Simple int properties ---
        public int FixChampion { get; set; } = -1;
        public int HeroesModeMapIndex { get; set; } = -1;
        public int BackgroundCorrection { get; set; } = -1;
        public int BackgroundPosValue { get; set; } = -1;
        public int ForegroundPatternAlpha { get; set; } = -1;
        public int ApcRandomPoint { get; set; } = -1;
        public int MonsterLock { get; set; } = -1;
        public int DrawMonsterCount { get; set; } = -1;
        public int SortBottom { get; set; } = -1;
        public int AddGravity { get; set; } = -1;
        public int JumpPowerRate { get; set; } = -1;

        // --- Bool flags ---
        public bool BlockUseStackableItem { get; set; }
        public bool BlockUseActiveSkill { get; set; }
        public bool VisibleOnDungeonClear { get; set; }
        public bool LoopYAxis { get; set; }
        public bool AllDeadCasePassable { get; set; }
        public bool DisableItemEscapeStuck { get; set; }
        public bool DisableCharacterEscapeStuck { get; set; }
        public bool CannotUseCoinMap { get; set; }
        public bool NoRevivalTimerLimit { get; set; }
        public bool IgnoreDiehard { get; set; }
        public bool DisableRebirth { get; set; }
        public bool PreservePlayerCorpse { get; set; }
        public bool CannotUseResolutionChangeZoom { get; set; }
        public bool CenterFixedCamera { get; set; }
        public bool ForceDrawPattern { get; set; }
        public bool IsRevival { get; set; }
        public bool IsMoiveEnd { get; set; }
        public bool QuestStartMap { get; set; }
        public bool HideMonster { get; set; }
        public bool ShowDust { get; set; }

        // --- Int array properties ---
        public int[] DungeonStartArea { get; set; }
        public int[] ScreenPos { get; set; }
        public int[] MonsterTeam { get; set; }
        public int[] PvpPracticeStartArea { get; set; }
        public int[] VirtualMovableArea { get; set; }
        public int[] TownMovableArea { get; set; }
        public int[] PathgateObject { get; set; }

        // --- String properties ---
        public string OpeningBgm { get; set; }
        public string MapLoadingImagePath { get; set; }
        public string BasicAction { get; set; }
        public string MapDialog { get; set; }
        public string Dust { get; set; }
        public string AbsoluteStartPath { get; set; }

        // --- Complex raw string properties ---
        public string MonsterCondition { get; set; }
        public string MonsterSpawnPos { get; set; }
        public string BloodMonster { get; set; }
        public string BloodPhaseTime { get; set; }
        public string UltimateMonster { get; set; }
        public string UltimatePhaseTime { get; set; }
        public string Darkness { get; set; }
        public string StaticPlayerStartPos { get; set; }
        public string BeltScrollMap { get; set; }
        public string MoveLayeredMap { get; set; }
        public string CustomizedScreenEdge { get; set; }
        public string ExtendedTile { get; set; }
        public string ScrollAnimation { get; set; }
        public string ConditionalSummonMonster { get; set; }
        public string MapOverMoveAni { get; set; }
        public string CameraForceMove { get; set; }
        public string CameraEdgeException { get; set; }
        public string ReviveWithDlg { get; set; }
        public string ZoneDefence { get; set; }
        public string TournamentEnemies { get; set; }
        public string TournamentStartArea { get; set; }
        public string BeforeRenderingInfo { get; set; }
        public string TimeLine { get; set; }
        public string SummonStartArea { get; set; }
        public string MapFrame { get; set; }
        public string TileOption { get; set; }
        public string BackgroundEffect { get; set; }
        public string BlockEffect { get; set; }
        public string Item { get; set; }
        public string Quest { get; set; }
        public string ApcCreateCondition { get; set; }
        public string MapAnimation { get; set; }
        public string RevivalMap { get; set; }
        public string BlockPath { get; set; }

        private static readonly Regex BacktickStringRx = new Regex("`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex AniReferenceRx = new Regex("`[^`]+\\.ani`", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex InlineHellPartyRx = new Regex(
            @"`?\[hellparty\]`?(?<body>.*?)`?\[/hellparty\]`?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex SpecialPassiveTokenRx = new Regex(@"`[^`]*`|\S+", RegexOptions.Compiled);

        public static MapFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new MapFile { Content = content ?? string.Empty, Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var map = new MapFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : string.Empty;
                switch (node.Tag.ToLowerInvariant())
                {
                    case "map name":
                        map.MapName = StripBacktick(data);
                        break;
                    case "player number":
                        map.PlayerNumber = ParseIntArray(data);
                        break;
                    case "pvp start area":
                        map.PvpStartArea = ParseIntArray(data);
                        break;
                    case "dungeon":
                        map.DungeonId = ParseInt(data);
                        break;
                    case "type":
                        map.Type = StripBacktick(data);
                        break;
                    case "greed":
                        map.Greed = StripBacktick(data);
                        break;
                    case "tile":
                        map.Tiles.AddRange(ParseBacktickStrings(data));
                        break;
                    case "far sight scroll":
                        map.FarSightScroll = ParseInt(data);
                        break;
                    case "middle sight scroll":
                        map.MiddleSightScroll = ParseInt(data);
                        break;
                    case "near sight scroll":
                        map.NearSightScroll = ParseInt(data);
                        break;
                    case "background animation":
                        map.BackgroundAnimations.AddRange(ParseBackgroundAnimations(node, content));
                        break;
                    case "pathgate pos":
                        map.PathgatePos = ParseIntArray(data);
                        break;
                    case "sound":
                        map.Sounds.AddRange(ParseBacktickStrings(data));
                        break;
                    case "animation":
                        map.AnimationObjectCount = CountAnimationReferences(data);
                        break;
                    case "passive object":
                        map.PassiveObjectCount = CountNumberGroups(data, 4);
                        map.PassiveObjects = ParsePassiveObjects(data);
                        break;
                    case "special passive object":
                        map.SpecialPassiveObjectCount = CountNumberGroups(data, 4);
                        map.SpecialPassiveObjects = ParseSpecialPassiveObjects(data);
                        break;
                    case "monster":
                        map.Monsters = ParseMonsters(data);
                        map.MonsterCount = map.Monsters.Count;
                        break;
                    case "event monster position":
                        map.EventMonsterPositionCount = CountNumberGroups(data, 3);
                        map.EventMonsterPositions = ParseEventMonsterPositions(data);
                        break;
                    case "npc":
                        var npcData = GetAllDataContent(node, content);
                        map.NpcCount = CountNumberGroups(npcData, 4);
                        map.Npcs = ParseNpcs(npcData);
                        break;
                    case "monster specific ai":
                        map.MonsterSpecificAI = data;
                        break;
                    case "buff":
                        map.Buff = data;
                        break;
                    case "ai character":
                        map.AICharacters = ParseAICharacters(data);
                        break;

                    // --- Simple int ---
                    case "fix champion":
                        map.FixChampion = ParseInt(data);
                        break;
                    case "heroes mode map index":
                        map.HeroesModeMapIndex = ParseInt(data);
                        break;
                    case "background correction":
                        map.BackgroundCorrection = ParseInt(data);
                        break;
                    case "background pos":
                        map.BackgroundPosValue = ParseInt(data);
                        break;
                    case "foreground pattern alpha":
                        map.ForegroundPatternAlpha = ParseInt(data);
                        break;
                    case "apc random point":
                        map.ApcRandomPoint = ParseInt(data);
                        break;
                    case "monster lock":
                        map.MonsterLock = ParseInt(data);
                        break;
                    case "draw monster count":
                        map.DrawMonsterCount = ParseInt(data);
                        break;
                    case "sort bottom":
                        map.SortBottom = ParseInt(data);
                        break;
                    case "add gravity":
                        map.AddGravity = ParseInt(data);
                        break;
                    case "jump power rate":
                        map.JumpPowerRate = ParseInt(data);
                        break;

                    // --- Bool flags ---
                    case "block use stackable item":
                        map.BlockUseStackableItem = true;
                        break;
                    case "block use active skill":
                        map.BlockUseActiveSkill = true;
                        break;
                    case "visible on dungeon clear":
                        map.VisibleOnDungeonClear = true;
                        break;
                    case "loop y axis":
                        map.LoopYAxis = true;
                        break;
                    case "all dead case passable":
                        map.AllDeadCasePassable = true;
                        break;
                    case "disable item escape stuck":
                        map.DisableItemEscapeStuck = true;
                        break;
                    case "disable character escape stuck":
                        map.DisableCharacterEscapeStuck = true;
                        break;
                    case "cannot use coin map":
                        map.CannotUseCoinMap = true;
                        break;
                    case "no revival timer limit":
                        map.NoRevivalTimerLimit = true;
                        break;
                    case "ignore diehard":
                        map.IgnoreDiehard = true;
                        break;
                    case "disable rebirth":
                        map.DisableRebirth = true;
                        break;
                    case "preserve player corpse":
                        map.PreservePlayerCorpse = true;
                        break;
                    case "cannot use resolution change zoom":
                        map.CannotUseResolutionChangeZoom = true;
                        break;
                    case "center fixed camera":
                        map.CenterFixedCamera = true;
                        break;
                    case "force draw pattern":
                        map.ForceDrawPattern = true;
                        break;
                    case "is revival":
                        map.IsRevival = true;
                        break;
                    case "is moive end":
                        map.IsMoiveEnd = true;
                        break;
                    case "quest start map":
                        map.QuestStartMap = true;
                        break;
                    case "hide monster":
                        map.HideMonster = true;
                        break;
                    case "show dust":
                        map.ShowDust = true;
                        break;

                    // --- Int array ---
                    case "dungeon start area":
                        map.DungeonStartArea = ParseIntArray(data);
                        break;
                    case "screen pos":
                        map.ScreenPos = ParseIntArray(data);
                        break;
                    case "monster team":
                        map.MonsterTeam = ParseIntArray(data);
                        break;
                    case "pvp practice start area":
                        map.PvpPracticeStartArea = ParseIntArray(data);
                        break;
                    case "virtual movable area":
                        map.VirtualMovableArea = ParseIntArray(GetAllDataContent(node, content));
                        break;
                    case "town movable area":
                        map.TownMovableArea = ParseIntArray(data);
                        break;
                    case "pathgate object":
                        map.PathgateObject = ParseIntArray(data);
                        break;

                    // --- String ---
                    case "opening bgm":
                        map.OpeningBgm = data;
                        break;
                    case "map loading image path":
                        map.MapLoadingImagePath = StripBacktick(data);
                        break;
                    case "basic action":
                        map.BasicAction = StripBacktick(data);
                        break;
                    case "map dialog":
                        map.MapDialog = data;
                        break;
                    case "dust":
                        map.Dust = data;
                        break;
                    case "absolute start path":
                        map.AbsoluteStartPath = data;
                        break;

                    // --- Complex raw string ---
                    case "monster condition":
                        map.MonsterCondition = data;
                        map.MonsterConditionMonsters = ParseMonsters(data);
                        break;
                    case "monster spawn pos":
                        map.MonsterSpawnPos = data;
                        break;
                    case "blood monster":
                        map.BloodMonster = data;
                        break;
                    case "blood phase time":
                        map.BloodPhaseTime = data;
                        break;
                    case "ultimate monster":
                        map.UltimateMonster = data;
                        break;
                    case "ultimate phase time":
                        map.UltimatePhaseTime = data;
                        break;
                    case "darkness":
                        map.Darkness = data;
                        break;
                    case "static player start pos":
                        map.StaticPlayerStartPos = data;
                        break;
                    case "belt scroll map":
                        map.BeltScrollMap = data;
                        break;
                    case "move layered map":
                        map.MoveLayeredMap = data;
                        break;
                    case "customized screen edge":
                        map.CustomizedScreenEdge = data;
                        break;
                    case "extended tile":
                        map.ExtendedTile = data;
                        break;
                    case "scroll animation":
                        map.ScrollAnimation = data;
                        break;
                    case "conditional summon monster":
                        map.ConditionalSummonMonster = data;
                        map.ConditionalSummonMonsters = ParseConditionalSummonMonsters(data);
                        break;
                    case "map over move ani":
                        map.MapOverMoveAni = data;
                        break;
                    case "camera force move":
                        map.CameraForceMove = data;
                        break;
                    case "camera edge exception":
                        map.CameraEdgeException = data;
                        break;
                    case "revive with dlg":
                        map.ReviveWithDlg = data;
                        break;
                    case "zone defence":
                        map.ZoneDefence = data;
                        break;
                    case "tournament enemies":
                        map.TournamentEnemies = data;
                        if (TryParseTournamentEnemies(data, out var enemies))
                            map.TournamentEnemyCandidates.AddRange(enemies);
                        else
                            map.TournamentDefinitionMalformed = true;
                        break;
                    case "tournament start area":
                        map.TournamentStartArea = data;
                        if (TryParseTournamentStartAreas(data, out var startAreas))
                            map.TournamentStartAreas.AddRange(startAreas);
                        else
                            map.TournamentDefinitionMalformed = true;
                        break;
                    case "before rendering info":
                        map.BeforeRenderingInfo = data;
                        break;
                    case "time line":
                        map.TimeLine = data;
                        break;
                    case "summon start area":
                        map.SummonStartArea = data;
                        break;
                    case "map frame":
                        map.MapFrame = data;
                        break;
                    case "tile option":
                        map.TileOption = data;
                        break;
                    case "background effect":
                        map.BackgroundEffect = data;
                        break;
                    case "block effect":
                        map.BlockEffect = data;
                        break;
                    case "item":
                        map.Item = data;
                        break;
                    case "quest":
                        map.Quest = data;
                        break;
                    case "apc create condition":
                        map.ApcCreateCondition = data;
                        break;
                    case "map animation":
                        map.MapAnimation = data;
                        break;
                    case "revival map":
                        map.RevivalMap = data;
                        break;
                    case "block path":
                        map.BlockPath = data;
                        break;
                }
            }

            return map;
        }

        private static List<MapBackgroundAnimation> ParseBackgroundAnimations(ScriptNode node, string content)
        {
            var result = new List<MapBackgroundAnimation>();
            foreach (var child in node.GetChildren("ani info"))
            {
                var info = new MapBackgroundAnimation();
                var filename = child.GetChild("filename");
                var layer = child.GetChild("layer");
                var order = child.GetChild("order");
                if (filename != null) info.Filename = StripBacktick(filename.GetFirstDataContent(content));
                if (layer != null) info.Layer = StripBacktick(layer.GetFirstDataContent(content));
                if (order != null) info.Order = StripBacktick(order.GetFirstDataContent(content));
                result.Add(info);
            }
            return result;
        }

        private static List<string> ParseBacktickStrings(string data)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(data)) return result;

            var matches = BacktickStringRx.Matches(data);
            foreach (Match match in matches)
                result.Add(match.Groups[1].Value);
            return result;
        }

        private static int CountAnimationReferences(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return -1;
            return AniReferenceRx.Matches(data).Count;
        }

        private static int CountNumberGroups(string data, int groupSize)
        {
            if (string.IsNullOrWhiteSpace(data) || groupSize <= 0) return -1;
            var numbers = ParseIntArray(data);
            if (numbers.Length == 0) return -1;
            return numbers.Length / groupSize;
        }

        private static string GetAllDataContent(ScriptNode node, string content)
        {
            return string.Join(" ", node.DataItems
                .Select(item => item.GetContent(content).Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        private static List<AICharacterInfo> ParseAICharacters(string data)
        {
            var result = new List<AICharacterInfo>();
            var values = Regex.Split(data.Trim(), @"\s+");
            int i = 0;
            while (i < values.Length)
            {
                int code;
                if (!int.TryParse(values[i], out code)) break;
                var entry = new AICharacterInfo { Code = code };
                if (i + 1 < values.Length) { int v; if (int.TryParse(values[i + 1], out v)) entry.X = v; }
                if (i + 2 < values.Length) { int v; if (int.TryParse(values[i + 2], out v)) entry.Y = v; }
                if (i + 3 < values.Length) { int v; if (int.TryParse(values[i + 3], out v)) entry.Direction = v; }
                i += 4;
                if (i < values.Length)
                {
                    var f = StripBacktick(values[i]).ToLowerInvariant();
                    if (f == "[character]") entry.Faction = ApcFaction.Character;
                    else if (f == "[monster]") entry.Faction = ApcFaction.Monster;
                    else if (f == "[neutral]") entry.Faction = ApcFaction.Neutral;
                    i++;
                }
                if (i < values.Length)
                {
                    var a = StripBacktick(values[i]).ToLowerInvariant();
                    if (a == "[normal]") entry.AIType = ApcAIType.Normal;
                    else if (a == "[champion]") entry.AIType = ApcAIType.Champion;
                    else if (a == "[boss]") entry.AIType = ApcAIType.Boss;
                    i++;
                }
                // 末尾两个数值字段当前未使用。
                for (int skip = 0; skip < 2 && i < values.Length; skip++)
                {
                    int dummy;
                    if (int.TryParse(values[i], out dummy)) i++;
                    else break;
                }
                result.Add(entry);
            }
            return result;
        }

        private static List<MonsterInfo> ParseMonsters(string data)
        {
            var result = new List<MonsterInfo>();
            if (string.IsNullOrWhiteSpace(data))
                return result;

            var values = data.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index + 9 < values.Length;)
            {
                var typeToken = StripBacktick(values[index + 9]);
                int? npcId = null;
                var recordLength = 10;

                // Quest maps can bind a monster actor to the NPC it becomes after
                // the encounter: ... [fixed] [NPC] npcId [boss].
                if (string.Equals(typeToken, "[NPC]", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(typeToken, "NPC", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 11 >= values.Length)
                        break;

                    npcId = ParseNullableInt(values[index + 10]);
                    typeToken = StripBacktick(values[index + 11]);
                    recordLength = 12;
                }

                result.Add(new MonsterInfo
                {
                    MonsterId = ParseNullableInt(values[index]),
                    NpcId = npcId,
                    Lv = ParseNullableInt(values[index + 1]),
                    AutoLv = ParseNullableInt(values[index + 2]),
                    X = ParseNullableInt(values[index + 3]),
                    Y = ParseNullableInt(values[index + 4]),
                    Z = ParseNullableInt(values[index + 5]),
                    RandomDropCnt = ParseNullableInt(values[index + 6]),
                    SpecifyDropCnt = ParseNullableInt(values[index + 7]),
                    Fixed = StripBacktick(values[index + 8]),
                    Type = ParseMonsterType(typeToken),
                });

                index += recordLength;
            }

            return result;
        }

        private static List<MonsterInfo> ParseConditionalSummonMonsters(string data)
        {
            var result = new List<MonsterInfo>();
            if (string.IsNullOrWhiteSpace(data))
                return result;

            var values = data.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index + 9 < values.Length;)
            {
                var hasTailPosition = index + 12 < values.Length
                    && int.TryParse(values[index + 10], out _)
                    && int.TryParse(values[index + 11], out _)
                    && int.TryParse(values[index + 12], out _);

                result.Add(new MonsterInfo
                {
                    MonsterId = ParseNullableInt(values[index]),
                    Lv = ParseNullableInt(values[index + 1]),
                    AutoLv = ParseNullableInt(values[index + 2]),
                    ConditionalParam0 = ParseNullableInt(values[index + 3]),
                    ConditionalParam1 = ParseNullableInt(values[index + 4]),
                    ConditionalParam2 = ParseNullableInt(values[index + 5]),
                    X = hasTailPosition ? ParseNullableInt(values[index + 10]) : ParseNullableInt(values[index + 3]),
                    Y = hasTailPosition ? ParseNullableInt(values[index + 11]) : ParseNullableInt(values[index + 4]),
                    Z = hasTailPosition ? ParseNullableInt(values[index + 12]) : ParseNullableInt(values[index + 5]),
                    RandomDropCnt = ParseNullableInt(values[index + 6]),
                    SpecifyDropCnt = ParseNullableInt(values[index + 7]),
                    Fixed = StripBacktick(values[index + 8]),
                    Type = ParseMonsterType(StripBacktick(values[index + 9])),
                });

                index += hasTailPosition ? 13 : 10;
            }

            return result;
        }

        private static List<PassiveObjectInfo> ParsePassiveObjects(string data)
        {
            var result = new List<PassiveObjectInfo>();
            if (string.IsNullOrWhiteSpace(data)) return result;
            var nums = ParseIntArray(data);
            for (int i = 0; i + 3 < nums.Length; i += 4)
            {
                result.Add(new PassiveObjectInfo
                {
                    ObjectCode = nums[i],
                    X = nums[i + 1],
                    Y = nums[i + 2],
                    Flags = nums[i + 3],
                });
            }
            return result;
        }

        private static List<EventMonsterPositionInfo> ParseEventMonsterPositions(string data)
        {
            var result = new List<EventMonsterPositionInfo>();
            if (string.IsNullOrWhiteSpace(data))
                return result;

            var values = ParseIntArray(data);
            for (var index = 0; index + 2 < values.Length; index += 3)
            {
                result.Add(new EventMonsterPositionInfo
                {
                    X = values[index],
                    Y = values[index + 1],
                    Z = values[index + 2],
                });
            }

            return result;
        }

        private static List<MapNpcInfo> ParseNpcs(string data)
        {
            var result = new List<MapNpcInfo>();
            if (string.IsNullOrWhiteSpace(data))
                return result;

            var values = data.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (values.Length % 5 != 0)
                return result;

            for (var index = 0; index < values.Length; index += 5)
            {
                if (!int.TryParse(values[index], out var npcId)
                    || !int.TryParse(values[index + 2], out var x)
                    || !int.TryParse(values[index + 3], out var y)
                    || !int.TryParse(values[index + 4], out var flags))
                {
                    result.Clear();
                    return result;
                }

                result.Add(new MapNpcInfo
                {
                    NpcId = npcId,
                    Direction = StripBacktick(values[index + 1]),
                    X = x,
                    Y = y,
                    Flags = flags,
                });
            }

            return result;
        }

        private static bool TryParseTournamentEnemies(
            string data,
            out List<TournamentEnemyInfo> result)
        {
            result = new List<TournamentEnemyInfo>();
            if (!TryTokenize(data, out var tokens) || tokens.Count < 5)
                return false;

            if (!int.TryParse(tokens[0], out var partyCount)
                || partyCount <= 0)
            {
                return false;
            }

            var kind = StripBacktick(tokens[1]).Trim();
            var isApc = string.Equals(
                kind,
                "[apc]",
                StringComparison.OrdinalIgnoreCase);
            if (!isApc
                && !string.Equals(
                    kind,
                    "[monster]",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if ((tokens.Count - 2) % 3 != 0)
                return false;

            for (var index = 2; index < tokens.Count; index += 3)
            {
                if (!int.TryParse(tokens[index], out var code)
                    || !int.TryParse(tokens[index + 1], out var strength))
                {
                    result.Clear();
                    return false;
                }

                var name = StripBacktick(tokens[index + 2]).Trim();
                if (string.IsNullOrEmpty(name))
                {
                    result.Clear();
                    return false;
                }

                result.Add(new TournamentEnemyInfo
                {
                    PartyCount = partyCount,
                    IsApc = isApc,
                    Code = code,
                    Strength = strength,
                    Name = name,
                });
            }

            return result.Count > 0;
        }

        private static bool TryParseTournamentStartAreas(
            string data,
            out List<TournamentStartAreaInfo> result)
        {
            result = new List<TournamentStartAreaInfo>();
            if (!TryTokenize(data, out var tokens)
                || tokens.Count == 0
                || tokens.Count % 4 != 0)
            {
                return false;
            }

            for (var index = 0; index < tokens.Count; index += 4)
            {
                if (!int.TryParse(tokens[index], out var partyCount)
                    || !int.TryParse(tokens[index + 1], out var x)
                    || !int.TryParse(tokens[index + 2], out var y)
                    || !int.TryParse(tokens[index + 3], out var direction)
                    || partyCount <= 0)
                {
                    result.Clear();
                    return false;
                }

                result.Add(new TournamentStartAreaInfo
                {
                    PartyCount = partyCount,
                    X = x,
                    Y = y,
                    Direction = direction,
                });
            }

            return result.Count > 0;
        }

        private static bool TryTokenize(string data, out List<string> tokens)
        {
            tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(data))
                return false;

            foreach (Match match in SpecialPassiveTokenRx.Matches(data))
                tokens.Add(match.Value);
            return tokens.Count > 0;
        }

        private static List<SpecialPassiveObjectInfo> ParseSpecialPassiveObjects(string data)
        {
            var result = new List<SpecialPassiveObjectInfo>();
            if (string.IsNullOrWhiteSpace(data)) return result;

            var hellMatch = InlineHellPartyRx.Match(data);
            var head = hellMatch.Success ? data.Substring(0, hellMatch.Index) : data;
            if (!TryParseSpecialPassiveObjectsWithSpawns(head, result))
            {
                var nums = ParseIntArray(head);
                for (int i = 0; i + 3 < nums.Length; i += 4)
                {
                    result.Add(new SpecialPassiveObjectInfo
                    {
                        ObjectCode = nums[i],
                        X = nums[i + 1],
                        Y = nums[i + 2],
                        Flags = nums[i + 3],
                    });
                }
            }

            if (hellMatch.Success && result.Count > 0)
            {
                var entries = ParseHellPartyEntries(hellMatch.Groups["body"].Value);
                if (entries.Count > 0)
                    result[result.Count - 1].HellPartyEntries.AddRange(entries);
            }

            return result;
        }

        private static bool TryParseSpecialPassiveObjectsWithSpawns(
            string data,
            List<SpecialPassiveObjectInfo> result)
        {
            if (string.IsNullOrWhiteSpace(data))
                return false;

            var matches = SpecialPassiveTokenRx.Matches(data);
            if (matches.Count < 5)
                return false;

            var tokens = new List<string>(matches.Count);
            foreach (Match match in matches)
                tokens.Add(match.Value);

            var parsed = new List<SpecialPassiveObjectInfo>();
            var i = 0;
            while (i < tokens.Count)
            {
                int objectCode, x, y, flags, spawnCount;
                if (i + 4 >= tokens.Count
                    || !int.TryParse(tokens[i], out objectCode)
                    || !int.TryParse(tokens[i + 1], out x)
                    || !int.TryParse(tokens[i + 2], out y)
                    || !int.TryParse(tokens[i + 3], out flags)
                    || !int.TryParse(tokens[i + 4], out spawnCount)
                    || spawnCount < 0)
                {
                    return false;
                }

                var obj = new SpecialPassiveObjectInfo
                {
                    ObjectCode = objectCode,
                    X = x,
                    Y = y,
                    Flags = flags,
                };
                i += 5;

                if (spawnCount > (tokens.Count - i) / 6)
                    return false;

                for (var spawnIndex = 0; spawnIndex < spawnCount && i < tokens.Count; spawnIndex++)
                {
                    var kind = StripBacktick(tokens[i]);
                    if (string.IsNullOrEmpty(kind) || kind[0] != '[')
                        return false;

                    int code, level, p0, p1, p2;
                    if (i + 5 >= tokens.Count
                        || !int.TryParse(tokens[i + 1], out code)
                        || !int.TryParse(tokens[i + 2], out level)
                        || !int.TryParse(tokens[i + 3], out p0)
                        || !int.TryParse(tokens[i + 4], out p1)
                        || !int.TryParse(tokens[i + 5], out p2))
                    {
                        return false;
                    }

                    obj.Spawns.Add(new SpecialPassiveObjectSpawnInfo
                    {
                        Kind = kind,
                        Code = code,
                        Level = level,
                        Param0 = p0,
                        Param1 = p1,
                        Param2 = p2,
                    });
                    i += 6;
                }

                parsed.Add(obj);
            }

            if (parsed.Count == 0 || i != tokens.Count)
                return false;

            result.AddRange(parsed);
            return true;
        }

        private static List<HellPartyMapEntry> ParseHellPartyEntries(string data)
        {
            var result = new List<HellPartyMapEntry>();
            var nums = ParseIntArray(data);
            for (int i = 0; i + 2 < nums.Length; i += 3)
            {
                result.Add(new HellPartyMapEntry
                {
                    GroupId = nums[i],
                    Rate = nums[i + 1],
                    Order = nums[i + 2],
                });
            }

            return result;
        }

        private static int? ParseNullableInt(string value)
        {
            return int.TryParse(value, out var result) ? result : (int?)null;
        }

        private static MonsterType ParseMonsterType(string value)
        {
            switch (value)
            {
                case "[normal]":
                case "normal":
                    return MonsterType.Normal;
                case "[champion]":
                case "champion":
                    return MonsterType.Champion;
                case "[super champion]":
                case "super champion":
                    return MonsterType.SuperChampion;
                case "[boss]":
                case "boss":
                    return MonsterType.Boss;
                default:
                    return MonsterType.MaxValue;
            }
        }
    }
}
