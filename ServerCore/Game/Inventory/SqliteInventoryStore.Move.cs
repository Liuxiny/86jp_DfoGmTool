// GM瘦身拷贝: 相对服务端原版仅保留 MapToDbListType; 删除了 TryMoveItem, TrySortItems,
// TrySortAccountCargoItems, TryToggleSortItemLock, TryUnlockSortItemLock, LoadSortItemLocks(两个重载),
// LoadCommonItemForRefresh, LoadAvatarItemForRefresh, LoadPetItemForRefresh, 排序锁全部私有助手,
// ResolveDestinationStackTarget, UpdateRecordStackCount, DeleteRecord, InsertSplitRecord, MoveRecordTo,
// GetSortSegmentMap, CreateMoveResult, AttachEquipmentMoveState, BuildSubtype0TailMutation, ResolveForging,
// LoadCreatureNameBytes, IsSupportedMoveListType, IsSupportedSortListType, CanMoveToListType,
// CanMoveToMainSlotRange, CanSwap, CanStack, NormalizeMoveCount, IsStackableRecord, HasStackCapacity,
// GetStackLimit, IsAccountCargoRecord, GetSortPriority; 保留成员与原版逐字一致
using DfoGmTool.ServerCore.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed partial class SqliteInventoryStore
    {
        internal static InventoryListType MapToDbListType(InventoryListType listType)
        {
            if (listType == InventoryListType.Equipment)
                return InventoryListType.Avatar;
            return listType;
        }
    }
}
