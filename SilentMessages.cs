using System;
using System.Diagnostics;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Schachio.HungerPangsPlus
{
    [BepInPlugin("schachio.hungerpangsplus.silentmessages", "Hunger Pangs Plus Silent Messages", "1.0.7")]
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
        internal static bool IsAutomationSpam(string msg)
        {
            if(string.IsNullOrEmpty(msg))return false;
            string raw=msg.ToLowerInvariant();
            string localized=raw;
            try{if(Localization.instance!=null)localized=Localization.instance.Localize(msg).ToLowerInvariant();}catch{}
            return Bad(raw)||Bad(localized);
        }
        private static bool Bad(string s)
        {
            if(string.IsNullOrEmpty(s))return false;
            return s.Contains("cook")||s.Contains("koch")||s.Contains("cooking")||s.Contains("kochbar")||s.Contains("kochbaren")||s.Contains("kochbare")||
                   s.Contains("keine kochbaren gegenstände")||s.Contains("keine kochbaren gegenstande")||s.Contains("du hast keine kochbaren")||s.Contains("no cookable")||s.Contains("nothing to cook")||
                   s.Contains("food")||s.Contains("essen")||s.Contains("eat")||s.Contains("eating")||s.Contains("consume")||s.Contains("consum")||s.Contains("hungr")||s.Contains("hunger")||s.Contains("satt")||
                   s.Contains("stomach")||s.Contains("magen")||s.Contains("too full")||s.Contains("zu voll")||
                   s.Contains("no room")||s.Contains("kein platz")||s.Contains("inventory full")||s.Contains("inventar voll")||
                   s.Contains("$msg_full")||s.Contains("$msg_nocook")||s.Contains("$msg_nocookitems")||s.Contains("$msg_cantconsume")||s.Contains("$msg_toofull");
        }
    }
    [HarmonyPatch(typeof(Character),"Message",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class CharacterMessagePatch
    {
        private static bool Prefix(Character __instance,string msg){if(__instance==Player.m_localPlayer&&(HungerPangsMessageGuard.IsFromAutomation()||HungerPangsMessageGuard.IsAutomationSpam(msg)))return false;return true;}
    }
    [HarmonyPatch(typeof(MessageHud),"ShowMessage",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class MessageHudShowMessagePatch
    {
        private static bool Prefix(string text){return !(HungerPangsMessageGuard.IsFromAutomation()||HungerPangsMessageGuard.IsAutomationSpam(text));}
    }
}
