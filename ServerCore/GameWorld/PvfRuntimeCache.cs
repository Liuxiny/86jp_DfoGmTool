using DfoGmTool.ServerCore.Game.Characters;
using DfoGmTool.ServerCore.Game.Dungeon;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.SelectCharacter;
using DfoGmTool.ServerCore.Game.Skills;

namespace DfoGmTool.ServerCore.GameWorld
{
    // Every static value here is parsed from PVF and must not outlive a source switch.
    internal static class PvfRuntimeCache
    {
        internal static void ResetForPvfChange()
        {
            CharacterStatComputer.ResetForPvfChange();
            ExpTableProvider.ResetForPvfChange();
            InitialCharacterSkills.ResetForPvfChange();
            ItemMetadataResolver.ResetForPvfChange();
            AmplifyInitialValueResolver.ResetForPvfChange();
            AvatarAbilityDataProvider.ResetForPvfChange();
            AvatarDurationResolver.ResetForPvfChange();
            CreatureExtraResolver.ResetForPvfChange();
            RentalWeaponInventoryMapper.ResetForPvfChange();
            SkillDataProvider.ResetForPvfChange();
            SpTableProvider.ResetForPvfChange();
            SqliteInventoryStore.ResetForPvfChange();
        }

        internal static void WarmForPvfChange()
        {
            SkillDataProvider.WarmUp();
            AvatarAbilityDataProvider.WarmUp();
        }
    }
}
