using System;
using System.Runtime.InteropServices;
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
            if (Array.IndexOf(args, "--selftest-database-compatibility") >= 0)
            {
                Environment.Exit(
                    SelfTests.DatabaseCompatibilitySelfTest.Run());
                return;
            }
            if (Array.IndexOf(args, "--selftest-item-grant-options") >= 0)
            {
                Environment.Exit(SelfTests.ItemGrantOptionsSelfTest.Run());
                return;
            }
            if (Array.IndexOf(args, "--selftest-character-mutations") >= 0)
            {
                Environment.Exit(SelfTests.CharacterMutationSelfTest.Run());
                return;
            }
            if (Array.IndexOf(args, "--selftest-inventory-maintenance") >= 0)
            {
                Environment.Exit(SelfTests.InventoryMaintenanceSelfTest.Run());
                return;
            }

            if (Array.IndexOf(args, "--selftest-inventory-migration") >= 0)
            {
                Environment.Exit(SelfTests.InventoryMigrationSelfTest.Run());
                return;
            }

            GmToolHostConfig hostConfig;
            GmConfig initialConfig;
            try
            {
                hostConfig = GmToolHostConfig.LoadOrCreate();
                initialConfig = hostConfig.ResolveInitialSource(args);
            }
            catch (Exception ex)
            {
                ReportStartupFailure(ex.Message);
                return;
            }

            var runtime = new GmRuntimeEnvironment(initialConfig);
            var initialStatus = runtime.GetStatus();
            if (hostConfig.AllowRemoteAccess && !initialStatus.Configured)
            {
                ReportStartupFailure(initialStatus.Error ?? "无法加载远程模式的数据源。");
                return;
            }

            var accessControl = new GmAccessControl(hostConfig);

            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            var app = builder.Build();

            IResult WithRuntime(Func<GmService, PvfIndexService, object> operation)
            {
                return Results.Json(runtime.Execute(operation));
            }

            IResult RuntimeStatus(HttpContext context)
            {
                var authenticated = accessControl.IsAuthenticated(context);
                var status = runtime.GetStatus(!hostConfig.AllowRemoteAccess);
                return Results.Json(new
                {
                    configured = status.Configured,
                    ready = status.Ready,
                    loading = status.Loading,
                    database = status.Database,
                    pvf = status.Pvf,
                    serverBin = status.ServerBin,
                    indexReady = status.IndexReady,
                    indexError = status.IndexError,
                    error = status.Error,
                    hasError = status.HasError,
                    schemaVersion = status.SchemaVersion,
                    minimumSupportedSchemaVersion = status.MinimumSupportedSchemaVersion,
                    maximumSupportedSchemaVersion = status.MaximumSupportedSchemaVersion,
                    authenticationRequired = accessControl.RequiresAuthentication,
                    authenticated,
                    canChangeSource = !hostConfig.AllowRemoteAccess && authenticated,
                });
            }

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

            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value;
                var isPublicEndpoint = string.Equals(path, "/api/status", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, "/api/auth/login", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, "/api/auth/logout", StringComparison.OrdinalIgnoreCase);
                if (accessControl.RequiresAuthentication
                    && path != null
                    && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                    && !isPublicEndpoint
                    && !accessControl.IsAuthenticated(context))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "请先登录。",
                        loginRequired = true,
                    });
                    return;
                }

                await next(context);
            });

            app.MapGet("/api/status", RuntimeStatus);
            app.MapPost("/api/auth/login", (LoginRequest body, HttpContext context) =>
                Results.Json(accessControl.Login(context, body?.Password)));
            app.MapPost("/api/auth/logout", (HttpContext context) =>
            {
                accessControl.Logout(context);
                return Results.Json(new { success = true });
            });
            app.MapPost("/api/environment", (RuntimeEnvironmentRequest body) =>
                Results.Json(hostConfig.AllowRemoteAccess
                    ? new { success = false, error = "远程访问模式下请在 config.ini 修改数据库和 PVF 路径。" }
                    : runtime.Configure(body.DatabasePath, body.PvfPath)));

            app.MapGet("/api/inventory-migration/status", () =>
                WithRuntime((gm, _) => gm.GetInventoryMigrationStatus()));
            app.MapPost("/api/inventory-migration/legacy-to-new", () =>
                WithRuntime((gm, _) => gm.MigrateLegacyInventoryToNew()));
            app.MapPost("/api/inventory-migration/new-to-legacy", () =>
                WithRuntime((gm, _) => gm.MigrateNewInventoryToLegacy()));

            app.MapGet("/api/accounts", () => WithRuntime((gm, _) => gm.ListAccounts()));
            app.MapGet("/api/accounts/{id:int}/detail", (int id) => WithRuntime((gm, pvfIndex) => gm.GetAccountDetail(id, pvfIndex)));
            app.MapPost("/api/accounts/{id:int}/backup", (int id) =>
                WithRuntime((gm, _) => gm.ExportAccountBackup(id)));
            app.MapPost("/api/accounts/restore", (AccountBackupFile body) =>
                WithRuntime((gm, _) => gm.RestoreAccountBackup(body)));
            app.MapPost("/api/accounts/create-for-clone", (CreateAccountForCloneRequest body) =>
                WithRuntime((gm, _) => gm.CreateAccountForClone(body?.AccountName, body?.Password, body?.ConfirmPassword)));
            app.MapPost("/api/accounts/{id:int}/currency", (int id, CurrencyRequest body) =>
                WithRuntime((gm, _) => gm.AdjustAccountCurrency(id, body.Type, body.Amount, body.Value)));
            app.MapPost("/api/accounts/{id:int}/cube", (int id, CubeRequest body) =>
                WithRuntime((gm, _) => gm.AdjustCubeFragment(id, body.ItemId, body.Amount, body.Value)));
            app.MapPost("/api/accounts/{id:int}/honor-level", (int id, HonorLevelRequest body) =>
                WithRuntime((gm, _) => gm.SetAccountHonorLevel(id, body.Level)));
            app.MapPost("/api/accounts/{id:int}/honor-level/max", (int id) =>
                WithRuntime((gm, _) => gm.MaxAccountHonorLevel(id)));
            app.MapPost("/api/accounts/{id:int}/growth-capsule", (int id, GrowthCapsuleRequest body) =>
                WithRuntime((gm, _) => gm.SetGrowthCapsuleExp(id, body.Exp)));
            app.MapPost("/api/accounts/{id:int}/growth-capsule/max", (int id) =>
                WithRuntime((gm, _) => gm.MaxGrowthCapsuleExp(id)));
            app.MapPost("/api/characters/{id:int}/wallet", (int id, WalletSetRequest body) =>
                WithRuntime((gm, _) => gm.SetWalletValue(id, body.Type, body.Value)));
            app.MapPost("/api/accounts/{id:int}/cargo/delete", (int id, SlotRequest body) =>
                WithRuntime((gm, _) => gm.DeleteAccountCargoAt(id, body.Slot)));
            app.MapPost("/api/accounts/{id:int}/cargo/clear", (int id) =>
                WithRuntime((gm, _) => gm.ClearAccountCargo(id)));
            app.MapPost("/api/accounts/{id:int}/cargo/max", (int id) =>
                WithRuntime((gm, _) => gm.MaxAccountCargo(id)));
            app.MapGet("/api/characters", (int? accountId) => WithRuntime((gm, _) => gm.ListCharacters(accountId ?? -1)));
            app.MapGet("/api/characters/{id:int}", (int id) => WithRuntime((gm, _) => gm.GetCharacter(id)));
            app.MapGet("/api/characters/{id:int}/items", (int id) => WithRuntime((gm, pvfIndex) => gm.ListItems(id, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/mailbox/clear", (int id) =>
                WithRuntime((gm, _) => gm.ClearCharacterMailbox(id)));
            app.MapGet("/api/inventory-anomalies/status", () =>
                WithRuntime((gm, pvfIndex) => gm.GetInventoryAnomalyStatus(pvfIndex)));
            app.MapPost("/api/inventory-anomalies/clean", () =>
                WithRuntime((gm, pvfIndex) => gm.CleanInventoryAnomalies(pvfIndex)));
            app.MapGet("/api/characters/{id:int}/items/{templateId:int}/grant-options", (int id, int templateId) =>
                WithRuntime((gm, pvfIndex) => gm.GetItemGrantOptions(id, templateId, pvfIndex)));
            app.MapGet("/api/characters/{id:int}/items/config-options", (int id, int listType, int slot) =>
                WithRuntime((gm, pvfIndex) => gm.GetInventoryItemConfigOptions(id, listType, slot, pvfIndex)));
            app.MapGet("/api/characters/{id:int}/quests", (int id) => WithRuntime((gm, pvfIndex) => gm.ListQuests(id, pvfIndex)));
            app.MapGet("/api/characters/{id:int}/stats", (int id) => WithRuntime((gm, _) => gm.GetCharacterStats(id)));
            app.MapGet("/api/characters/{id:int}/gold-limit", (int id) => WithRuntime((gm, _) => gm.GetGoldLimitStatus(id)));
            app.MapGet("/api/characters/{id:int}/sptp", (int id) => WithRuntime((gm, _) => gm.GetSpTp(id)));
            app.MapGet("/api/characters/{id:int}/clone-plan", (int id) => WithRuntime((gm, _) => gm.GetCharacterClonePlan(id)));
            app.MapGet("/api/characters/name-available", (string name) => WithRuntime((gm, _) => gm.CheckCharacterNameAvailable(name)));

            app.MapPost("/api/characters/{id:int}/items", (int id, ItemRequest body) =>
                WithRuntime((gm, pvfIndex) => gm.GiveItem(
                    id,
                    body.TemplateId,
                    body.Count,
                    body.Options,
                    pvfIndex,
                    body.RequestId,
                    body.DeliveryMode)));
            app.MapPost("/api/characters/{id:int}/items/remove", (int id, ItemRequest body) =>
                WithRuntime((gm, _) => gm.RemoveItem(id, body.TemplateId, body.Count)));
            app.MapPost("/api/characters/{id:int}/items/delete-at", (int id, DeleteAtRequest body) =>
                WithRuntime((gm, _) => gm.DeleteItemAt(id, body.ListType, body.Slot, body.Count)));
            app.MapPost("/api/characters/{id:int}/items/batch-delete", (int id, BatchDeleteRequest body) =>
                WithRuntime((gm, _) => gm.BatchDeleteItems(id, body.Items)));
            app.MapPost("/api/characters/{id:int}/items/configure", (int id, InventoryItemConfigureRequest body) =>
                WithRuntime((gm, pvfIndex) => gm.ConfigureInventoryItem(id, body, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/gold", (int id, AmountRequest body) =>
                WithRuntime((gm, _) => gm.AdjustGold(id, body.Amount)));
            app.MapPost("/api/characters/{id:int}/cera", (int id, CeraRequest body) =>
                WithRuntime((gm, _) => gm.AdjustCera(id, body.Amount, body.Type)));
            app.MapPost("/api/characters/{id:int}/level", (int id, LevelRequest body) =>
                WithRuntime((gm, _) => gm.SetLevel(id, body.Level)));
            app.MapPost("/api/characters/{id:int}/inventory-limit/max", (int id) =>
                WithRuntime((gm, _) => gm.SetInventoryLimitTo999(id)));
            app.MapPost("/api/characters/{id:int}/inventory-limit/restore", (int id) =>
                WithRuntime((gm, _) => gm.RestoreNormalInventoryLimit(id)));
            app.MapPost("/api/characters/{id:int}/gold-limit/max", (int id) =>
                WithRuntime((gm, _) => gm.SetMaximumGoldLimit(id)));
            app.MapPost("/api/characters/{id:int}/personal-cargo/max", (int id) =>
                WithRuntime((gm, _) => gm.MaxPersonalCargo(id)));
            app.MapPost("/api/characters/{id:int}/equipment-slots/unlock", (int id) =>
                WithRuntime((gm, _) => gm.UnlockExtraEquipmentSlots(id)));
            app.MapPost("/api/characters/{id:int}/dungeon-permissions/unlock", (int id) =>
                WithRuntime((gm, pvfIndex) => gm.UnlockDungeonPermissions(id, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/delete", (int id, DeleteCharacterRequest body) =>
                WithRuntime((gm, _) => gm.DeleteCharacterPermanently(id, body?.ConfirmText)));
            app.MapPost("/api/characters/{id:int}/sp", (int id, SpRequest body) =>
                WithRuntime((gm, _) => gm.AdjustSpTpSynced(id, body.Sp, body.Tp)));
            app.MapPost("/api/characters/{id:int}/sp/zero-remaining", (int id) =>
                WithRuntime((gm, _) => gm.ZeroRemainingSpTp(id)));
            app.MapPost("/api/characters/{id:int}/clone", (int id, CharacterCloneRequest body) =>
                WithRuntime((gm, _) => gm.CloneCharacter(id, body)));
            app.MapGet("/api/characters/{id:int}/growoptions", (int id, int? job) => WithRuntime((gm, _) => gm.GetGrowOptions(id, job)));
            app.MapPost("/api/characters/{id:int}/growtype", (int id, GrowTypeRequest body) =>
                WithRuntime((gm, _) => gm.SetGrowTypeFixed(id, body.Job, body.First, body.Second)));
            app.MapPost("/api/characters/{id:int}/quests/{questId:int}/ready", (int id, int questId, string activationId) =>
                WithRuntime((gm, _) => gm.MarkQuestReady(id, questId, activationId)));
            app.MapPost("/api/characters/{id:int}/quests/{questId:int}/daily-ready", (int id, int questId) =>
                WithRuntime((gm, pvfIndex) => gm.MarkVisibleDailyQuestReady(id, questId, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/{questId:int}/complete", (int id, int questId) =>
                WithRuntime((gm, _) => gm.ForceCompleteQuest(id, questId)));
            app.MapGet("/api/characters/{id:int}/quests/cleared", (int id) =>
                WithRuntime((gm, pvfIndex) => gm.ListClearedQuests(id, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/{questId:int}/unclear", (int id, int questId) =>
                WithRuntime((gm, _) => gm.UnclearQuest(id, questId)));
            app.MapGet("/api/characters/{id:int}/quests/search", (int id, string q, string grade, string region, int? limit) =>
                WithRuntime((gm, pvfIndex) => gm.SearchQuests(id, q, grade, region, limit ?? 500, pvfIndex)));
            app.MapGet("/api/characters/{id:int}/quests/main", (int id) =>
                WithRuntime((gm, pvfIndex) => gm.MainQuestOverview(id, pvfIndex)));
            app.MapGet("/api/characters/{id:int}/quests/all-visible", (int id) =>
                WithRuntime((gm, pvfIndex) => gm.AllVisibleQuestOverview(id, pvfIndex)));
            app.MapGet("/api/characters/{id:int}/quests/achievement", (int id) =>
                WithRuntime((gm, pvfIndex) => gm.AchievementOverview(id, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/{questId:int}/complete-chain", (int id, int questId) =>
                WithRuntime((gm, pvfIndex) => gm.CompleteQuestChain(id, questId, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/complete-batch", (int id, QuestBatchRequest body) =>
                WithRuntime((gm, _) => gm.CompleteQuestBatch(id, body.QuestIds)));
            app.MapPost("/api/characters/{id:int}/quests/all-visible/complete-batch", (int id, QuestBatchRequest body) =>
                WithRuntime((gm, pvfIndex) => gm.CompleteVisibleQuestBatch(id, body.QuestIds, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/daily/reset", (int id) =>
                WithRuntime((gm, pvfIndex) => gm.ResetVisibleDailyQuests(id, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/unclear-batch", (int id, QuestBatchRequest body) =>
                WithRuntime((gm, _) => gm.UnclearQuestBatch(id, body.QuestIds)));
            app.MapPost("/api/characters/{id:int}/quests/titlebook/complete-all", (int id) =>
                WithRuntime((gm, _) => gm.CompleteAllTitleBook(id)));
            app.MapPost("/api/characters/{id:int}/quests/main/complete-current-level", (int id) =>
                WithRuntime((gm, pvfIndex) => gm.CompleteCurrentLevelMainQuests(id, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/side/complete-current-level", (int id) =>
                WithRuntime((gm, pvfIndex) => gm.CompleteCurrentLevelSideQuests(id, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/system/complete-current-level", (int id) =>
                WithRuntime((gm, pvfIndex) => gm.CompleteCurrentLevelSystemQuests(id, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/achievement-no-item/complete-current-level", (int id) =>
                WithRuntime((gm, pvfIndex) => gm.CompleteCurrentLevelNoItemAchievementQuests(id, pvfIndex)));
            app.MapPost("/api/characters/{id:int}/quests/profession/complete", (int id, ProfessionQuestRequest body) =>
                WithRuntime((gm, pvfIndex) => gm.CompleteProfessionQuests(id, pvfIndex, body.First)));
            app.MapGet("/api/characters/{id:int}/quests/equipment-slots/status", (int id) =>
                WithRuntime((gm, _) => gm.GetExtraEquipmentSlotQuestStatus(id)));
            app.MapPost("/api/characters/{id:int}/quests/equipment-slots/complete", (int id) =>
                WithRuntime((gm, _) => gm.CompleteExtraEquipmentSlotQuests(id)));

            app.MapGet("/api/items/search", (string q, int? limit) =>
                WithRuntime((_, pvfIndex) => pvfIndex.Search(q, limit ?? 30)));
            app.MapGet("/api/items/categories", () => WithRuntime((_, pvfIndex) => pvfIndex.GetItemCategories()));
            app.MapGet("/api/items/browse", (string q, string kind, string tag, string segment, string special, int? minLevel, int? maxLevel, int? rarity, int? limit, int? offset, int? usableJob, string expiration = null) =>
                WithRuntime((_, pvfIndex) => pvfIndex.SearchItems(q, kind, tag, segment, special, minLevel ?? 0, maxLevel ?? 0, rarity ?? -1, limit ?? 100, offset ?? 0, expiration, usableJob ?? -1)));

            Console.WriteLine("GM Tool 监听: " + hostConfig.ListenUrl);
            Console.WriteLine("配置文件: " + hostConfig.ConfigPath);
            if (hostConfig.AllowRemoteAccess)
                Console.WriteLine("远程模式已启用：请通过服务器 IP 和端口访问，数据库与 PVF 路径由 config.ini 锁定。");
            else
                Console.WriteLine("本地模式：未自动发现数据源时，请在页面选择数据库和 PVF。");
            Console.WriteLine("注意: 服务器运行中的改动, 在线角色需要返回选角再进入才会生效。");
            try
            {
                app.Run(hostConfig.ListenUrl);
            }
            catch (Exception ex)
            {
                ReportStartupFailure("无法监听 " + hostConfig.ListenUrl + ":\r\n" + ex.GetBaseException().Message);
            }
        }

        private static void ReportStartupFailure(string error)
        {
            Console.Error.WriteLine("GM Tool 启动失败。");
            Console.Error.WriteLine();
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine("请检查同目录 config.ini 及上述错误后重新启动。");
            WaitForKeyWhenLaunchedDirectly();
            Environment.ExitCode = 1;
        }

        private static void WaitForKeyWhenLaunchedDirectly()
        {
            if (!OperatingSystem.IsWindows()
                || Console.IsInputRedirected
                || Console.IsOutputRedirected)
                return;

            try
            {
                var processIds = new uint[2];
                if (GetConsoleProcessList(processIds, (uint)processIds.Length) != 1)
                    return;

                Console.Error.WriteLine();
                Console.Error.WriteLine("按任意键关闭此窗口...");
                Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                // A detached process may not have an interactive console to wait on.
            }
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
    }

    public sealed class ItemRequest
    {
        public int TemplateId { get; set; }
        public int Count { get; set; }
        public ServerCore.Game.Inventory.ItemGrantOptions Options { get; set; }
        public string RequestId { get; set; }
        public string DeliveryMode { get; set; }
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

    public sealed class RuntimeEnvironmentRequest
    {
        public string DatabasePath { get; set; }
        public string PvfPath { get; set; }
    }

    public sealed class LoginRequest
    {
        public string Password { get; set; }
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
        public int? Job { get; set; }
        public int First { get; set; }
        public int Second { get; set; }
    }

    public sealed class ProfessionQuestRequest
    {
        public int? First { get; set; }
    }

    public sealed class DeleteCharacterRequest
    {
        public string ConfirmText { get; set; }
    }

    public sealed class CreateAccountForCloneRequest
    {
        public string AccountName { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
    }
}
