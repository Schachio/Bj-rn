using System;
using System.Diagnostics;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Schachio.HungerPangsPlus
{
    [BepInPlugin("schachio.hungerpangsplus.silentmessages", "Hunger Pangs Plus Silent Messages", "1.0.3")]
    [BepInDependency(HungerPangsPlusPlugin.PluginGuid)]
    public sealed class HungerPangsPlusSilentMessagesPlugin : BaseUnityPlugin
    {
        private Harmony _harmony;
        private void Awake(){_harmony=new Harmony("schachio.hungerpangsplus.silentmessages");_harmony.PatchAll(typeof(HungerPangsPlusSilentMessagesPlugin).Assembly);}
        private void OnDestroy(){if(_harmony!=null)_harmony.UnpatchSelf();}
    }
    internal static class HungerPangsMessageGuard
    {
        internal static bool IsFromAutomation(){try{var frames=new StackTrace().GetFrames();if(frames==null)return false;foreach(var f in frames){var m=f.GetMethod();var t=m!=null?m.DeclaringType:null;if(t==typeof(HungerPangsPlusPlugin)||t==typeof(ExpandedAutomationPlugin))return true;}}catch{}return false;}
        internal static bool IsCookingSpam(string msg)
        {
            if(string.IsNullOrEmpty(msg))return false;
            string s=msg.ToLowerInvariant();
            return s.Contains("cook")||s.Contains("koch")||s.Contains("cooking")||s.Contains("no room")||s.Contains("kein platz")||s.Contains("inventory full")||s.Contains("inventar voll");
        }
    }
    [HarmonyPatch(typeof(Character),"Message",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class CharacterMessagePatch
    {
        private static bool Prefix(Character __instance,string msg){if(__instance==Player.m_localPlayer&&(HungerPangsMessageGuard.IsFromAutomation()||HungerPangsMessageGuard.IsCookingSpam(msg)))return false;return true;}
    }
    [HarmonyPatch(typeof(MessageHud),"ShowMessage",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class MessageHudShowMessagePatch
    {
        private static bool Prefix(string text){return !(HungerPangsMessageGuard.IsFromAutomation()||HungerPangsMessageGuard.IsCookingSpam(text));}
    }
}
