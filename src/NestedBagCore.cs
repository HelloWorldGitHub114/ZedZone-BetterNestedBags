using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace ZedZone.BetterNestedBags
{
    /// <summary>
    /// 多层嵌套背包核心遍历与辅助工具：
    /// 统一采用 IL2CPP 原生指针去重与 BFS 浅层优先策略，避免循环引用与重复统计。
    /// </summary>
    internal static class NestedBagCore
    {
        internal static int EffectiveMaxDepth =>
            Plugin.MaxNestDepth != null ? Math.Clamp(Plugin.MaxNestDepth.Value, 1, 32) : 12;

        internal static IntPtr Ptr(Il2CppObjectBase obj)
        {
            return obj == null ? IntPtr.Zero : IL2CPP.Il2CppObjectBaseToPtr(obj);
        }

        internal static bool Same(Il2CppObjectBase a, Il2CppObjectBase b)
        {
            IntPtr pa = Ptr(a);
            return pa != IntPtr.Zero && pa == Ptr(b);
        }

        internal static HumanCharacterController LocalPlayer
        {
            get
            {
                var gc = GameController.instance;
                return gc != null ? gc.playerCharacter : null;
            }
        }

        internal static bool IsLocalPlayer(BasicCharacterController character)
        {
            if (character == null) return false;
            var player = LocalPlayer;
            return player != null && Same(character, player);
        }

        internal static ItemAttr GetAttr(int itemId)
        {
            var mgr = ItemManager.instance;
            if (mgr == null || itemId < 0) return null;
            return mgr.FindItemAttrById(itemId) ?? mgr.GetItemAttrById(itemId);
        }

        /// <summary>
        /// 收集原版非递归逻辑已经扫描过的三处表层容器指针（主背包、上衣口袋列表、裤子口袋列表）。
        /// Postfix 补丁只需补充表层之外的嵌套容器，从而严格保留原版表层顺序并杜绝重复计数或双倍扣料。
        /// </summary>
        internal static HashSet<IntPtr> CollectVanillaSurfaceInventoryPtrs(CharacterData cd)
        {
            var surface = new HashSet<IntPtr>();
            if (cd == null) return surface;

            AddPtr(surface, cd.inventoryData);

            var eq = cd.characterEquipmentData;
            if (eq != null)
            {
                AddClothingInventoryPtrs(surface, eq.torseItemList);
                AddClothingInventoryPtrs(surface, eq.legItemList);
            }

            return surface;
        }

        /// <summary>
        /// 收集角色身上全部携带容器（优先复用原版防环递归列表，并辅以 BFS 深度兜底）。
        /// </summary>
        internal static List<InventoryData> CollectAllCarriedInventories(CharacterData cd)
        {
            var result = new List<InventoryData>();
            if (cd == null) return result;

            var visited = new HashSet<IntPtr>();
            var roots = new List<InventoryData>();

            void EnqueueSeed(InventoryData inv)
            {
                if (inv == null) return;
                IntPtr p = Ptr(inv);
                if (p == IntPtr.Zero || !visited.Add(p)) return;
                result.Add(inv);
                roots.Add(inv);
            }

            // 1. 优先吸收原版 GetCarriedInventoryList（已覆盖主背包、服装口袋及各装备槽携带容器）
            try
            {
                var nativeCarried = cd.GetCarriedInventoryList();
                if (nativeCarried != null)
                {
                    for (int i = 0; i < nativeCarried.Count; i++)
                        EnqueueSeed(nativeCarried[i]);
                }
            }
            catch
            {
                // 回退到显式表层收集
            }

            // 2. 确保主背包与衣服口袋必定在种子集合内
            EnqueueSeed(cd.inventoryData);
            var eq = cd.characterEquipmentData;
            if (eq != null)
            {
                AddClothingInventories(eq.torseItemList, EnqueueSeed);
                AddClothingInventories(eq.legItemList, EnqueueSeed);
            }

            // 3. 对全部种子容器再做一次有深度上限的 BFS 展开，确保多层套包无遗漏
            ExpandSubInventories(roots, visited, result);
            return result;
        }

        /// <summary>
        /// 仅收集原版表层三处之外的嵌套子背包与扩展携带容器（浅层优先、去重防环）。
        /// </summary>
        internal static List<InventoryData> CollectExtraNestedInventories(CharacterData cd)
        {
            var extra = new List<InventoryData>();
            if (cd == null) return extra;

            var surface = CollectVanillaSurfaceInventoryPtrs(cd);
            var all = CollectAllCarriedInventories(cd);
            for (int i = 0; i < all.Count; i++)
            {
                var inv = all[i];
                IntPtr p = Ptr(inv);
                if (p != IntPtr.Zero && !surface.Contains(p))
                    extra.Add(inv);
            }

            return extra;
        }

        /// <summary>
        /// 展开给定容器集合内部嵌套的所有子背包（不包含种子容器自身，结果已按指针去重）。
        /// </summary>
        internal static List<InventoryData> CollectDescendantInventories(
            IEnumerable<InventoryData> seeds,
            HashSet<IntPtr> alreadyVisited)
        {
            var discovered = new List<InventoryData>();
            if (seeds == null) return discovered;

            var visited = alreadyVisited ?? new HashSet<IntPtr>();
            var queue = new Queue<(InventoryData inv, int depth)>();

            foreach (var seed in seeds)
            {
                if (seed == null) continue;
                IntPtr p = Ptr(seed);
                if (p != IntPtr.Zero)
                    visited.Add(p);
                queue.Enqueue((seed, 0));
            }

            int maxDepth = EffectiveMaxDepth;
            while (queue.Count > 0)
            {
                var (cur, depth) = queue.Dequeue();
                if (depth >= maxDepth) continue;

                var items = cur.itemList;
                if (items == null) continue;

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item == null) continue;
                    var subInv = item.inventoryData;
                    if (subInv == null) continue;

                    IntPtr sp = Ptr(subInv);
                    if (sp == IntPtr.Zero || !visited.Add(sp)) continue;

                    discovered.Add(subInv);
                    queue.Enqueue((subInv, depth + 1));
                }
            }

            return discovered;
        }

        /// <summary>
        /// 判断指定容器是否属于角色随身容器集合（主背包、衣服口袋或任意深度嵌套子包）。
        /// </summary>
        internal static bool IsOwnedByCharacter(CharacterData cd, InventoryData target, out bool isNestedOnly)
        {
            isNestedOnly = false;
            if (cd == null || target == null) return false;

            IntPtr targetPtr = Ptr(target);
            if (targetPtr == IntPtr.Zero) return false;

            var surface = CollectVanillaSurfaceInventoryPtrs(cd);
            if (surface.Contains(targetPtr))
                return true;

            var all = CollectAllCarriedInventories(cd);
            for (int i = 0; i < all.Count; i++)
            {
                if (Ptr(all[i]) == targetPtr)
                {
                    isNestedOnly = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 在全部随身容器（含多层嵌套子包）中按 IL2CPP 指针查找物品所属容器，不回调原版 BasicCharacterController 避免任何递归风险。
        /// </summary>
        internal static InventoryData FindOwnerInInventories(List<InventoryData> inventories, ItemData targetItem)
        {
            if (inventories == null || targetItem == null) return null;
            IntPtr targetPtr = Ptr(targetItem);
            if (targetPtr == IntPtr.Zero) return null;

            for (int i = 0; i < inventories.Count; i++)
            {
                var inv = inventories[i];
                var items = inv?.itemList;
                if (items == null) continue;

                for (int j = 0; j < items.Count; j++)
                {
                    if (Ptr(items[j]) == targetPtr)
                        return inv;
                }
            }

            return null;
        }

        internal static InventoryData FindOwnerInAllCarried(CharacterData cd, ItemData targetItem)
        {
            return FindOwnerInInventories(CollectAllCarriedInventories(cd), targetItem);
        }

        internal static InventoryData FindOwnerInExtraNested(CharacterData cd, ItemData targetItem)
        {
            return FindOwnerInInventories(CollectExtraNestedInventories(cd), targetItem);
        }

        /// <summary>
        /// 直接从表层之外的随身嵌套子包中移除指定物品实例并刷新格位与总负重，完全避免回调 RemoveItemFromCharacter 引发递归。
        /// </summary>
        internal static bool TryRemoveFromExtraNested(CharacterData cd, ItemData targetItem, bool executeOnChange)
        {
            if (cd == null || targetItem == null) return false;
            var ownerInv = FindOwnerInExtraNested(cd, targetItem);
            if (ownerInv == null) return false;

            bool removed = ownerInv.RemoveItem(targetItem, executeOnChange);
            if (!removed) return false;

            ownerInv.RefreshOccupyData();
            ownerInv.RefreshTotalItemWeight();
            RefreshCarriedWeights(cd);
            return true;
        }

        /// <summary>
        /// 扣除或移除嵌套子包物品后，同步刷新角色表层容器的递归负重缓存。
        /// </summary>
        internal static void RefreshCarriedWeights(CharacterData cd)
        {
            if (cd == null) return;
            try
            {
                cd.inventoryData?.RefreshTotalItemWeight();
                var eq = cd.characterEquipmentData;
                if (eq != null)
                {
                    RefreshClothWeights(eq.torseItemList);
                    RefreshClothWeights(eq.legItemList);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("refresh_weights", ex);
            }
        }

        /// <summary>
        /// 将追加的物品列表按 IL2CPP 指针去重合并到目标结果列表。
        /// </summary>
        internal static void AppendUniqueItems(
            Il2CppSystem.Collections.Generic.List<ItemData> target,
            Il2CppSystem.Collections.Generic.List<ItemData> addition,
            HashSet<IntPtr> seenPtrs)
        {
            if (target == null || addition == null || addition.Count == 0) return;

            for (int i = 0; i < addition.Count; i++)
            {
                var item = addition[i];
                IntPtr p = Ptr(item);
                if (p != IntPtr.Zero && seenPtrs.Add(p))
                    target.Add(item);
            }
        }

        internal static HashSet<IntPtr> BuildItemPtrSet(Il2CppSystem.Collections.Generic.List<ItemData> items)
        {
            var set = new HashSet<IntPtr>();
            if (items == null) return set;

            for (int i = 0; i < items.Count; i++)
            {
                IntPtr p = Ptr(items[i]);
                if (p != IntPtr.Zero)
                    set.Add(p);
            }

            return set;
        }

        private static void ExpandSubInventories(
            List<InventoryData> seeds,
            HashSet<IntPtr> visited,
            List<InventoryData> output)
        {
            var queue = new Queue<(InventoryData inv, int depth)>();
            for (int i = 0; i < seeds.Count; i++)
                queue.Enqueue((seeds[i], 0));

            int maxDepth = EffectiveMaxDepth;
            while (queue.Count > 0)
            {
                var (cur, depth) = queue.Dequeue();
                if (depth >= maxDepth) continue;

                var items = cur.itemList;
                if (items == null) continue;

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item == null) continue;
                    var subInv = item.inventoryData;
                    if (subInv == null) continue;

                    IntPtr sp = Ptr(subInv);
                    if (sp == IntPtr.Zero || !visited.Add(sp)) continue;

                    output.Add(subInv);
                    queue.Enqueue((subInv, depth + 1));
                }
            }
        }

        private static void AddPtr(HashSet<IntPtr> set, InventoryData inv)
        {
            IntPtr p = Ptr(inv);
            if (p != IntPtr.Zero)
                set.Add(p);
        }

        private static void AddClothingInventoryPtrs(
            HashSet<IntPtr> set,
            Il2CppSystem.Collections.Generic.List<ItemData> clothList)
        {
            if (clothList == null) return;
            for (int i = 0; i < clothList.Count; i++)
            {
                var cloth = clothList[i];
                if (cloth != null && cloth.inventoryData != null)
                    AddPtr(set, cloth.inventoryData);
            }
        }

        private static void AddClothingInventories(
            Il2CppSystem.Collections.Generic.List<ItemData> clothList,
            Action<InventoryData> addAction)
        {
            if (clothList == null) return;
            for (int i = 0; i < clothList.Count; i++)
            {
                var cloth = clothList[i];
                if (cloth != null && cloth.inventoryData != null)
                    addAction(cloth.inventoryData);
            }
        }

        private static void RefreshClothWeights(Il2CppSystem.Collections.Generic.List<ItemData> clothList)
        {
            if (clothList == null) return;
            for (int i = 0; i < clothList.Count; i++)
            {
                var cloth = clothList[i];
                cloth?.inventoryData?.RefreshTotalItemWeight();
            }
        }
    }
}
