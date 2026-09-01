using System;
using System.Diagnostics;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Schachio.HungerPangsPlus
{
    [BepInPlugin("schachio.hungerpangsplus.silentmessages", "Hunger Pangs Plus Silent Messages", "1.0.9")]
    [BepInDependency(HungerPangsPlusPlugin.PluginGuid)]
    public sealed class HungerPangsPlusSilentMessagesPlugin : BaseUnityPlugin
    {
        private Harmony _harmony;
        private void Awake(){_harmony=new Harmony("schachio.hungerpangsplus.silentmessages");_harmony.PatchAll(typeof(HungerPangsPlusSilentMessagesPlugin).Assembly);}
        private void OnDestroy(){if(_harmony!=null)_harmony.UnpatchSelf();}
    }

    internal static class HungerPangsMessageGuard
    {
        [ThreadStatic] internal static bool Suppress;

        internal static bool IsFromAutomation()
        {
            try
            {
                var frames=new StackTrace().GetFrames();
                if(frames==null)return false;
                foreach(var f in frames)
                {
                    var m=f.GetMethod();
                    var t=m!=null?m.DeclaringType:null;
                    if(t==typeof(HungerPangsPlusPlugin)||t==typeof(ExpandedAutomationPlugin))return true;
                }
            }
            catch{}
            return false;
        }

        internal static bool IsFoodCookingMessage(string msg)
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
            string[] keys={
                "cook","cooking","cooked","koch","kochen","gekocht","kochbar","kochbaren","kochbare","kochst",
                "no cookable","nothing to cook","keine kochbaren","nichts zu kochen",
                "food","eat","eating","ate","edible","consume","consum","hunger","hungry","stomach","full stomach",
                "essen","isst","iss ","essbar","gegessen","verzehr","verzehren","hungr","magen","satt","zu voll",
                "no room","inventory full","kein platz","inventar voll",
                "$msg_full","$msg_nocook","$msg_nocookitems","$msg_cantconsume","$msg_toofull",
                "$msg_canteat","$msg_canteatmore","$msg_canteatyet","$msg_food","$msg_hungry"
            };
            foreach(string k in keys)if(s.Contains(k))return true;
            return false;
        }

        internal static bool Block(MessageHud.MessageType type,string text)
        {
            if(type!=MessageHud.MessageType.Center)return false;
            return Suppress||IsFromAutomation()||IsFoodCookingMessage(text);
        }
    }

    [HarmonyPatch(typeof(Character),"Message",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class CharacterMessagePatch
    {
        private static bool Prefix(Character __instance,MessageHud.MessageType type,string msg)
        {
            return !(__instance==Player.m_localPlayer&&HungerPangsMessageGuard.Block(type,msg));
        }
    }

    [HarmonyPatch(typeof(MessageHud),"ShowMessage",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class MessageHudShowMessagePatch
    {
        private static bool Prefix(MessageHud.MessageType type,string text)
        {
            return !HungerPangsMessageGuard.Block(type,text);
        }
    }

    [HarmonyPatch(typeof(CookingStation),"Interact")]
    internal static class CookingStationInteractPatch
    {
        private static void Prefix(){HungerPangsMessageGuard.Suppress=true;}
        private static void Postfix(){HungerPangsMessageGuard.Suppress=false;}
        private static Exception Finalizer(Exception __exception){HungerPangsMessageGuard.Suppress=false;return __exception;}
    }

    [HarmonyPatch(typeof(CookingStation),"UseItem")]
    internal static class CookingStationUseItemPatch
    {
        private static void Prefix(){HungerPangsMessageGuard.Suppress=true;}
        private static void Postfix(){HungerPangsMessageGuard.Suppress=false;}
        private static Exception Finalizer(Exception __exception){HungerPangsMessageGuard.Suppress=false;return __exception;}
    }
}
