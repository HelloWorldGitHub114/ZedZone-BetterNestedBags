using System;
using HarmonyLib;

namespace ZedZone.BetterNestedBags
{
    /// <summary>
    /// 角色底层查询与扣料递归补丁：
    /// 统一补全原版 BasicCharacterController 只扫描表层三处容器（主背包与服装口袋）的限制，
    /// 使建造、体素建造、维修锤、换弹装箭、医疗包扎、右键工具与任务检查全面识别多层套包。
    /// </summary>
    internal static class CharacterRecursionGuard
    {
        [ThreadStatic]
        internal static int RemoveItemsDepth;

        [ThreadStatic]
        internal static int AddItemSyncDepth;

        internal static bool IsEnabled =>
            Plugin.EnableCharacterRecursion != null && Plugin.EnableCharacterRecursion.Value;
    }

    /// <summary>
    /// 统计角色持有某物品的总数量（直接惠及 HasItem、建造校验、体素建造 VoxelWallet、维修锤 CanAfford 与任务统计）。
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.GetTotalItemNumberByID),
        typeof(int))]
    internal static class Patch_BasicCharacterController_GetTotalItemNumberByID
    {
        [HarmonyPostfix]
        private static void Postfix(BasicCharacterController __instance, int m_itemID, ref float __result)
        {
            if (!CharacterRecursionGuard.IsEnabled || __instance == null || m_itemID < 0) return;

            try
            {
                var cd = __instance.characterData;
                if (cd == null) return;

                var nested = NestedBagCore.CollectExtraNestedInventories(cd);
                for (int i = 0; i < nested.Count; i++)
                    __result += nested[i].GetTotalItemNumberByID(m_itemID);
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("char_total_by_id", ex);
            }
        }
    }

    /// <summary>
    /// 按物品 ID 收集角色所有匹配物品（直接惠及散弹/子弹换弹 GetAmmosInInventory、HUD 弹药总数、投掷与连续部署）。
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.GetAllItemListById),
        typeof(CharacterData), typeof(int))]
    internal static class Patch_BasicCharacterController_GetAllItemListById_Return
    {
        [HarmonyPostfix]
        private static void Postfix(
            CharacterData m_characterData,
            int m_itemID,
            Il2CppSystem.Collections.Generic.List<ItemData> __result)
        {
            if (!CharacterRecursionGuard.IsEnabled || m_characterData == null || __result == null || m_itemID < 0)
                return;

            try
            {
                var nested = NestedBagCore.CollectExtraNestedInventories(m_characterData);
                if (nested.Count == 0) return;

                var seen = NestedBagCore.BuildItemPtrSet(__result);
                for (int i = 0; i < nested.Count; i++)
                    NestedBagCore.AppendUniqueItems(__result, nested[i].GetItemListById(m_itemID), seen);
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("char_all_by_id_ret", ex);
            }
        }
    }

    /// <summary>
    /// 按物品 ID 收集到已有列表的重载版本。
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.GetAllItemListById),
        typeof(CharacterData), typeof(int), typeof(Il2CppSystem.Collections.Generic.List<ItemData>))]
    internal static class Patch_BasicCharacterController_GetAllItemListById_OutList
    {
        [HarmonyPostfix]
        private static void Postfix(
            CharacterData m_characterData,
            int m_itemID,
            Il2CppSystem.Collections.Generic.List<ItemData> results)
        {
            if (!CharacterRecursionGuard.IsEnabled || m_characterData == null || results == null || m_itemID < 0)
                return;

            try
            {
                var nested = NestedBagCore.CollectExtraNestedInventories(m_characterData);
                if (nested.Count == 0) return;

                var seen = NestedBagCore.BuildItemPtrSet(results);
                for (int i = 0; i < nested.Count; i++)
                    NestedBagCore.AppendUniqueItems(results, nested[i].GetItemListById(m_itemID), seen);
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("char_all_by_id_out", ex);
            }
        }
    }

    /// <summary>
    /// 按 ItemType 收集角色物品（直接惠及医疗包扎 AidTreatUtil、弓箭/弩箭换弹与右键按类型选工具）。
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.GetAllItemListByType),
        typeof(CharacterData), typeof(ItemType))]
    internal static class Patch_BasicCharacterController_GetAllItemListByType
    {
        [HarmonyPostfix]
        private static void Postfix(
            CharacterData m_characterData,
            ItemType m_itemType,
            Il2CppSystem.Collections.Generic.List<ItemData> __result)
        {
            if (!CharacterRecursionGuard.IsEnabled || m_characterData == null || __result == null) return;

            try
            {
                var nested = NestedBagCore.CollectExtraNestedInventories(m_characterData);
                if (nested.Count == 0) return;

                var seen = NestedBagCore.BuildItemPtrSet(__result);
                for (int i = 0; i < nested.Count; i++)
                    NestedBagCore.AppendUniqueItems(__result, nested[i].GetItemListByType(m_itemType), seen);
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("char_all_by_type", ex);
            }
        }
    }

    /// <summary>
    /// 按功能标签 ItemFeatureType 静态收集角色物品。
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.GetAllItemListByItemFeature),
        typeof(CharacterData), typeof(ItemFeatureType))]
    internal static class Patch_BasicCharacterController_GetAllItemListByItemFeature
    {
        [HarmonyPostfix]
        private static void Postfix(
            CharacterData m_characterData,
            ItemFeatureType itemFeatureType,
            Il2CppSystem.Collections.Generic.List<ItemData> __result)
        {
            if (!CharacterRecursionGuard.IsEnabled || m_characterData == null || __result == null) return;

            try
            {
                var nested = NestedBagCore.CollectExtraNestedInventories(m_characterData);
                if (nested.Count == 0) return;

                var seen = NestedBagCore.BuildItemPtrSet(__result);
                for (int i = 0; i < nested.Count; i++)
                    NestedBagCore.AppendUniqueItems(__result, nested[i].GetItemListByFeature(itemFeatureType), seen);
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("char_all_by_feature", ex);
            }
        }
    }

    /// <summary>
    /// 按功能标签实例收集角色物品（直接惠及右键开罐器、打火机、缝纫工具、撬锁/破门工具与拆解工具选择）。
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.GetItemListByFeature),
        typeof(ItemFeatureType))]
    internal static class Patch_BasicCharacterController_GetItemListByFeature
    {
        [HarmonyPostfix]
        private static void Postfix(
            BasicCharacterController __instance,
            ItemFeatureType m_itemFeatureType,
            Il2CppSystem.Collections.Generic.List<ItemData> __result)
        {
            if (!CharacterRecursionGuard.IsEnabled || __instance == null || __result == null) return;

            try
            {
                var cd = __instance.characterData;
                if (cd == null) return;

                var nested = NestedBagCore.CollectExtraNestedInventories(cd);
                if (nested.Count == 0) return;

                var seen = NestedBagCore.BuildItemPtrSet(__result);
                for (int i = 0; i < nested.Count; i++)
                    NestedBagCore.AppendUniqueItems(__result, nested[i].GetItemListByFeature(m_itemFeatureType), seen);
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("char_inst_by_feature", ex);
            }
        }
    }

    /// <summary>
    /// 定位物品所属容器时回退搜索套包子包（直接惠及 ConsumeOneItemFromCharacter、食物消耗与连续部署）。
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.FindOwnerInventoryData),
        typeof(CharacterData), typeof(ItemData))]
    internal static class Patch_BasicCharacterController_FindOwnerInventoryData
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(CharacterData characterData, ItemData m_itemData, ref InventoryData __result)
        {
            if (__result != null || !CharacterRecursionGuard.IsEnabled) return;
            if (characterData == null || m_itemData == null) return;

            try
            {
                __result = NestedBagCore.FindOwnerInExtraNested(characterData, m_itemData);
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("char_find_owner", ex);
            }
        }
    }

    /// <summary>
    /// 将随身嵌套子背包纳入角色自有容器判定，确保右键交互、使用、装备与消费逻辑视其为随身容器。
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.IsCharacterOwnedInventory),
        typeof(CharacterData), typeof(InventoryData))]
    internal static class Patch_BasicCharacterController_IsCharacterOwnedInventory
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(CharacterData m_characterData, InventoryData m_inventoryData, ref bool __result)
        {
            if (__result || !CharacterRecursionGuard.IsEnabled) return;
            if (m_characterData == null || m_inventoryData == null) return;

            try
            {
                if (NestedBagCore.IsOwnedByCharacter(m_characterData, m_inventoryData, out _))
                    __result = true;
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("char_is_owned_inv", ex);
            }
        }
    }

    /// <summary>
    /// 从角色身上移除指定物品实例（静态重载），表层未命中时直接定位随身嵌套子包移除并刷新总负重。
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.RemoveItemFromCharacter),
        typeof(CharacterData), typeof(ItemData), typeof(bool))]
    internal static class Patch_BasicCharacterController_RemoveItemFromCharacter_Static
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(
            CharacterData characterData,
            ItemData m_itemData,
            bool m_executeOnChange,
            ref bool __result)
        {
            if (__result || !CharacterRecursionGuard.IsEnabled) return;
            if (characterData == null || m_itemData == null) return;

            try
            {
                if (NestedBagCore.TryRemoveFromExtraNested(characterData, m_itemData, m_executeOnChange))
                    __result = true;
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("char_remove_static", ex);
            }
        }
    }

    /// <summary>
    /// 从角色身上移除指定物品实例（实例重载），表层未命中时直接定位随身嵌套子包移除并刷新总负重。
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.RemoveItemFromCharacter),
        typeof(ItemData))]
    internal static class Patch_BasicCharacterController_RemoveItemFromCharacter_Instance
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(BasicCharacterController __instance, ItemData itemData, ref bool __result)
        {
            if (__result || !CharacterRecursionGuard.IsEnabled) return;
            if (__instance == null || itemData == null) return;

            try
            {
                var cd = __instance.characterData;
                if (cd != null && NestedBagCore.TryRemoveFromExtraNested(cd, itemData, true))
                    __result = true;
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("char_remove_instance", ex);
            }
        }
    }

    /// <summary>
    /// 按物品 ID 与数量批量扣料（覆盖建造、体素建造、维修与任务消耗）：
    /// 采用低优先级增量差额补扣设计——先计算原版及其他模组在 __result 中已扣除的总量，
    /// 仅当仍有剩余欠量时才从嵌套子包精准补扣差额，从根源上杜绝与其他建造/工具模组冲突导致的双倍扣料。
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.RemoveItemsFromCharacter),
        typeof(int), typeof(float))]
    internal static class Patch_BasicCharacterController_RemoveItemsFromCharacter
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(
            BasicCharacterController __instance,
            int itemId,
            float itemNumber,
            ref Il2CppSystem.Collections.Generic.List<ItemData> __result)
        {
            if (!CharacterRecursionGuard.IsEnabled || __instance == null || itemId < 0 || itemNumber <= 0f)
                return;
            if (CharacterRecursionGuard.RemoveItemsDepth > 0)
                return;

            CharacterRecursionGuard.RemoveItemsDepth++;
            try
            {
                var cd = __instance.characterData;
                if (cd == null) return;

                __result ??= new Il2CppSystem.Collections.Generic.List<ItemData>();

                float alreadyRemoved = 0f;
                for (int i = 0; i < __result.Count; i++)
                {
                    var removed = __result[i];
                    if (removed != null)
                        alreadyRemoved += removed.itemNumberFloat;
                }

                float remaining = itemNumber - alreadyRemoved;
                if (remaining <= 1e-4f)
                    return;

                var nested = NestedBagCore.CollectExtraNestedInventories(cd);
                if (nested.Count == 0)
                    return;

                bool touchedNested = false;
                for (int i = 0; i < nested.Count && remaining > 1e-4f; i++)
                {
                    var subInv = nested[i];
                    if (subInv == null) continue;

                    float removedHere = __instance.RemoveItemsFromInventory(subInv, itemId, remaining, __result);
                    if (removedHere > 1e-4f)
                    {
                        touchedNested = true;
                        remaining -= removedHere;
                    }
                }

                if (touchedNested)
                {
                    NestedBagCore.RefreshCarriedWeights(cd);
                    Plugin.LogVerbose($"RemoveItemsFromCharacter supplemented nested itemId={itemId}, req={itemNumber:0.##}, surface={alreadyRemoved:0.##}, left={Math.Max(0f, remaining):0.##}");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("char_remove_items", ex);
            }
            finally
            {
                CharacterRecursionGuard.RemoveItemsDepth--;
            }
        }
    }

    /// <summary>
    /// 物品直接添加进随身嵌套子包时的联动同步：
    /// 1. 原版 InventoryData.AddItem 仅在 isPlayerInventory 为真时通知任务系统；若放入随身嵌套子包，主动补发任务进度通知。
    /// 2. 若新放入随身容器的物品本身带有子背包，立即刷新快捷栏容器监听订阅。
    /// </summary>
    [HarmonyPatch(typeof(InventoryData), nameof(InventoryData.AddItem),
        typeof(ItemData), typeof(bool))]
    internal static class Patch_InventoryData_AddItem_NestedSync
    {
        [HarmonyPostfix]
        private static void Postfix(
            InventoryData __instance,
            ItemData m_itemData,
            bool executeOnChangeAction,
            int __result)
        {
            if (__result != 0 || !executeOnChangeAction || __instance == null || m_itemData == null)
                return;
            if (CharacterRecursionGuard.AddItemSyncDepth > 0)
                return;

            CharacterRecursionGuard.AddItemSyncDepth++;
            try
            {
                var player = NestedBagCore.LocalPlayer;
                var cd = player?.characterData;
                if (player == null || cd == null) return;

                if (!NestedBagCore.IsOwnedByCharacter(cd, __instance, out bool isNestedOnly))
                    return;

                // 嵌套子包常未标记 isPlayerInventory，此处补触发收集任务检查
                if (isNestedOnly &&
                    !__instance.isPlayerInventory &&
                    Plugin.EnableNestedMissionSync != null &&
                    Plugin.EnableNestedMissionSync.Value)
                {
                    MissionManager.instance?.OnPlayerInventoryAddItem(m_itemData);
                }

                // 新塞入随身容器的背包类物品立即纳入快捷栏实时监听
                if (m_itemData.inventoryData != null &&
                    Plugin.EnableNestedHotbar != null &&
                    Plugin.EnableNestedHotbar.Value)
                {
                    var sp = ShortcutPanel.instance;
                    if (sp != null)
                        sp.RefreshInventorySubscriptions(player);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("inv_add_nested_sync", ex);
            }
            finally
            {
                CharacterRecursionGuard.AddItemSyncDepth--;
            }
        }
    }
}
