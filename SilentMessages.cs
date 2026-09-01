using System;
using System.Diagnostics;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Schachio.HungerPangsPlus
{
    [BepInPlugin("schachio.hungerpangsplus.silentmessages", "Hunger Pangs Plus Silent Messages", "1.1.0")]
    [BepInDependency(HungerPangsPlusPlugin.PluginGuid)]
    public sealed class HungerPangsPlusSilentMessagesPlugin : BaseUnityPlugin
    {
        private Harmony _harmony;
        private void Awake(){_harmony=new Harmony("schachio.hungerpangsplus.silentmessages");_harmony.PatchAll(typeof(HungerPangsPlusSilentMessagesPlugin).Assembly);}
        private void OnDestroy(){if(_harmony!=null)_harmony.UnpatchSelf();}
    }
    internal static class HungerPangsMessageGuard
    {
        [ThreadStatic] internal static int SuppressDepth;
        internal static bool Suppress{get{return SuppressDepth>0;}}
        internal static void Enter(){SuppressDepth++;}
        internal static void Exit(){if(SuppressDepth>0)SuppressDepth--;}
        internal static bool IsFromAutomation(){try{var fs=new StackTrace().GetFrames();if(fs!=null)foreach(var f in fs){var m=f.GetMethod();var t=m!=null?m.DeclaringType:null;if(t==typeof(HungerPangsPlusPlugin)||t==typeof(ExpandedAutomationPlugin))return true;}}catch{}return false;}
        internal static bool IsFoodCookingMessage(string msg){if(string.IsNullOrEmpty(msg))return false;string a=msg.ToLowerInvariant(),b=a;try{if(Localization.instance!=null)b=Localization.instance.Localize(msg).ToLowerInvariant();}catch{}return Bad(a)||Bad(b);}
        private static bool Bad(string s){if(string.IsNullOrEmpty(s))return false;string[] k={"cook","cooking","cooked","koch","kochen","gekocht","kochbar","kochbaren","kochbare","kochst","no cookable","nothing to cook","keine kochbaren","nichts zu kochen","food","eat","eating","edible","consume","consum","hunger","hungry","stomach","full stomach","essen","isst","essbar","gegessen","verzehr","hungr","magen","satt","zu voll","no room","inventory full","kein platz","inventar voll","$msg_full","$msg_nocook","$msg_nocookitems","$msg_cantconsume","$msg_toofull","$msg_canteat","$msg_canteatmore","$msg_canteatyet","$msg_food","$msg_hungry"};foreach(string x in k)if(s.Contains(x))return true;return false;}
        internal static bool Block(MessageHud.MessageType type,string text){return type==MessageHud.MessageType.Center&&(Suppress||IsFromAutomation()||IsFoodCookingMessage(text));}
    }
    [HarmonyPatch(typeof(Character),"Message",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class CharacterMessagePatch{private static bool Prefix(Character __instance,MessageHud.MessageType type,string msg){return !(__instance==Player.m_localPlayer&&HungerPangsMessageGuard.Block(type,msg));}}
    [HarmonyPatch(typeof(MessageHud),"ShowMessage",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class MessageHudShowMessagePatch{private static bool Prefix(MessageHud.MessageType type,string text){return !HungerPangsMessageGuard.Block(type,text);}}

    // Valheim also renders queued center messages through UpdateMessage. Patch every overload
    // dynamically so this remains compatible when the exact signature differs between game builds.
    [HarmonyPatch]
    internal static class MessageHudUpdateMessagePatch
    {
        private static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            foreach(var m in typeof(MessageHud).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                if(m.Name=="UpdateMessage")yield return m;
        }
        private static bool Prefix(object[] __args)
        {
            if(__args==null)return true;
            foreach(object a in __args)
                if(a is string && HungerPangsMessageGuard.IsFoodCookingMessage((string)a))return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(CookingStation),"Interact")]
    internal static class CookingStationInteractPatch{private static void Prefix(){HungerPangsMessageGuard.Enter();}private static void Postfix(){HungerPangsMessageGuard.Exit();}private static Exception Finalizer(Exception __exception){HungerPangsMessageGuard.Exit();return __exception;}}
    [HarmonyPatch(typeof(CookingStation),"UseItem")]
    internal static class CookingStationUseItemPatch{private static void Prefix(){HungerPangsMessageGuard.Enter();}private static void Postfix(){HungerPangsMessageGuard.Exit();}private static Exception Finalizer(Exception __exception){HungerPangsMessageGuard.Exit();return __exception;}}
}
