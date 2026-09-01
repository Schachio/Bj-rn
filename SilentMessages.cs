using System;
using System.Diagnostics;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Schachio.HungerPangsPlus
{
    [BepInPlugin("schachio.hungerpangsplus.silentmessages", "Hunger Pangs Plus Silent Messages", "1.0.0")]
    [BepInDependency(HungerPangsPlusPlugin.PluginGuid)]
    public sealed class HungerPangsPlusSilentMessagesPlugin : BaseUnityPlugin
    {
        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony("schachio.hungerpangsplus.silentmessages");
            _harmony.PatchAll(typeof(HungerPangsPlusSilentMessagesPlugin).Assembly);
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchSelf();
        }
    }

    internal static class HungerPangsMessageGuard
    {
        internal static bool IsFromAutomation()
        {
            StackFrame[] frames;
            try
            {
                frames = new StackTrace().GetFrames();
            }
            catch
            {
                return false;
            }

            if (frames == null) return false;

            foreach (StackFrame frame in frames)
            {
                var method = frame.GetMethod();
                var type = method != null ? method.DeclaringType : null;
                if (type == typeof(HungerPangsPlusPlugin))
                    return true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "Message", new Type[]
    {
        typeof(MessageHud.MessageType),
        typeof(string),
        typeof(int),
        typeof(Sprite)
    })]
    internal static class CharacterMessagePatch
    {
        private static bool Prefix(Character __instance)
        {
            if (__instance == Player.m_localPlayer && HungerPangsMessageGuard.IsFromAutomation())
                return false;

            return true;
        }
    }
}
