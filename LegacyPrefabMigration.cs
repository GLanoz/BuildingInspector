using System.Collections.Generic;
using HarmonyLib;

namespace BuildingInspector
{
    internal static class LegacyPrefabMigration
    {
        private static readonly Dictionary<int, int> LegacyPrefabHashes = new Dictionary<int, int>
        {
            { StringExtensionMethods.GetStableHashCode("InspectionFlagCustom1"), StringExtensionMethods.GetStableHashCode("InspectionFlagRed") },
            { StringExtensionMethods.GetStableHashCode("InspectionFlagCustom2"), StringExtensionMethods.GetStableHashCode("InspectionFlagYellow") },
            { StringExtensionMethods.GetStableHashCode("InspectionFlagCustom3"), StringExtensionMethods.GetStableHashCode("InspectionFlagGreen") },
            { StringExtensionMethods.GetStableHashCode("InspectionFlagCustom4"), StringExtensionMethods.GetStableHashCode("InspectionFlagBlue") },
            { StringExtensionMethods.GetStableHashCode("InspectionFlagCustom5"), StringExtensionMethods.GetStableHashCode("InspectionFlagPurple") },
            { StringExtensionMethods.GetStableHashCode("InspectionFlagCustom8"), StringExtensionMethods.GetStableHashCode("InspectionFlagWhite") }
        };

        private static int RemapPrefabHash(int prefabHash)
        {
            int currentPrefabHash;
            return LegacyPrefabHashes.TryGetValue(prefabHash, out currentPrefabHash)
                ? currentPrefabHash
                : prefabHash;
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.HasPrefab), new[] { typeof(int) })]
        private static class HasPrefabPatch
        {
            [HarmonyPrefix]
            private static void Prefix(ref int __0)
            {
                __0 = RemapPrefabHash(__0);
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.GetPrefab), new[] { typeof(int) })]
        private static class GetPrefabPatch
        {
            [HarmonyPrefix]
            private static void Prefix(ref int __0)
            {
                __0 = RemapPrefabHash(__0);
            }
        }
    }
}
