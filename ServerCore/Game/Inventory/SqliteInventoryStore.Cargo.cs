// GM瘦身拷贝: 相对服务端原版仅保留 NormalizePersonalCargoListParam; 删除了金库金币存取、
// 账号金库开通/升级、个人金库升级(券)、升级道具识别等全部其余成员; 保留成员与原版逐字一致
using DfoGmTool.ServerCore.Game.Currency;
using System;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    // 账号金库(account_cargo_state): selection_key=容量档位, value32=存入金币。
    // 原先散在 InventoryHandler.Trade.cs 的裸SQL, 下沉为 store 方法; handler 只留解析+ACK。
    public sealed partial class SqliteInventoryStore
    {
        internal static ushort NormalizePersonalCargoListParam(ushort listParam16)
        {
            return listParam16 == 0 ? DefaultPersonalCargoCapacity : listParam16;
        }
    }
}
