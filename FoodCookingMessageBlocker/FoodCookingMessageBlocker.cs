using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Schachio.FoodCookingMessageBlocker
{
    [BepInPlugin("schachio.foodcookingmessageblocker", "Food Cooking Message Blocker", "1.0.0")]
    public sealed class FoodCookingMessageBlockerPlugin : BaseUnityPlugin
    {
        private Harmony _harmony;
        private void Awake(){_harmony=new Harmony("schachio.foodcookingmessageblocker");_harmony.PatchAll(typeof(FoodCookingMessageBlockerPlugin).Assembly);}
        private void OnDestroy(){if(_harmony!=null)_harmony.UnpatchSelf();}
    }

    internal static class Filter
    {
        internal static bool Match(string text)
        {
            if(string.IsNullOrEmpty(text))return false;
            string raw=text.ToLowerInvariant();
            string localized=raw;
            try{if(Localization.instance!=null)localized=Localization.instance.Localize(text).ToLowerInvariant();}catch{}
            return MatchOne(raw)||MatchOne(localized);
        }
        private static bool MatchOne(string s)
        {
            string[] terms={
                "cook","cooking","cooked","cookable","koch","kochen","gekocht","kochbar","kochbaren","kochbare",
                "no cookable","nothing to cook","keine kochbaren","nichts zu kochen",
                "food","edible","eat","eating","consume","consum","hungry","hunger","stomach","full stomach",
                "essen","essbar","gegessen","verzehr","hungr","magen","satt","zu voll",
                "$msg_nocook","$msg_nocookitems","$msg_cantconsume","$msg_toofull","$msg_canteat","$msg_canteatmore","$msg_canteatyet","$msg_food","$msg_hungry"
            };
            foreach(string term in terms)if(s.Contains(term))return true;
            return false;
        }
        internal static bool Block(MessageHud.MessageType type,string text){return type==MessageHud.MessageType.Center&&Match(text);}
    }

    [HarmonyPatch(typeof(Character),"Message",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class CharacterMessagePatch
    {
        private static bool Prefix(Character __instance,MessageHud.MessageType type,string msg)
        { return !(__instance==Player.m_localPlayer&&Filter.Block(type,msg)); }
    }

    [HarmonyPatch(typeof(MessageHud),"ShowMessage",new Type[]{typeof(MessageHud.MessageType),typeof(string),typeof(int),typeof(Sprite)})]
    internal static class ShowMessagePatch
    {
        private static bool Prefix(MessageHud.MessageType type,string text){return !Filter.Block(type,text);}
    }

    [HarmonyPatch]
    internal static class UpdateMessagePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(var m in typeof(MessageHud).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                if(m.Name=="UpdateMessage")yield return m;
        }
        private static bool Prefix(object[] __args)
        {
            if(__args==null)return true;
            foreach(object arg in __args)if(arg is string&&Filter.Match((string)arg))return false;
            return true;
        }
    }
}
