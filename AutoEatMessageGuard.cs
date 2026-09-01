using System;
using System.Diagnostics;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Schachio.HungerPangsPlus
{
    [BepInPlugin("schachio.hungerpangsplus.autoeatmessageguard", "Hunger Pangs Plus Auto Eat Message Guard", "1.0.0")]
    [BepInDependency(HungerPangsPlusPlugin.PluginGuid)]
    internal sealed class AutoEatMessageGuardPlugin : BaseUnityPlugin
    {
        private Harmony _harmony;
        private void Awake(){_harmony=new Harmony("schachio.hungerpangsplus.autoeatmessageguard");_harmony.PatchAll(typeof(AutoEatMessageGuardPlugin).Assembly);}
        private void OnDestroy(){if(_harmony!=null)_harmony.UnpatchSelf();}
    }

    internal static class AutoEatMessageState
    {
        [ThreadStatic] internal static int Depth;
        internal static bool Active{get{return Depth>0;}}
        internal static void Enter(){Depth++;}
        internal static void Exit(){if(Depth>0)Depth--;}
        internal static bool IsFullMessage(string msg)
        {
            if(string.IsNullOrEmpty(msg))return false;
            string a=msg.ToLowerInvariant(),b=a;
            try{if(Localization.instance!=null)b=Localization.instance.Localize(msg).ToLowerInvariant();}catch{}
            return Bad(a)||Bad(b);
        }
        private static bool Bad(string s)
        {
            string[] k={"$msg_toofull","$msg_canteatmore","$msg_canteat","stomach is full","stomach full","my stomach is full","magen ist voll","mein magen ist voll","magen voll","zu voll","too full"};
            foreach(string x in k)if(s.Contains(x))return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class AutoEatConsumeScopePatch
    {
        private static bool OurCaller()
        {
            try{var fs=new StackTrace().GetFrames();if(fs!=null)foreach(var f in fs){var m=f.GetMethod();var t=m!=null?m.DeclaringType:null;if(t==typeof(HungerPangsPlusPlugin)||t==typeof(ExpandedAutomationPlugin))return true;}}catch{}
            return false;
        }
        private static void Prefix(ref bool __state){__state=OurCaller();if(__state)AutoEatMessageState.Enter();}
        private static void Postfix(bool __state){if(__state)AutoEatMessageState.Exit();}
        private static Exception Finalizer(Exception __exception,bool __state){if(__state)AutoEatMessageState.Exit();return __exception;}
    }

    [HarmonyPatch(typeof(Character),"Message",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class AutoEatCharacterMessagePatch
    {
        private static bool Prefix(Character __instance,MessageHud.MessageType type,string msg)
        {
            return !(__instance==Player.m_localPlayer&&type==MessageHud.MessageType.Center&&AutoEatMessageState.Active&&AutoEatMessageState.IsFullMessage(msg));
        }
    }

    [HarmonyPatch(typeof(MessageHud),"ShowMessage",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class AutoEatHudMessagePatch
    {
        private static bool Prefix(MessageHud.MessageType type,string text)
        {
            return !(type==MessageHud.MessageType.Center&&AutoEatMessageState.Active&&AutoEatMessageState.IsFullMessage(text));
        }
    }
}
