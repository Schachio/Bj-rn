using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Schachio.HungerPangsPlus
{
    [BepInPlugin("schachio.hungerpangsplus.expandedautomation", "Hunger Pangs Plus Expanded Automation", "1.1.0")]
    [BepInDependency(HungerPangsPlusPlugin.PluginGuid)]
    public sealed class ExpandedAutomationPlugin : BaseUnityPlugin
    {
        private HungerPangsPlusPlugin _main;
        private FieldInfo _foodIconsField;
        private MethodInfo _pickupMethod;
        private float _nextFoodCheck;
        private float _nextGroundCheck;
        private readonly Dictionary<string, float> _pickedFoodReadyAt = new Dictionary<string, float>();

        private void Start()
        {
            _main = UnityEngine.Object.FindObjectOfType<HungerPangsPlusPlugin>();
            _foodIconsField = typeof(Hud).GetField("m_foodIcons", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _pickupMethod = typeof(ItemDrop).GetMethod("Pickup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(Humanoid) }, null);
            if (_pickupMethod == null)
                _pickupMethod = typeof(ItemDrop).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "Pickup" && m.GetParameters().Length == 1);
            ApplyRequestedDefaults();
        }

        private void ApplyRequestedDefaults()
        {
            if (_main == null) return;
            try
            {
                FieldInfo pauseField = typeof(HungerPangsPlusPlugin).GetField("_manualFoodPauseSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
                ConfigEntry<float> pause = pauseField != null ? pauseField.GetValue(_main) as ConfigEntry<float> : null;
                if (pause != null && Math.Abs(pause.Value - 3f) > 0.001f)
                    pause.Value = 3f;

                FieldInfo stockField = typeof(HungerPangsPlusPlugin).GetField("_foodTypesToStock", BindingFlags.Instance | BindingFlags.NonPublic);
                ConfigEntry<int> stock = stockField != null ? stockField.GetValue(_main) as ConfigEntry<int> : null;
                if (stock != null && stock.Value < 10)
                    stock.Value = 10;

                _main.Config.Save();
            }
            catch (Exception e)
            {
                Logger.LogDebug("Could not apply expanded defaults: " + e.Message);
            }
        }

        private void Update()
        {
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead()) return;

            float now = Time.time;
            if (now >= _nextGroundCheck)
            {
                _nextGroundCheck = now + 0.5f;
                TryPickupEdibleGroundItems(p);
            }

            if (now >= _nextFoodCheck)
            {
                _nextFoodCheck = now + 0.5f;
                TryFillDynamicFoodSlots(p);
            }
        }

        private int GetFoodSlotCapacity(Player p)
        {
            int capacity = 3;
            try
            {
                Hud hud = Hud.instance;
                if (hud != null && _foodIconsField != null)
                {
                    Array icons = _foodIconsField.GetValue(hud) as Array;
                    if (icons != null && icons.Length > 0)
                        capacity = icons.Length;
                }
            }
            catch { }

            try
            {
                List<Player.Food> foods = p.GetFoods();
                if (foods != null)
                    capacity = Math.Max(capacity, foods.Count);
            }
            catch { }

            return Mathf.Clamp(capacity, 1, 10);
        }

        private static bool IsFood(ItemDrop.ItemData item)
        {
            return item != null && item.m_shared != null &&
                   (item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f || item.m_shared.m_foodEitr > 0f);
        }

        private static float FoodScore(ItemDrop.ItemData item)
        {
            if (!IsFood(item)) return 0f;
            return item.m_shared.m_food + item.m_shared.m_foodStamina + item.m_shared.m_foodEitr;
        }

        private void TryFillDynamicFoodSlots(Player p)
        {
            int capacity = GetFoodSlotCapacity(p);
            List<Player.Food> active = p.GetFoods();
            if (active == null || active.Count >= capacity) return;

            Inventory inv = p.GetInventory();
            if (inv == null) return;

            PullExtraFoodFromNearbyContainers(p, capacity);

            int safety = 0;
            while (active.Count < capacity && safety++ < 10)
            {
                HashSet<string> activeNames = new HashSet<string>(active
                    .Where(f => f != null && f.m_item != null && f.m_item.m_shared != null)
                    .Select(f => f.m_item.m_shared.m_name));

                float now = Time.time;
                ItemDrop.ItemData candidate = inv.GetAllItems()
                    .Where(IsFood)
                    .Where(i => i.m_shared != null && !activeNames.Contains(i.m_shared.m_name))
                    .Where(i => !_pickedFoodReadyAt.ContainsKey(i.m_shared.m_name) || now >= _pickedFoodReadyAt[i.m_shared.m_name])
                    .OrderByDescending(FoodScore)
                    .FirstOrDefault();

                if (candidate == null) break;

                int before = active.Count;
                string name = candidate.m_shared.m_name;
                try { p.ConsumeItem(inv, candidate, false); }
                catch (Exception e) { Logger.LogDebug("Expanded auto-eat skipped: " + e.Message); break; }

                active = p.GetFoods();
                if (active == null || active.Count <= before) break;
                _pickedFoodReadyAt.Remove(name);
            }
        }

        private void PullExtraFoodFromNearbyContainers(Player p, int capacity)
        {
            Inventory inv = p.GetInventory();
            if (inv == null) return;

            List<Player.Food> active = p.GetFoods();
            HashSet<string> known = new HashSet<string>();
            if (active != null)
                foreach (Player.Food f in active)
                    if (f != null && f.m_item != null && f.m_item.m_shared != null)
                        known.Add(f.m_item.m_shared.m_name);
            foreach (ItemDrop.ItemData i in inv.GetAllItems())
                if (IsFood(i) && i.m_shared != null)
                    known.Add(i.m_shared.m_name);

            int needTypes = Math.Max(0, capacity - known.Count);
            if (needTypes <= 0) return;

            long id = p.GetPlayerID();
            List<Container> containers = GetNearbySafeContainers(p, 20f, id);
            List<ItemDrop.ItemData> options = new List<ItemDrop.ItemData>();
            foreach (Container c in containers)
            {
                Inventory src = c.GetInventory();
                if (src != null)
                    options.AddRange(src.GetAllItems().Where(IsFood));
            }

            foreach (ItemDrop.ItemData option in options
                .Where(i => i != null && i.m_shared != null && !known.Contains(i.m_shared.m_name))
                .GroupBy(i => i.m_shared.m_name)
                .Select(g => g.OrderByDescending(FoodScore).First())
                .OrderByDescending(FoodScore)
                .Take(needTypes)
                .ToList())
            {
                string name = option.m_shared.m_name;
                foreach (Container c in containers)
                {
                    Inventory src = c.GetInventory();
                    if (src == null) continue;
                    ItemDrop.ItemData source = src.GetAllItems().FirstOrDefault(i => i != null && i.m_shared != null && i.m_shared.m_name == name && i.m_stack > 0);
                    if (source == null) continue;
                    ItemDrop.ItemData copy = source.Clone();
                    copy.m_stack = 1;
                    if (!inv.CanAddItem(copy, 1)) return;
                    if (inv.AddItem(copy))
                    {
                        src.RemoveItem(source, 1);
                        known.Add(name);
                    }
                    break;
                }
            }
        }

        private void TryPickupEdibleGroundItems(Player p)
        {
            Inventory inv = p.GetInventory();
            if (inv == null || _pickupMethod == null) return;

            const float radius = 3.5f;
            List<ItemDrop> drops = UnityEngine.Object.FindObjectsOfType<ItemDrop>()
                .Where(d => d != null && d.gameObject != null && d.gameObject.activeInHierarchy && d.m_itemData != null)
                .Where(d => Vector3.Distance(p.transform.position, d.transform.position) <= radius)
                .Where(d => IsFood(d.m_itemData))
                .OrderBy(d => Vector3.Distance(p.transform.position, d.transform.position))
                .ToList();

            foreach (ItemDrop drop in drops)
            {
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;
                string sharedName = drop.m_itemData.m_shared.m_name;
                int before = CountBySharedName(inv, sharedName);

                try { _pickupMethod.Invoke(drop, new object[] { p }); }
                catch (Exception e) { Logger.LogDebug("Ground food pickup skipped: " + e.Message); continue; }

                int after = CountBySharedName(inv, sharedName);
                if (after > before)
                    _pickedFoodReadyAt[sharedName] = Time.time + 2f;
            }
        }

        private List<Container> GetNearbySafeContainers(Player p, float radius, long playerId)
        {
            return UnityEngine.Object.FindObjectsOfType<Container>()
                .Where(c => c != null && c.gameObject != null)
                .Where(c => Vector3.Distance(p.transform.position, c.transform.position) <= radius)
                .Where(IsSafeStorage)
                .Where(c => CanAccess(c, playerId))
                .OrderBy(c => Vector3.Distance(p.transform.position, c.transform.position))
                .ToList();
        }

        private static bool CanAccess(Container c, long playerId)
        {
            try { return c.CheckAccess(playerId); }
            catch { return false; }
        }

        private static bool IsSafeStorage(Container c)
        {
            string n = c.gameObject.name ?? "";
            return n.IndexOf("tombstone", StringComparison.OrdinalIgnoreCase) < 0 &&
                   n.IndexOf("grave", StringComparison.OrdinalIgnoreCase) < 0 &&
                   n.IndexOf("cart", StringComparison.OrdinalIgnoreCase) < 0 &&
                   n.IndexOf("wagon", StringComparison.OrdinalIgnoreCase) < 0 &&
                   n.IndexOf("ship", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static int CountBySharedName(Inventory inv, string name)
        {
            int count = 0;
            foreach (ItemDrop.ItemData i in inv.GetAllItems())
                if (i != null && i.m_shared != null && i.m_shared.m_name == name)
                    count += i.m_stack;
            return count;
        }
    }
}
