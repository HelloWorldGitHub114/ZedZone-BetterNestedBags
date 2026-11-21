using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ZedZone.BetterNestedBags
{
    /// <summary>
    /// 更好的套包（Better Nested Bags）插件入口：
    /// 统一打通多层嵌套背包（包中包）在角色查询/扣料、建造与维修、制作合成、商人交易、
    /// 换弹装箭、右键工具选择、任务进度同步以及快捷栏直接使用中的单层限制。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BasePlugin
    {
        public const string PluginGuid = "zedzone.betternestedbags";
        public const string PluginName = "Better Nested Bags";
        public const string PluginVersion = "1.0.1";

        internal static ManualLogSource Logs;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> MaxNestDepth;
        internal static ConfigEntry<bool> EnableCharacterRecursion;
        internal static ConfigEntry<bool> EnableNestedHotbar;
        internal static ConfigEntry<bool> EnableNestedCrafting;
        internal static ConfigEntry<bool> EnableNestedTrading;
        internal static ConfigEntry<bool> IncludeNearbyContainerNestedBags;
        internal static ConfigEntry<bool> EnableNestedMissionSync;
        internal static ConfigEntry<bool> VerboseLog;

        private static readonly HashSet<string> LoggedErrorTags = new HashSet<string>(StringComparer.Ordinal);

        public override void Load()
        {
            Logs = Log;

            Enabled = Config.Bind("General", "Enabled", true,
                "启用更好的套包（Better Nested Bags）模组。");
            MaxNestDepth = Config.Bind("General", "MaxNestDepth", 12,
                new ConfigDescription("递归扫描套包的最大层级深度，用于防止异常嵌套导致性能开销。",
                    new AcceptableValueRange<int>(1, 32)));
            VerboseLog = Config.Bind("General", "VerboseLog", false,
                "输出详细调试日志到 BepInEx\\LogOutput.log。");

            EnableCharacterRecursion = Config.Bind("Features", "EnableCharacterRecursion", true,
                "启用角色物品查询、数量统计与增量差额扣料的套包递归（覆盖建造、体素建造、维修锤、换弹装箭、医疗包扎、右键工具等）。");
            EnableNestedHotbar = Config.Bind("Features", "EnableNestedHotbar", true,
                "启用快捷栏套包物品直接使用（支持套包内消耗品、熟食、药品、投掷物、武器切换与快捷栏实时刷新）。");
            EnableNestedCrafting = Config.Bind("Features", "EnableNestedCrafting", true,
                "制作台、熔炉与便携烹饪直接识别套包内的原料与工具（并支持取消制作时返还工具耐久）。");
            EnableNestedTrading = Config.Bind("Features", "EnableNestedTrading", true,
                "商人买卖与批量售卖直接识别套包内的货币与物品。");
            IncludeNearbyContainerNestedBags = Config.Bind("Features", "IncludeNearbyContainerNestedBags", true,
                "制作合成与右键外部容器交互时，同样穿透附近或已打开储物箱内部的套包子包。");
            EnableNestedMissionSync = Config.Bind("Features", "EnableNestedMissionSync", true,
                "物品直接放入随身嵌套子包时同步触发收集任务进度检查。");

            if (!Enabled.Value)
            {
                Log.LogInfo($"{PluginName} is disabled in config.");
                return;
            }

            var harmony = new Harmony(PluginGuid);
            int applied = 0;
            int skipped = 0;

            Type[] patchTypes =
            {
                // 角色核心查询、归属判定、增量差额扣料与任务同步
                typeof(Patch_BasicCharacterController_GetTotalItemNumberByID),
                typeof(Patch_BasicCharacterController_GetAllItemListById_Return),
                typeof(Patch_BasicCharacterController_GetAllItemListById_OutList),
                typeof(Patch_BasicCharacterController_GetAllItemListByType),
                typeof(Patch_BasicCharacterController_GetAllItemListByItemFeature),
                typeof(Patch_BasicCharacterController_GetItemListByFeature),
                typeof(Patch_BasicCharacterController_FindOwnerInventoryData),
                typeof(Patch_BasicCharacterController_IsCharacterOwnedInventory),
                typeof(Patch_BasicCharacterController_RemoveItemFromCharacter_Static),
                typeof(Patch_BasicCharacterController_RemoveItemFromCharacter_Instance),
                typeof(Patch_BasicCharacterController_RemoveItemsFromCharacter),
                typeof(Patch_InventoryData_AddItem_NestedSync),

                // 快捷栏套包使用与实时订阅刷新
                typeof(Patch_ShortcutManager_ResolveFreshestItem),
                typeof(Patch_ShortcutManager_GetItemNumberByIdAndCookState),
                typeof(Patch_ShortcutManager_SelectShortcut_Weapon),
                typeof(Patch_HumanCharacterController_IsItemOnCharacterInventories),
                typeof(Patch_ShortcutPanel_RefreshInventorySubscriptions),

                // 制作合成与商人交易
                typeof(Patch_CraftPanel_GetCraftSourceInventories),
                typeof(Patch_CraftPanel_GetToolDurabilityRefundInventories),
                typeof(Patch_NPCInteractController_GetAllCharacterInventories),

                // 外部箱子嵌套工具与材料穿透
                typeof(Patch_BasicItemUI_CollectExternalItemSourceInventories),
            };

            foreach (var patchType in patchTypes)
            {
                if (PatchOne(harmony, patchType))
                    applied++;
                else
                    skipped++;
            }

            int hooked = harmony.GetPatchedMethods().Count();
            Log.LogInfo($"{PluginName} {PluginVersion}: {applied} patch group(s) applied, {skipped} skipped, {hooked} method(s) hooked.");
        }

        private static bool PatchOne(Harmony harmony, Type patchType)
        {
            try
            {
                harmony.PatchAll(patchType);
                return true;
            }
            catch (Exception ex)
            {
                Logs?.LogWarning($"Patch {patchType.Name} skipped: {ex.Message}");
                return false;
            }
        }

        internal static void LogVerbose(string message)
        {
            if (VerboseLog != null && VerboseLog.Value)
                Logs?.LogInfo("[Verbose] " + message);
        }

        internal static void LogOnce(string tag, Exception ex)
        {
            if (!LoggedErrorTags.Add(tag)) return;
            Logs?.LogWarning($"[{tag}] {ex.GetType().Name}: {ex.Message}");
        }
    }
}
