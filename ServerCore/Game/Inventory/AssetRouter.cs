using DfoGmTool.ServerCore.Game.Currency;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public enum AssetStorage
    {
        CharacterItem,
        AccountCubeFragment,
    }

    public static class AssetRouter
    {
        public static AssetStorage Classify(int itemTemplateId)
        {
            if (CurrencyService.IsCubeFragment(itemTemplateId))
                return AssetStorage.AccountCubeFragment;
            return AssetStorage.CharacterItem;
        }
    }
}
