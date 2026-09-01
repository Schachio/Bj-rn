using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Schachio.HungerPangsPlus
{
    [BepInPlugin("schachio.hungerpangsplus.expandedautomation", "Hunger Pangs Plus Expanded Automation", "1.3.1")]
    [BepInDependency(HungerPangsPlusPlugin.PluginGuid)]
    public sealed class ExpandedAutomationPlugin : BaseUnityPlugin
    {
        private HungerPangsPlusPlugin _main;
        private MethodInfo _pickupMethod;
        private float _nextFoodCheck;
        private float _nextGroundCheck;
        private readonly Dictionary<string,float> _pickedFoodReadyAt=new Dictionary<string,float>();
        private ConfigEntry<bool> _expandedEnabled,_groundPickupEnabled,_pullFromContainers;
        private ConfigEntry<int> _foodSlots;
        private ConfigEntry<float> _foodCheckSeconds,_groundCheckSeconds,_groundPickupRadius,_pickedFoodDelaySeconds,_containerRadius;

        private void Awake()
        {
            _expandedEnabled=Config.Bind("Food Slots","ExpandedFoodSlotsEnabled",true,"Enable automatic filling of all available food slots.");
            _foodSlots=Config.Bind("Food Slots","MaximumFoodSlots",10,new ConfigDescription("Maximum number of food slots Hunger Pangs Plus will probe and fill. The game/mod decides how many slots really exist. Range: 3 to 10.",new AcceptableValueRange<int>(3,10)));
            _foodCheckSeconds=Config.Bind("Auto Eat","FoodCheckIntervalSeconds",.20f,new ConfigDescription("Seconds between automatic food-slot checks. Lower values react faster.",new AcceptableValueRange<float>(.10f,10f)));
            _pickedFoodDelaySeconds=Config.Bind("Auto Eat","PickedFoodDelaySeconds",2f,new ConfigDescription("Seconds to wait before newly picked-up food may be eaten automatically.",new AcceptableValueRange<float>(0f,60f)));
            _pullFromContainers=Config.Bind("Base Refill","PullFoodFromContainers",true,"Allow expanded food automation to pull missing food types from nearby accessible containers.");
            _containerRadius=Config.Bind("Base Refill","ExpandedContainerRadius",20f,new ConfigDescription("Container search radius for expanded food-slot refill.",new AcceptableValueRange<float>(1f,100f)));
            _groundPickupEnabled=Config.Bind("Travel Pickup","AutoPickupFood",true,"Automatically pick up edible food items lying on the ground.");
            _groundPickupRadius=Config.Bind("Travel Pickup","PickupRadius",3.5f,new ConfigDescription("Maximum distance for automatic ground-food pickup.",new AcceptableValueRange<float>(1f,20f)));
            _groundCheckSeconds=Config.Bind("Travel Pickup","PickupCheckIntervalSeconds",.25f,new ConfigDescription("Seconds between checks for edible ground items.",new AcceptableValueRange<float>(.10f,10f)));
        }

        private void Start(){_main=UnityEngine.Object.FindObjectOfType<HungerPangsPlusPlugin>();_pickupMethod=typeof(ItemDrop).GetMethod("Pickup",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new Type[]{typeof(Humanoid)},null);if(_pickupMethod==null)_pickupMethod=typeof(ItemDrop).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).FirstOrDefault(m=>m.Name=="Pickup"&&m.GetParameters().Length==1);}
        private void Update(){Player p=Player.m_localPlayer;if(p==null||p.IsDead())return;float n=Time.time;if(_groundPickupEnabled.Value&&n>=_nextGroundCheck){_nextGroundCheck=n+Mathf.Max(.10f,_groundCheckSeconds.Value);PickupFood(p);}if(_expandedEnabled.Value&&n>=_nextFoodCheck){_nextFoodCheck=n+Mathf.Max(.10f,_foodCheckSeconds.Value);FillSlots(p);}}
        private static bool IsFood(ItemDrop.ItemData i){return i!=null&&i.m_shared!=null&&(i.m_shared.m_food>0f||i.m_shared.m_foodStamina>0f||i.m_shared.m_foodEitr>0f);}
        private static float Score(ItemDrop.ItemData i){return IsFood(i)?i.m_shared.m_food+i.m_shared.m_foodStamina+i.m_shared.m_foodEitr:0f;}
        private bool Ready(ItemDrop.ItemData i){float t;return i!=null&&i.m_shared!=null&&(!_pickedFoodReadyAt.TryGetValue(i.m_shared.m_name,out t)||Time.time>=t);}

        private void FillSlots(Player p)
        {
            Inventory inv=p.GetInventory();if(inv==null)return;
            int maxSlots=Mathf.Clamp(_foodSlots.Value,3,10);
            if(_pullFromContainers.Value)PullFood(p,maxSlots);
            int tries=0;
            while(tries++<maxSlots)
            {
                List<Player.Food> active=p.GetFoods();if(active==null||active.Count>=maxSlots)return;
                var names=new HashSet<string>(active.Where(x=>x!=null&&x.m_item!=null&&x.m_item.m_shared!=null).Select(x=>x.m_item.m_shared.m_name));
                var foods=inv.GetAllItems().Where(IsFood).Where(Ready).Where(x=>x.m_shared!=null&&!names.Contains(x.m_shared.m_name)).OrderByDescending(Score).ToList();
                if(foods.Count==0)return;
                bool fitted=false;
                foreach(var food in foods)
                {
                    int before=active.Count;string name=food.m_shared.m_name;
                    try{p.ConsumeItem(inv,food,false);}catch{continue;}
                    active=p.GetFoods();
                    if(active!=null&&active.Count>before){_pickedFoodReadyAt.Remove(name);fitted=true;break;}
                }
                // No candidate increased the active food count: the actual slot limit is reached
                // (or Valheim rejected all candidates). Stop probing until the next check.
                if(!fitted)return;
            }
        }

        private void PullFood(Player p,int target)
        {
            Inventory inv=p.GetInventory();if(inv==null)return;
            var known=new HashSet<string>();var active=p.GetFoods();if(active!=null)foreach(var f in active)if(f!=null&&f.m_item!=null&&f.m_item.m_shared!=null)known.Add(f.m_item.m_shared.m_name);foreach(var i in inv.GetAllItems())if(IsFood(i)&&i.m_shared!=null)known.Add(i.m_shared.m_name);
            int need=Math.Max(0,target-known.Count);if(need==0)return;var cs=Containers(p,Mathf.Max(1f,_containerRadius.Value),p.GetPlayerID());
            var options=cs.SelectMany(c=>c.GetInventory()!=null?c.GetInventory().GetAllItems().Where(IsFood):Enumerable.Empty<ItemDrop.ItemData>()).Where(i=>i!=null&&i.m_shared!=null&&!known.Contains(i.m_shared.m_name)).GroupBy(i=>i.m_shared.m_name).Select(g=>g.OrderByDescending(Score).First()).OrderByDescending(Score).Take(need).ToList();
            foreach(var o in options){foreach(var c in cs){var src=c.GetInventory();if(src==null)continue;var item=src.GetAllItems().FirstOrDefault(i=>i!=null&&i.m_shared!=null&&i.m_shared.m_name==o.m_shared.m_name&&i.m_stack>0);if(item==null)continue;var copy=item.Clone();copy.m_stack=1;if(inv.CanAddItem(copy,1)&&inv.AddItem(copy)){src.RemoveItem(item,1);known.Add(o.m_shared.m_name);}break;}}
        }

        private void PickupFood(Player p)
        {
            Inventory inv=p.GetInventory();if(inv==null||_pickupMethod==null)return;float radius=Mathf.Max(1f,_groundPickupRadius.Value);
            var drops=UnityEngine.Object.FindObjectsOfType<ItemDrop>().Where(d=>d!=null&&d.gameObject!=null&&d.gameObject.activeInHierarchy&&d.m_itemData!=null&&Vector3.Distance(p.transform.position,d.transform.position)<=radius&&IsFood(d.m_itemData)).OrderBy(d=>Vector3.Distance(p.transform.position,d.transform.position)).ToList();
            foreach(var d in drops){string name=d.m_itemData.m_shared.m_name;int before=Count(inv,name);try{_pickupMethod.Invoke(d,new object[]{p});}catch{continue;}if(Count(inv,name)>before)_pickedFoodReadyAt[name]=Time.time+Mathf.Max(0f,_pickedFoodDelaySeconds.Value);}
        }
        private List<Container> Containers(Player p,float r,long id){return UnityEngine.Object.FindObjectsOfType<Container>().Where(c=>c!=null&&c.gameObject!=null&&Vector3.Distance(p.transform.position,c.transform.position)<=r&&Safe(c)&&Access(c,id)).OrderBy(c=>Vector3.Distance(p.transform.position,c.transform.position)).ToList();}
        private static bool Access(Container c,long id){try{return c.CheckAccess(id);}catch{return false;}}
        private static bool Safe(Container c){string n=c.gameObject.name??"";return n.IndexOf("tombstone",StringComparison.OrdinalIgnoreCase)<0&&n.IndexOf("grave",StringComparison.OrdinalIgnoreCase)<0&&n.IndexOf("cart",StringComparison.OrdinalIgnoreCase)<0&&n.IndexOf("wagon",StringComparison.OrdinalIgnoreCase)<0&&n.IndexOf("ship",StringComparison.OrdinalIgnoreCase)<0;}
        private static int Count(Inventory inv,string n){int x=0;foreach(var i in inv.GetAllItems())if(i!=null&&i.m_shared!=null&&i.m_shared.m_name==n)x+=i.m_stack;return x;}
    }
}
