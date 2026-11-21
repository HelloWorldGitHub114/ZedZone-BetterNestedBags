using System;
using HarmonyLib;

namespace ZedZone.BetterNestedBags
{
    /// <summary>
    /// 快捷栏套包物品使用补丁：
    /// 修复放入套包内的药品、食物、熟食、饮品、投掷物与武器绑定到快捷栏后，
    /// 无法直接按键使用、熟食数量统计遗漏以及套包变动后快捷栏刷新延迟的问题。
    /// </summary>
    internal static class HotbarNestedHelper
    {
        internal static bool IsEnabled =>
            Plugin.EnableNestedHotbar != null && Plugin.EnableNestedHotbar.Value;

        internal static Il2CppSystem.Collections.Generic.List<ItemData> CollectAllById(CharacterData cd, int itemId)
        {
            if (cd == null || itemId < 0) return null;
            var list = new Il2CppSystem.Collections.Generic.List<ItemData>();
            var invs = NestedBagCore.CollectAllCarriedInventories(cd);
            for (int i = 0; i < invs.Count; i++)
            {
                var items = invs[i]?.itemList;
                if (items == null) continue;
                for (int j = 0; j < items.Count; j++)
                {
                    var it = items[j];
                    if (it != null && it.itemId == itemId)
                        list.Add(it);
                }
            }
            return list;
        }
    }

    /// <summary>
    /// 当表层背包与口袋找不到快捷栏对应的消耗品/工具时，递归随身套包按烹饪状态与新鲜度挑选可用物品。
    /// </summary>
    [HarmonyPatch(typeof(ShortcutManager), nameof(ShortcutManager.ResolveFreshestItem),
        typeof(int), typeof(int))]
    internal static class Patch_ShortcutManager_ResolveFreshestItem
    {
        [HarmonyPostfix]
        private static void Postfix(ShortcutManager __instance, int itemId, int cookState, ref ItemData __result)
        {
            if (__result != null || !HotbarNestedHelper.IsEnabled) return;
            if (__instance == null || itemId < 0) return;

            try
            {
                var cd = NestedBagCore.LocalPlayer?.characterData;
                if (cd == null) return;

                var carried = HotbarNestedHelper.CollectAllById(cd, itemId);
                if (carried == null || carried.Count == 0) return;

                var filtered = cookState >= 0
                    ? __instance.FilterByCookState(carried, cookState)
                    : carried;
                if (filtered == null || filtered.Count == 0) return;

                __result = __instance.GetFreshestItem(filtered);
                if (__result != null)
                    Plugin.LogVerbose($"Hotbar resolved nested itemId={itemId} cookState={cookState}");
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("hotbar_resolve", ex);
            }
        }
    }

    /// <summary>
    /// 补齐快捷栏上可烹饪食物（cookState >= 0）及其他物品的随身套包总数统计。
    /// </summary>
    [HarmonyPatch(typeof(ShortcutManager), nameof(ShortcutManager.GetItemNumberByIdAndCookState),
        typeof(int), typeof(int))]
    internal static class Patch_ShortcutManager_GetItemNumberByIdAndCookState
    {
        [HarmonyPostfix]
        private static void Postfix(int itemId, int cookState, ref int __result)
        {
            if (!HotbarNestedHelper.IsEnabled || itemId < 0) return;

            try
            {
                var cd = NestedBagCore.LocalPlayer?.characterData;
                if (cd == null) return;

                var carried = HotbarNestedHelper.CollectAllById(cd, itemId);
                if (carried == null || carried.Count == 0) return;

                var mgr = ItemManager.instance;
                float total = 0f;
                for (int i = 0; i < carried.Count; i++)
                {
                    var it = carried[i];
                    if (it == null) continue;
                    if (cookState >= 0 && mgr != null && mgr.GetCookStateBucket(it) != cookState)
                        continue;
                    total += it.itemNumberFloat;
                }

                int count = (int)Math.Floor(total + 1e-4f);
                if (count > __result)
                    __result = count;
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("hotbar_count", ex);
            }
        }
    }

    /// <summary>
    /// 支持通过普通物品快捷槽直接切换装备放在套包或衣服口袋中的近战/远程武器。
    /// </summary>
    [HarmonyPatch(typeof(ShortcutManager), nameof(ShortcutManager.SelectShortcut), typeof(ShortcutData))]
    internal static class Patch_ShortcutManager_SelectShortcut_Weapon
    {
        [HarmonyPrefix]
        private static bool Prefix(ShortcutManager __instance, ShortcutData m_shotcutData)
        {
            if (!HotbarNestedHelper.IsEnabled || __instance == null || m_shotcutData == null) return true;

            try
            {
                var player = NestedBagCore.LocalPlayer;
                var cd = player?.characterData;
                if (player == null || cd == null || cd.inventoryData == null) return true;

                // 仅处理普通物品快捷槽（跳过主武器槽 0..2，且 ActionType 为普通物品 0）
                if (m_shotcutData.shotcutIndex >= 0 && m_shotcutData.shotcutIndex < 3) return true;
                if ((int)m_shotcutData.actionType != 0) return true;

                var item = m_shotcutData.itemData;
                if (item == null) return true;

                var attr = NestedBagCore.GetAttr(item.itemId);
                if (attr == null) return true;
                if (attr.itemType != ItemType.MeleeWeapon && attr.itemType != ItemType.RangedWeapon)
                    return true;

                // 已在主背包内则沿用原版流程
                var mainList = cd.inventoryData.itemList;
                if (mainList != null && mainList.Contains(item))
                    return true;

                // 存在于随身套包或衣服口袋内时直接装备
                var ownerInv = NestedBagCore.FindOwnerInAllCarried(cd, item);
                if (ownerInv == null) return true;

                if (!ShortcutManager.TryReleaseLiftingBlueprint())
                    return false;

                __instance.selectedShortcutData = m_shotcutData;
                if (attr.itemType == ItemType.MeleeWeapon)
                    player.EquipMeleeWeapon(item, true);
                else
                    player.EquipRangedWeapon(item, true);

                Plugin.LogVerbose($"Hotbar equipped nested weapon itemId={item.itemId}");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("hotbar_weapon", ex);
                return true;
            }
        }
    }

    /// <summary>
    /// 补全手持物品在角色随身容器中的持有检查，确保快捷栏掏出的套包内绷带、食物、饮料、投掷物与部署物正常生效。
    /// </summary>
    [HarmonyPatch(typeof(HumanCharacterController), nameof(HumanCharacterController.IsItemOnCharacterInventories),
        typeof(ItemData))]
    internal static class Patch_HumanCharacterController_IsItemOnCharacterInventories
    {
        [HarmonyPostfix]
        private static void Postfix(HumanCharacterController __instance, ItemData itemData, ref bool __result)
        {
            if (__result || !HotbarNestedHelper.IsEnabled) return;
            if (__instance == null || itemData == null || !NestedBagCore.IsLocalPlayer(__instance)) return;

            try
            {
                var cd = __instance.characterData;
                if (cd != null && NestedBagCore.FindOwnerInAllCarried(cd, itemData) != null)
                    __result = true;
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("hotbar_is_on_char", ex);
            }
        }
    }

    /// <summary>
    /// 快捷栏刷新容器订阅时一并监听所有随身嵌套子背包，移动或消耗套包物品时快捷栏角标即时更新。
    /// </summary>
    [HarmonyPatch(typeof(ShortcutPanel), nameof(ShortcutPanel.RefreshInventorySubscriptions),
        typeof(HumanCharacterController))]
    internal static class Patch_ShortcutPanel_RefreshInventorySubscriptions
    {
        [HarmonyPostfix]
        private static void Postfix(ShortcutPanel __instance, HumanCharacterController player)
        {
            if (!HotbarNestedHelper.IsEnabled || __instance == null || player == null) return;
            if (!NestedBagCore.IsLocalPlayer(player)) return;

            try
            {
                var cd = player.characterData;
                if (cd == null) return;

                var nested = NestedBagCore.CollectExtraNestedInventories(cd);
                for (int i = 0; i < nested.Count; i++)
                    __instance.SubscribeInventory(nested[i]);
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("hotbar_sub", ex);
            }
        }
    }
}
