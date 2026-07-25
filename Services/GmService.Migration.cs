namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        public object GetInventoryMigrationStatus()
            => _inventoryMigration.GetStatus();

        public object MigrateLegacyInventoryToNew()
            => _inventoryMigration.MigrateLegacyToNew();

        public object MigrateNewInventoryToLegacy()
            => _inventoryMigration.MigrateNewToLegacy();
    }
}
