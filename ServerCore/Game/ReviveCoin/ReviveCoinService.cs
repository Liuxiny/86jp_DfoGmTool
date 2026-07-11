// GM瘦身拷贝: 相对服务端原版仅保留常量(ItemId/WalletSlot/ConsumableItemId/DailyClaimKey)与
// IsReviveCoinReward; 删除了 GrantToWallet、每日领取/死亡消耗实例成员(依赖 DailyResetService);
// 保留成员与原版逐字一致
using System;

namespace DfoGmTool.ServerCore.Game.ReviveCoin
{
    // 复活币功能域 — 常量与全部用例的唯一归属地。
    //
    // 复活币实体: itemId=1 固定 Main slot1(86种子实证: 角色1002 list0/slot1/id1/stackable
    // x3368; 钱包区布局 slot0=金币/slot1=复活币/slot2=SP)。PVF 无 id=1 词条, 属服务端
    // 合成物品, 拾取走 SqliteInventoryStore.TryPickupItemCore 专线(不能过 metadata 解析)。
    public sealed class ReviveCoinService
    {
        public const int ItemId = 1;
        public const short WalletSlot = 1;
        // PVF: stackable/cash/coin_general.stk, name=復活コイン, type=[waste]
        public const int ConsumableItemId = 42;

        public static bool IsReviveCoinReward(int itemTemplateId)
        {
            return itemTemplateId == ItemId || itemTemplateId == ConsumableItemId;
        }

        // 每日领取标记(账本 key, cap=1)
        public const string DailyClaimKey = "revive_coin_daily_claim";
    }
}
