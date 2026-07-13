using System;
using DfoGmTool.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DfoGmTool
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var config = GmConfig.Resolve(args);

            // 服务端程序集内部按这两个环境变量定位 PVF 和数据库
            Environment.SetEnvironmentVariable("PVF_ARCHIVE_PATH", config.PvfPath);
            Environment.SetEnvironmentVariable("INVENTORY_DATABASE_PATH", config.DatabasePath);

            var pvfIndex = new PvfIndexService(config.PvfPath);
            pvfIndex.WarmInBackground();
            var gm = new GmService(config, pvfIndex);

            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            var app = builder.Build();

            // 本地工具: 异常直接以 JSON 返回, 方便定位
            app.Use(async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch (Exception ex)
                {
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = ex.GetBaseException().Message,
                        where = ex.GetBaseException().StackTrace?.Split('\n')[0]?.Trim(),
                    });
                }
            });

            app.UseDefaultFiles();
            // 本地工具禁用静态文件缓存, 避免改了前端浏览器还跑旧脚本
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                    ctx.Context.Response.Headers["Pragma"] = "no-cache";
                    ctx.Context.Response.Headers["Expires"] = "0";
                },
            });

            app.MapGet("/api/status", () => Results.Json(new
            {
                serverBin = config.ServerBinDir,
                database = config.DatabasePath,
                pvf = config.PvfPath,
                indexReady = pvfIndex.IsReady,
                indexError = pvfIndex.BuildError,
            }));

            app.MapGet("/api/accounts", () => Results.Json(gm.ListAccounts()));
            app.MapGet("/api/accounts/{id:int}/detail", (int id) => Results.Json(gm.GetAccountDetail(id, pvfIndex)));
            app.MapPost("/api/accounts/{id:int}/currency", (int id, CurrencyRequest body) =>
                Results.Json(gm.AdjustAccountCurrency(id, body.Type, body.Amount, body.Value)));
            app.MapPost("/api/accounts/{id:int}/cube", (int id, CubeRequest body) =>
                Results.Json(gm.AdjustCubeFragment(id, body.ItemId, body.Amount, body.Value)));
            app.MapPost("/api/accounts/{id:int}/honor-level", (int id, HonorLevelRequest body) =>
                Results.Json(gm.SetAccountHonorLevel(id, body.Level)));
            app.MapPost("/api/accounts/{id:int}/honor-level/max", (int id) =>
                Results.Json(gm.MaxAccountHonorLevel(id)));
            app.MapPost("/api/accounts/{id:int}/growth-capsule", (int id, GrowthCapsuleRequest body) =>
                Results.Json(gm.SetGrowthCapsuleExp(id, body.Exp)));
            app.MapPost("/api/accounts/{id:int}/growth-capsule/max", (int id) =>
                Results.Json(gm.MaxGrowthCapsuleExp(id)));
            app.MapPost("/api/characters/{id:int}/wallet", (int id, WalletSetRequest body) =>
                Results.Json(gm.SetWalletValue(id, body.Type, body.Value)));
            app.MapPost("/api/accounts/{id:int}/cargo/delete", (int id, SlotRequest body) =>
                Results.Json(gm.DeleteAccountCargoAt(id, body.Slot)));
            app.MapPost("/api/accounts/{id:int}/cargo/clear", (int id) =>
                Results.Json(gm.ClearAccountCargo(id)));
            app.MapGet("/api/characters", (int? accountId) => Results.Json(gm.ListCharacters(accountId ?? -1)));
            app.MapGet("/api/characters/{id:int}", (int id) => Results.Json(gm.GetCharacter(id)));
            app.MapGet("/api/characters/{id:int}/items", (int id) => Results.Json(gm.ListItems(id, pvfIndex)));
            app.MapGet("/api/characters/{id:int}/quests", (int id) => Results.Json(gm.ListQuests(id, pvfIndex)));
            app.MapGet("/api/characters/{id:int}/stats", (int id) => Results.Json(gm.GetCharacterStats(id)));
            app.MapGet("/api/characters/{id:int}/sptp", (int id) => Results.Json(gm.GetSpTp(id)));

            app.MapPost("/api/characters/{id:int}/items", (int id, ItemRequest body) =>
                Results.Json(gm.GiveItem(id, body.TemplateId, body.Count, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/items/remove", (int id, ItemRequest body) =>
                Results.Json(gm.RemoveItem(id, body.TemplateId, body.Count)));
            app.MapPost("/api/characters/{id:int}/items/delete-at", (int id, DeleteAtRequest body) =>
                Results.Json(gm.DeleteItemAt(id, body.ListType, body.Slot, body.Count)));
            app.MapPost("/api/characters/{id:int}/items/batch-delete", (int id, BatchDeleteRequest body) =>
                Results.Json(gm.BatchDeleteItems(id, body.Items)));
            app.MapPost("/api/characters/{id:int}/gold", (int id, AmountRequest body) =>
                Results.Json(gm.AdjustGold(id, body.Amount)));
            app.MapPost("/api/characters/{id:int}/cera", (int id, CeraRequest body) =>
                Results.Json(gm.AdjustCera(id, body.Amount, body.Type)));
            app.MapPost("/api/characters/{id:int}/level", (int id, LevelRequest body) =>
                Results.Json(gm.SetLevel(id, body.Level)));
            app.MapPost("/api/characters/{id:int}/sp", (int id, SpRequest body) =>
                Results.Json(gm.AdjustSpTp(id, body.Sp, body.Tp)));
            app.MapGet("/api/characters/{id:int}/growoptions", (int id) => Results.Json(gm.GetGrowOptions(id)));
            app.MapPost("/api/characters/{id:int}/growtype", (int id, GrowTypeRequest body) =>
                Results.Json(gm.SetGrowType(id, body.First, body.Second)));
            app.MapPost("/api/characters/{id:int}/quests/{questId:int}/ready", (int id, int questId) =>
                Results.Json(gm.MarkQuestReady(id, questId)));
            app.MapPost("/api/characters/{id:int}/quests/{questId:int}/complete", (int id, int questId) =>
                Results.Json(gm.ForceCompleteQuest(id, questId)));
            app.MapGet("/api/characters/{id:int}/quests/cleared", (int id) =>
                Results.Json(gm.ListClearedQuests(id, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/{questId:int}/unclear", (int id, int questId) =>
                Results.Json(gm.UnclearQuest(id, questId)));
            app.MapGet("/api/characters/{id:int}/quests/search", (int id, string q, int? limit) =>
                Results.Json(gm.SearchQuests(id, q, limit ?? 30, pvfIndex)));
            app.MapGet("/api/characters/{id:int}/quests/main", (int id) =>
                Results.Json(gm.MainQuestOverview(id, pvfIndex)));
            app.MapGet("/api/characters/{id:int}/quests/achievement", (int id) =>
                Results.Json(gm.AchievementOverview(id, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/{questId:int}/complete-chain", (int id, int questId) =>
                Results.Json(gm.CompleteQuestChain(id, questId, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/complete-batch", (int id, QuestBatchRequest body) =>
                Results.Json(gm.CompleteQuestBatch(id, body.QuestIds)));

            app.MapGet("/api/items/search", (string q, int? limit) =>
                Results.Json(pvfIndex.Search(q, limit ?? 30)));
            app.MapGet("/api/items/categories", () => Results.Json(pvfIndex.GetItemCategories()));
            app.MapGet("/api/items/browse", (string q, string kind, string tag, string segment, string special, int? minLevel, int? maxLevel, int? rarity, int? limit, int? offset, string expiration = null) =>
                Results.Json(pvfIndex.SearchItems(q, kind, tag, segment, special, minLevel ?? 0, maxLevel ?? 0, rarity ?? -1, limit ?? 100, offset ?? 0, expiration)));

            Console.WriteLine("GM Tool: http://localhost:5050");
            Console.WriteLine("服务端目录: " + config.ServerBinDir);
            Console.WriteLine("注意: 服务器运行中做的改动, 在线角色需要返回选角再进入才会生效。");
            app.Run("http://localhost:5050");
        }
    }

    public sealed class ItemRequest
    {
        public int TemplateId { get; set; }
        public int Count { get; set; }
    }

    public sealed class AmountRequest
    {
        public int Amount { get; set; }
    }

    public sealed class CeraRequest
    {
        public int Amount { get; set; }
        public string Type { get; set; }
    }

    public sealed class CurrencyRequest
    {
        public string Type { get; set; }
        public int Amount { get; set; }
        public long? Value { get; set; }
    }

    public sealed class CubeRequest
    {
        public int ItemId { get; set; }
        public int Amount { get; set; }
        public long? Value { get; set; }
    }

    public sealed class HonorLevelRequest
    {
        public int Level { get; set; }
    }

    public sealed class GrowthCapsuleRequest
    {
        public long Exp { get; set; }
    }

    public sealed class WalletSetRequest
    {
        public string Type { get; set; }
        public int Value { get; set; }
    }

    public sealed class SlotRequest
    {
        public int Slot { get; set; }
    }

    public sealed class DeleteAtRequest
    {
        public int ListType { get; set; }
        public int Slot { get; set; }
        public int Count { get; set; }
    }

    public sealed class BatchDeleteRequest
    {
        public System.Collections.Generic.List<Services.BatchDeleteEntry> Items { get; set; }
    }

    public sealed class QuestBatchRequest
    {
        public System.Collections.Generic.List<int> QuestIds { get; set; }
    }

    public sealed class LevelRequest
    {
        public int Level { get; set; }
    }

    public sealed class SpRequest
    {
        public int Sp { get; set; }
        public int Tp { get; set; }
    }

    public sealed class GrowTypeRequest
    {
        public int First { get; set; }
        public int Second { get; set; }
    }
}
