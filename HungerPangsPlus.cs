using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Schachio.HungerPangsPlus
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class HungerPangsPlusPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "schachio.hungerpangsplus";
        public const string PluginName = "Hunger Pangs Plus";
        public const string PluginVersion = "1.6.0";

        private ConfigEntry<bool> _masterEnabled;
        private ConfigEntry<KeyboardShortcut> _toggleShortcut;
        private ConfigEntry<bool> _autoEat, _autoRefill, _autoHotbar, _autoHarvest, _autoCooking;
        private ConfigEntry<bool> _clickFoodSlots, _returnRemovedFood;
        private ConfigEntry<float> _baseRadius, _containerRadius, _harvestRadius, _eatInterval, _cookingRadius, _cookingInterval;
        private ConfigEntry<float> _manualFoodPauseSeconds;
        private ConfigEntry<int> _foodTypesToStock;
        private float _nextEat, _nextRefill, _nextHarvest, _nextCooking, _autoEatPausedUntil;
        private Hud _hookedHud;
        private MethodInfo _updateFoodMethod;
        private FieldInfo _foodIconsField;

        private void Awake()
        {
            _masterEnabled=Config.Bind("General","AutomationEnabled",true,"Master switch for all Hunger Pangs Plus automation.");
            _toggleShortcut=Config.Bind("General","ToggleShortcut",new KeyboardShortcut(KeyCode.F3,KeyCode.LeftAlt),"Keyboard shortcut to toggle all automation on/off. Default: Left Alt + F3.");
            _autoEat=Config.Bind("Auto eat","Enabled",true,"Automatically eat only when Valheim has a free or refreshable food slot.");
            _eatInterval=Config.Bind("Auto eat","CheckIntervalSeconds",1f,"How often auto-eat checks food slots.");
            _clickFoodSlots=Config.Bind("Auto eat","ClickableActiveFoodSlots",true,"Allow left or right click on an active food icon beside the health bar to remove that food.");
            _returnRemovedFood=Config.Bind("Auto eat","ReturnRemovedFoodToInventory",true,"Return one removed active food item to the player inventory when a food slot is clicked.");
            _manualFoodPauseSeconds=Config.Bind("Auto eat","ManualSelectionPauseSeconds",60f,"Pause auto-eating after manually removing an active food. Default: 60 seconds.");
            _autoRefill=Config.Bind("Base refill","Enabled",true,"Refill prepared edible food from nearby accessible containers while at base.");
            _baseRadius=Config.Bind("Base refill","WorkbenchRadius",20f,"Distance from a workbench that counts as base.");
            _containerRadius=Config.Bind("Base refill","ContainerRadius",20f,"Maximum distance to food containers.");
            _foodTypesToStock=Config.Bind("Base refill","FoodTypesToStock",3,"How many different food types to pull from nearby containers.");
            _autoHotbar=Config.Bind("Hotbar","Enabled",true,"Place the selected food stacks into free hotbar slots without replacing existing items.");
            _autoHarvest=Config.Bind("Travel harvest","Enabled",true,"Harvest only edible Pickable plants while outside base.");
            _harvestRadius=Config.Bind("Travel harvest","Radius",3.5f,"Maximum automatic edible-plant harvesting distance.");
            _autoCooking=Config.Bind("Auto cooking","Enabled",true,"Automatically collect finished cooking-station food and load compatible raw ingredients from inventory or nearby containers while at base.");
            _cookingRadius=Config.Bind("Auto cooking","CookingStationRadius",12f,"Maximum distance to cooking stations used by auto cooking.");
            _cookingInterval=Config.Bind("Auto cooking","CheckIntervalSeconds",2f,"How often nearby cooking stations are checked.");
            _updateFoodMethod=typeof(Player).GetMethod("UpdateFood",BindingFlags.Instance|BindingFlags.NonPublic,null,new Type[]{typeof(float),typeof(bool)},null);
            _foodIconsField=typeof(Hud).GetField("m_foodIcons",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            Logger.LogInfo(PluginName+" "+PluginVersion+" loaded");
        }

        private void Update()
        {
            Player p=Player.m_localPlayer;
            if(p==null||p.IsDead()) return;

            EnsureFoodSlotHandlers();

            if(_toggleShortcut.Value.IsDown())
            {
                _masterEnabled.Value=!_masterEnabled.Value;
                Config.Save();
                try { p.Message(MessageHud.MessageType.Center, PluginName+": Automatik "+(_masterEnabled.Value?"AN":"AUS")); }
                catch { }
            }

            if(!_masterEnabled.Value) return;

            float now=Time.time;
            bool atBase=IsAtBase(p.transform.position);

            if(atBase&&_autoCooking.Value&&now>=_nextCooking)
            {
                _nextCooking=now+Mathf.Max(.5f,_cookingInterval.Value);
                TryAutoCooking(p);
            }

            if(atBase&&_autoRefill.Value&&now>=_nextRefill)
            {
                _nextRefill=now+4f;
                TryRefill(p);
                if(_autoHotbar.Value) PutSelectedFoodOnHotbar(p.GetInventory());
                if(_autoEat.Value&&now>=_autoEatPausedUntil) TryAutoEat(p);
            }
            if(_autoEat.Value&&now>=_nextEat&&now>=_autoEatPausedUntil)
            {
                _nextEat=now+Mathf.Max(.25f,_eatInterval.Value);
                TryAutoEat(p);
            }
            if(!atBase&&_autoHarvest.Value&&now>=_nextHarvest)
            {
                _nextHarvest=now+.75f;
                TryHarvest(p);
            }
        }

        private void EnsureFoodSlotHandlers()
        {
            if(!_clickFoodSlots.Value) return;
            Hud hud=Hud.instance;
            if(hud==null||hud==_hookedHud||_foodIconsField==null) return;
            Array icons=_foodIconsField.GetValue(hud) as Array;
            if(icons==null) return;
            _hookedHud=hud;
            for(int i=0;i<icons.Length;i++)
            {
                Component icon=icons.GetValue(i) as Component;
                if(icon==null) continue;
                UIInputHandler handler=icon.GetComponent<UIInputHandler>();
                if(handler==null) handler=icon.gameObject.AddComponent<UIInputHandler>();
                int slot=i;
                handler.m_onLeftClick += delegate(UIInputHandler _) { OnFoodSlotClicked(slot); };
                handler.m_onRightClick += delegate(UIInputHandler _) { OnFoodSlotClicked(slot); };
            }
        }

        private void OnFoodSlotClicked(int slot)
        {
            if(!_clickFoodSlots.Value) return;
            Player p=Player.m_localPlayer;
            if(p==null||p.IsDead()) return;
            List<Player.Food> foods=p.GetFoods();
            if(foods==null||slot<0||slot>=foods.Count) return;
            Player.Food active=foods[slot];
            if(active==null||active.m_item==null) return;

            Inventory inv=p.GetInventory();
            if(_returnRemovedFood.Value&&inv!=null)
            {
                ItemDrop.ItemData returned=active.m_item.Clone();
                returned.m_stack=1;
                if(!inv.CanAddItem(returned,1))
                {
                    try { p.Message(MessageHud.MessageType.Center,PluginName+": Inventar voll - Essen bleibt aktiv"); } catch { }
                    return;
                }
                if(!inv.AddItem(returned)) return;
            }

            string foodName=active.m_item.m_shared!=null?active.m_item.m_shared.m_name:"Essen";
            foods.RemoveAt(slot);
            try { if(_updateFoodMethod!=null) _updateFoodMethod.Invoke(p,new object[]{0f,true}); } catch(Exception e) { Logger.LogDebug("Food refresh skipped: "+e.Message); }

            float pause=Mathf.Max(0f,_manualFoodPauseSeconds.Value);
            _autoEatPausedUntil=Time.time+pause;
            _nextEat=_autoEatPausedUntil;
            try { p.Message(MessageHud.MessageType.Center,PluginName+": "+foodName+" entfernt - Auto-Essen pausiert "+Mathf.CeilToInt(pause)+" Sek."); } catch { }
        }

        private static bool IsFood(ItemDrop.ItemData i)
        {
            return i!=null&&i.m_shared!=null&&(i.m_shared.m_food>0f||i.m_shared.m_foodStamina>0f||i.m_shared.m_foodEitr>0f);
        }

        private static float FoodScore(ItemDrop.ItemData i)
        {
            return IsFood(i)?i.m_shared.m_food+i.m_shared.m_foodStamina+i.m_shared.m_foodEitr:0f;
        }

        private static bool IsPreparedFood(ItemDrop.ItemData i)
        {
            if(!IsFood(i)) return false;
            string n=(i.m_shared.m_name??"").ToLowerInvariant();
            string p=(i.m_dropPrefab!=null?i.m_dropPrefab.name:"").ToLowerInvariant();
            string s=n+" "+p;
            string[] keys={"cooked","cook","grilled","roast","stew","soup","pie","pudding","sausage","bread","omelette","salad","wrap","platter","jerky","skewer","eyescream","muckshake","queensjam","onionsoup","wolfjerky","fishwrap","loxpie","bloodpudding","serpentstew"};
            return keys.Any(k=>s.Contains(k));
        }

        private static float BaseSelectionScore(ItemDrop.ItemData i)
        {
            return FoodScore(i)+(IsPreparedFood(i)?10000f:0f);
        }

        private static bool CanEatSilently(Player p,ItemDrop.ItemData c)
        {
            if(!IsFood(c)) return false;
            List<Player.Food> f=p.GetFoods();
            if(f==null) return true;
            foreach(Player.Food a in f)
            {
                if(a==null||a.m_item==null||a.m_item.m_shared==null) continue;
                if(a.m_item.m_shared.m_name==c.m_shared.m_name) return a.CanEatAgain();
            }
            return f.Count<3;
        }

        private void TryAutoEat(Player p)
        {
            Inventory inv=p.GetInventory();
            if(inv==null) return;
            foreach(ItemDrop.ItemData f in inv.GetAllItems().Where(x=>CanEatSilently(p,x)).OrderByDescending(BaseSelectionScore).ToList())
            {
                try { if(p.ConsumeItem(inv,f,false)) return; }
                catch(Exception e) { Logger.LogDebug("Auto-eat skipped item: "+e.Message); }
            }
        }

        private bool IsAtBase(Vector3 pos)
        {
            float r=Mathf.Max(1f,_baseRadius.Value);
            foreach(CraftingStation s in UnityEngine.Object.FindObjectsOfType<CraftingStation>())
            {
                if(s==null||s.gameObject==null) continue;
                string o=s.gameObject.name??"",n=s.m_name??"";
                bool w=o.IndexOf("workbench",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("workbench",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("$piece_workbench",StringComparison.OrdinalIgnoreCase)>=0;
                if(w&&Vector3.Distance(pos,s.transform.position)<=r) return true;
            }
            return false;
        }

        private List<Container> GetNearbyContainers(Player p)
        {
            float r=Mathf.Max(1f,_containerRadius.Value);
            return UnityEngine.Object.FindObjectsOfType<Container>()
                .Where(c=>c!=null&&c.gameObject!=null&&Vector3.Distance(p.transform.position,c.transform.position)<=r)
                .OrderBy(c=>Vector3.Distance(p.transform.position,c.transform.position))
                .ToList();
        }

        private bool CanAccessContainer(Container c,long playerId)
        {
            if(!IsSafeStorage(c)) return false;
            try { return c.CheckAccess(playerId); }
            catch { return false; }
        }

        private void TryAutoCooking(Player p)
        {
            Inventory inv=p.GetInventory();
            if(inv==null) return;
            long playerId=p.GetPlayerID();
            List<Container> containers=GetNearbyContainers(p);
            float radius=Mathf.Max(1f,_cookingRadius.Value);
            List<CookingStation> stations=UnityEngine.Object.FindObjectsOfType<CookingStation>()
                .Where(s=>s!=null&&s.gameObject!=null&&Vector3.Distance(p.transform.position,s.transform.position)<=radius)
                .OrderBy(s=>Vector3.Distance(p.transform.position,s.transform.position))
                .ToList();

            foreach(CookingStation station in stations)
            {
                try { station.Interact(p,false,false); }
                catch(Exception e) { Logger.LogDebug("Auto-cooking collect skipped: "+e.Message); }

                if(station.m_conversion==null||station.m_conversion.Count==0) continue;
                foreach(CookingStation.ItemConversion conversion in station.m_conversion)
                {
                    if(conversion==null||conversion.m_from==null||conversion.m_from.m_itemData==null||conversion.m_from.m_itemData.m_shared==null) continue;
                    string rawName=conversion.m_from.m_itemData.m_shared.m_name;
                    ItemDrop.ItemData raw=FindInventoryItem(inv,rawName);
                    if(raw==null)
                    {
                        PullOneItemFromContainers(inv,containers,playerId,rawName);
                        raw=FindInventoryItem(inv,rawName);
                    }
                    if(raw==null) continue;
                    try { if(station.UseItem(p,raw)) break; }
                    catch(Exception e) { Logger.LogDebug("Auto-cooking load skipped: "+e.Message); }
                }
            }
        }

        private static ItemDrop.ItemData FindInventoryItem(Inventory inv,string sharedName)
        {
            if(inv==null||string.IsNullOrEmpty(sharedName)) return null;
            return inv.GetAllItems().FirstOrDefault(i=>i!=null&&i.m_shared!=null&&i.m_shared.m_name==sharedName);
        }

        private void PullOneItemFromContainers(Inventory target,List<Container> containers,long playerId,string sharedName)
        {
            if(target==null||containers==null||string.IsNullOrEmpty(sharedName)) return;
            foreach(Container c in containers)
            {
                if(!CanAccessContainer(c,playerId)) continue;
                Inventory src=c.GetInventory();
                if(src==null) continue;
                ItemDrop.ItemData item=src.GetAllItems().FirstOrDefault(i=>i!=null&&i.m_shared!=null&&i.m_shared.m_name==sharedName&&i.m_stack>0);
                if(item==null) continue;
                ItemDrop.ItemData copy=item.Clone();
                copy.m_stack=1;
                if(!target.CanAddItem(copy,1)) return;
                if(target.AddItem(copy))
                {
                    src.RemoveItem(item,1);
                    return;
                }
            }
        }

        private void TryRefill(Player p)
        {
            Inventory target=p.GetInventory();
            if(target==null) return;
            long id=p.GetPlayerID();
            List<Container> cs=GetNearbyContainers(p);
            List<ItemDrop.ItemData> foods=new List<ItemDrop.ItemData>();
            foreach(Container c in cs)
            {
                if(!CanAccessContainer(c,id)) continue;
                Inventory src=c.GetInventory();
                if(src!=null) foods.AddRange(src.GetAllItems().Where(IsFood));
            }

            int types=Mathf.Clamp(_foodTypesToStock.Value,1,3);
            List<string> names=foods.GroupBy(x=>x.m_shared.m_name)
                .Select(g=>g.OrderByDescending(BaseSelectionScore).First())
                .OrderByDescending(BaseSelectionScore)
                .Take(types)
                .Select(x=>x.m_shared.m_name)
                .ToList();

            foreach(Player.Food a in p.GetFoods())
                if(a!=null&&a.m_item!=null&&a.m_item.m_shared!=null&&!names.Contains(a.m_item.m_shared.m_name)) names.Insert(0,a.m_item.m_shared.m_name);
            names=names.Distinct().Take(types).ToList();

            foreach(string name in names)
            {
                int need=10-CountFood(target,name);
                if(need<=0) continue;
                foreach(Container c in cs)
                {
                    if(need<=0) break;
                    if(!CanAccessContainer(c,id)) continue;
                    Inventory src=c.GetInventory();
                    if(src==null) continue;
                    foreach(ItemDrop.ItemData item in new List<ItemDrop.ItemData>(src.GetAllItems()))
                    {
                        if(need<=0) break;
                        if(!IsFood(item)||item.m_shared.m_name!=name) continue;
                        int amount=Math.Min(need,item.m_stack);
                        ItemDrop.ItemData copy=item.Clone();
                        copy.m_stack=amount;
                        if(!target.CanAddItem(copy,amount)) return;
                        if(target.AddItem(copy))
                        {
                            src.RemoveItem(item,amount);
                            need-=amount;
                        }
                    }
                }
            }
        }

        private static void PutSelectedFoodOnHotbar(Inventory inv)
        {
            if(inv==null) return;
            List<ItemDrop.ItemData> foods=inv.GetAllItems().Where(IsFood).OrderByDescending(BaseSelectionScore).Take(3).ToList();
            foreach(ItemDrop.ItemData food in foods)
            {
                if(food.m_gridPos.y==0) continue;
                for(int x=0;x<8;x++)
                {
                    if(inv.GetItemAt(x,0)!=null) continue;
                    food.m_gridPos=new Vector2i(x,0);
                    inv.Changed();
                    break;
                }
            }
        }

        private static int CountFood(Inventory i,string n)
        {
            int c=0;
            foreach(ItemDrop.ItemData x in i.GetAllItems()) if(x!=null&&x.m_shared!=null&&x.m_shared.m_name==n)c+=x.m_stack;
            return c;
        }

        private static bool IsSafeStorage(Container c)
        {
            string n=c.gameObject.name??"";
            return n.IndexOf("tombstone",StringComparison.OrdinalIgnoreCase)<0&&n.IndexOf("grave",StringComparison.OrdinalIgnoreCase)<0&&n.IndexOf("cart",StringComparison.OrdinalIgnoreCase)<0&&n.IndexOf("wagon",StringComparison.OrdinalIgnoreCase)<0&&n.IndexOf("ship",StringComparison.OrdinalIgnoreCase)<0;
        }

        private void TryHarvest(Player p)
        {
            Inventory inv=p.GetInventory();
            if(inv==null) return;
            float r=Mathf.Max(.5f,_harvestRadius.Value);
            foreach(Pickable x in UnityEngine.Object.FindObjectsOfType<Pickable>())
            {
                if(x==null||x.gameObject==null||Vector3.Distance(p.transform.position,x.transform.position)>r||!x.CanBePicked()) continue;
                GameObject prefab=x.m_itemPrefab;
                if(prefab==null) continue;
                ItemDrop d=prefab.GetComponent<ItemDrop>();
                if(d==null||!IsFood(d.m_itemData)||!inv.CanAddItem(prefab,1)) continue;
                try { x.Interact(p,false,false); }
                catch(Exception e) { Logger.LogDebug("Auto-harvest failed: "+e.Message); }
            }
        }
    }
}
