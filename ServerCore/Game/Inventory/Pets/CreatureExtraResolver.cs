using System;
using System.Collections.Concurrent;
using System.IO;
using DfoGmTool.ServerCore.GameWorld;
using GmPvfLib;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    public static class CreatureExtraResolver
    {
        private static readonly ConcurrentDictionary<int, bool> CreatureExtraCache = new ConcurrentDictionary<int, bool>();
        private static readonly ConcurrentDictionary<int, bool> PetInventoryEquipmentCache = new ConcurrentDictionary<int, bool>();
        private static readonly object CacheLock = new object();

        private static Lazy<LstFile> EquipmentList = CreateEquipmentList();

        internal static void ResetForPvfChange()
        {
            CreatureExtraCache.Clear();
            PetInventoryEquipmentCache.Clear();
            lock (CacheLock)
                EquipmentList = CreateEquipmentList();
        }

        private static Lazy<LstFile> CreateEquipmentList()
        {
            return new Lazy<LstFile>(() => LstFile.Parse(PvfArchiveAccessor.ReadText("equipment/equipment.lst")));
        }

        
        
        
        
        public static bool HasCreatureExtra(int itemTemplateId)
        {
            return CreatureExtraCache.GetOrAdd(itemTemplateId, ResolveCreatureExtraFromPvf);
        }

        public static bool IsPetInventoryEquipment(int itemTemplateId)
        {
            return PetInventoryEquipmentCache.GetOrAdd(itemTemplateId, ResolvePetInventoryEquipmentFromPvf);
        }

        private static bool ResolveCreatureExtraFromPvf(int itemTemplateId)
        {
            var valueLine = LoadEquipmentTypeLine(itemTemplateId);
            return valueLine != null && valueLine.Contains("[creature]");
        }

        private static bool ResolvePetInventoryEquipmentFromPvf(int itemTemplateId)
        {
            var valueLine = LoadEquipmentTypeLine(itemTemplateId);
            return valueLine != null &&
                (valueLine.Contains("[creature]")
                    || valueLine.Contains("[artifact red]")
                    || valueLine.Contains("[artifact blue]")
                    || valueLine.Contains("[artifact green]"));
        }

        private static string LoadEquipmentTypeLine(int itemTemplateId)
        {
            var entry = EquipmentList.Value.GetById(itemTemplateId);
            if (entry == null)
                throw new InvalidDataException(
                    $"[CreatureExtraResolver] item {itemTemplateId} 不在 equipment.lst — 无法判定 creature extra 边界");

            var text = PvfArchiveAccessor.ReadText(Path.Combine("equipment", entry.FilePath));

            
            int typeIdx = text.IndexOf("[equipment type]", StringComparison.Ordinal);
            if (typeIdx >= 0)
            {
                int lineEnd = text.IndexOf('\n', typeIdx);
                int valueEnd = lineEnd >= 0 ? text.IndexOf('\n', lineEnd + 1) : -1;
                var valueLine = lineEnd >= 0
                    ? text.Substring(lineEnd + 1, (valueEnd >= 0 ? valueEnd : text.Length) - lineEnd - 1)
                    : "";
                return valueLine;
            }

            return null;
        }
    }
}
