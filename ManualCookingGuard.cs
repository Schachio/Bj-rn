using BepInEx;
using HarmonyLib;

namespace Schachio.HungerPangsPlus
{
    [BepInPlugin("schachio.hungerpangsplus.manualcookingguard", "Hunger Pangs Plus Manual Cooking Guard", "1.0.0")]
    [BepInDependency(HungerPangsPlusPlugin.PluginGuid)]
    internal sealed class ManualCookingGuardPlugin : BaseUnityPlugin
    {
        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony("schachio.hungerpangsplus.manualcookingguard");
            _harmony.PatchAll(typeof(ManualCookingGuardPlugin).Assembly);
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchSelf();
        }
    }

    // Cooking stations are intentionally manual-only. Any background/mod-driven
    // Interact call is rejected unless the player is physically pressing the
    // game's bound Use control at that moment. Keyboard rebinding and gamepad
    // are respected through Valheim's ZInput action names.
    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Interact))]
    internal static class ManualCookingOnlyPatch
    {
        private static bool Prefix(Humanoid user, ref bool __result)
        {
            Player local = Player.m_localPlayer;
            if (local == null || user != local)
                return true;

            bool manualUse = ZInput.instance != null &&
                (ZInput.GetButton("Use") || ZInput.GetButton("JoyUse"));

            if (manualUse)
                return true;

            __result = false;
            return false;
        }
    }
}
