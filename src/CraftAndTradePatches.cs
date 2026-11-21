using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ZedZone.BetterNestedBags
{
    /// <summary>
    /// 制作合成与商人交易套包补丁：
    /// 1. 工作台/熔炉/便携合成识别玩家套包与附近箱子内套包的原料，并精确维护 localSourceCount 分界线。
    /// 2. 取消制作时支持向嵌套子包返还工具耐久。
    /// 3. 商人买卖、货币找零与批量售卖全面识别套包内物品。
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_CraftPanel_GetCraftSourceInventories
    {
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(CraftPanel),
                nameof(CraftPanel.GetCraftSourceInventories),
                new[] { typeof(int).MakeByRefType() });
        }

        [HarmonyPostfix]
        private static void Postfix(
            ref int localSourceCount,
            Il2CppSystem.Collections.Generic.List<InventoryData> __result)
        {
            if (Plugin.EnableNestedCrafting == null || !Plugin.EnableNestedCrafting.Value) return;
            if (__result == null || __result.Count == 0) return;

            try
            {
                int origCount = __result.Count;
                int clampedLocal = Math.Clamp(localSourceCount, 0, origCount);

                var visited = new HashSet<IntPtr>();
                var localSeeds = new List<InventoryData>(clampedLocal);
                var nearbySeeds = new List<InventoryData>(Math.Max(0, origCount - clampedLocal));

                for (int i = 0; i < origCount; i++)
                {
                    var inv = __result[i];
                    if (inv == null) continue;
                    IntPtr p = NestedBagCore.Ptr(inv);
                    if (p != IntPtr.Zero)
                        visited.Add(p);

                    if (i < clampedLocal)
                        localSeeds.Add(inv);
                    else
                        nearbySeeds.Add(inv);
                }

                // 1. 玩家随身与工作台自身嵌套子包插入到 localSourceCount 之前，保持界面“身上/附近”统计边界准确
                var localNested = NestedBagCore.CollectDescendantInventories(localSeeds, visited);
                var playerCd = NestedBagCore.LocalPlayer?.characterData;
                if (playerCd != null)
                {
                    var extraCarried = NestedBagCore.CollectExtraNestedInventories(playerCd);
                    for (int i = 0; i < extraCarried.Count; i++)
                    {
                        var inv = extraCarried[i];
                        IntPtr p = NestedBagCore.Ptr(inv);
                        if (p != IntPtr.Zero && visited.Add(p))
                            localNested.Add(inv);
                    }
                }

                if (localNested.Count > 0)
                {
                    int insertPos = clampedLocal;
                    for (int i = 0; i < localNested.Count; i++)
                    {
                        __result.Insert(insertPos, localNested[i]);
                        insertPos++;
                    }
                    localSourceCount = insertPos;
                }

                // 2. 附近储物箱内部的套包子包追加到列表末尾（受配置项开关控制）
                if (Plugin.IncludeNearbyContainerNestedBags != null &&
                    Plugin.IncludeNearbyContainerNestedBags.Value &&
                    nearbySeeds.Count > 0)
                {
                    var nearbyNested = NestedBagCore.CollectDescendantInventories(nearbySeeds, visited);
                    for (int i = 0; i < nearbyNested.Count; i++)
                        __result.Add(nearbyNested[i]);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("craft_sources", ex);
            }
        }
    }

    /// <summary>
    /// 取消制作序列时，确保能把预扣的工具耐久正确返还给套包里的工具。
    /// </summary>
    [HarmonyPatch(typeof(CraftPanel), nameof(CraftPanel.GetToolDurabilityRefundInventories))]
    internal static class Patch_CraftPanel_GetToolDurabilityRefundInventories
    {
        [HarmonyPostfix]
        private static void Postfix(Il2CppSystem.Collections.Generic.List<InventoryData> __result)
        {
            if (Plugin.EnableNestedCrafting == null || !Plugin.EnableNestedCrafting.Value) return;
            if (__result == null) return;

            try
            {
                var visited = new HashSet<IntPtr>();
                var seeds = new List<InventoryData>(__result.Count);
                for (int i = 0; i < __result.Count; i++)
                {
                    var inv = __result[i];
                    if (inv == null) continue;
                    IntPtr p = NestedBagCore.Ptr(inv);
                    if (p != IntPtr.Zero) visited.Add(p);
                    seeds.Add(inv);
                }

                var nested = NestedBagCore.CollectDescendantInventories(seeds, visited);
                for (int i = 0; i < nested.Count; i++)
                    __result.Add(nested[i]);

                var cd = NestedBagCore.LocalPlayer?.characterData;
                if (cd != null)
                {
                    var extra = NestedBagCore.CollectExtraNestedInventories(cd);
                    for (int i = 0; i < extra.Count; i++)
                    {
                        var inv = extra[i];
                        IntPtr p = NestedBagCore.Ptr(inv);
                        if (p != IntPtr.Zero && visited.Add(p))
                            __result.Add(inv);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("craft_refund", ex);
            }
        }
    }

    /// <summary>
    /// 商人系统全递归：GetAllCharacterItems、购买扣币、批量出售与持有判断均依赖此方法。
    /// </summary>
    [HarmonyPatch(typeof(NPCInteractController), nameof(NPCInteractController.GetAllCharacterInventories))]
    internal static class Patch_NPCInteractController_GetAllCharacterInventories
    {
        [HarmonyPostfix]
        private static void Postfix(Il2CppSystem.Collections.Generic.List<InventoryData> __result)
        {
            if (Plugin.EnableNestedTrading == null || !Plugin.EnableNestedTrading.Value) return;
            if (__result == null) return;

            try
            {
                var visited = new HashSet<IntPtr>();
                var seeds = new List<InventoryData>(__result.Count);
                for (int i = 0; i < __result.Count; i++)
                {
                    var inv = __result[i];
                    if (inv == null) continue;
                    IntPtr p = NestedBagCore.Ptr(inv);
                    if (p != IntPtr.Zero) visited.Add(p);
                    seeds.Add(inv);
                }

                var nested = NestedBagCore.CollectDescendantInventories(seeds, visited);
                for (int i = 0; i < nested.Count; i++)
                    __result.Add(nested[i]);

                var cd = NestedBagCore.LocalPlayer?.characterData;
                if (cd != null)
                {
                    var extra = NestedBagCore.CollectExtraNestedInventories(cd);
                    for (int i = 0; i < extra.Count; i++)
                    {
                        var inv = extra[i];
                        IntPtr p = NestedBagCore.Ptr(inv);
                        if (p != IntPtr.Zero && visited.Add(p))
                            __result.Add(inv);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("trade_invs", ex);
            }
        }
    }
}
