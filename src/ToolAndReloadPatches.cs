using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ZedZone.BetterNestedBags
{
    /// <summary>
    /// 外部容器嵌套穿透补丁：
    /// 打开储物箱、车辆后备箱或地面包进行右键工具选择、装备维修、拆解与弹匣/武器装填时，
    /// 一并扫描这些外部容器内部套着的子背包。
    /// </summary>
    [HarmonyPatch(typeof(BasicItemUI), nameof(BasicItemUI.CollectExternalItemSourceInventories),
        typeof(bool))]
    internal static class Patch_BasicItemUI_CollectExternalItemSourceInventories
    {
        [HarmonyPostfix]
        private static void Postfix(Il2CppSystem.Collections.Generic.List<InventoryData> __result)
        {
            if (Plugin.IncludeNearbyContainerNestedBags == null || !Plugin.IncludeNearbyContainerNestedBags.Value)
                return;
            if (__result == null || __result.Count == 0)
                return;

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
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("external_nested", ex);
            }
        }
    }
}
