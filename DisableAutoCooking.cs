using HarmonyLib;

namespace Schachio.HungerPangsPlus
{
    // Automatic cooking is intentionally disabled. Cooking stations are now
    // operated manually by the player; all food-slot/refill automation remains unchanged.
    [HarmonyPatch(typeof(HungerPangsPlusPlugin), "TryAutoCooking")]
    internal static class DisableAutoCookingPatch
    {
        private static bool Prefix()
        {
            return false;
        }
    }
}
