using System;
using System.Collections.Generic;
using System.Linq;
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
        public const string PluginVersion = "1.2.0";

        private ConfigEntry<bool> _autoEat;
        private ConfigEntry<bool> _autoRefill;
        private ConfigEntry<bool> _autoHarvest;
        private ConfigEntry<float> _baseRadius;
        private ConfigEntry<float> _containerRadius;
        private ConfigEntry<float> _harvestRadius;
        private ConfigEntry<int> _foodTypesToStock;
        private ConfigEntry<float> _eatInterval;
        private float _nextEat;
        private float _nextRefill;
        private float _nextHarvest;

        private void Awake()
        {
            _autoEat = Config.Bind("Auto eat", "Enabled", true, "Automatically eat only when Valheim has a free or refreshable food slot.");
            _eatInterval = Config.Bind("Auto eat", "CheckIntervalSeconds", 1.0f, "How often auto-eat checks food slots.");
            _autoRefill = Config.Bind("Base refill", "Enabled", true, "Refill edible food from nearby accessible containers while at base.");
            _baseRadius = Config.Bind("Base refill", "WorkbenchRadius", 20f, "Distance from a workbench that counts as base.");
            _containerRadius = Config.Bind("Base refill", "ContainerRadius", 20f, "Maximum distance to food containers.");
            _foodTypesToStock = Config.Bind("Base refill", "FoodTypesToStock", 3, "How many different food types to pull from nearby containers.");
            _autoHarvest = Config.Bind("Travel harvest", "Enabled", true, "Harvest only edible Pickable plants while outside base.");
            _harvestRadius = Config.Bind("Travel harvest", "Radius", 3.5f, "Maximum automatic edible-plant harvesting distance.");
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded");
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return;
            float now = Time.time;
            bool atBase = IsAtBase(player.transform.position);
            if (atBase && _autoRefill.Value && now >= _nextRefill) { _nextRefill = now + 4f; TryRefill(player); }
            if (_autoEat.Value && now >= _nextEat) { _nextEat = now + Mathf.Max(0.25f, _eatInterval.Value); TryAutoEat(player); }
            if (!atBase && _autoHarvest.Value && now >= _nextHarvest) { _nextHarvest = now + 0.75f; TryHarvest(player); }
        }

        private static bool IsFood(ItemDrop.ItemData item)
        {
            return item != null && item.m_shared != null && (item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f || item.m_shared.m_foodEitr > 0f);
        }

        private static float FoodScore(ItemDrop.ItemData item)
        {
            if (!IsFood(item)) return 0f;
            return item.m_shared.m_food + item.m_shared.m_foodStamina + item.m_shared.m_foodEitr;
        }

        private static bool CanEatSilently(Player player, ItemDrop.ItemData candidate)
        {
            if (!IsFood(candidate)) return false;
            List<Player.Food> activeFoods = player.GetFoods();
            if (activeFoods == null) return true;
            foreach (Player.Food active in activeFoods)
            {
                if (active == null || active.m_item == null || active.m_item.m_shared == null) continue;
                if (active.m_item.m_shared.m_name == candidate.m_shared.m_name) return active.CanEatAgain();
            }
            return activeFoods.Count < 3;
        }

        private void TryAutoEat(Player player)
        {
            Inventory inventory = player.GetInventory();
            if (inventory == null) return;
            List<ItemDrop.ItemData> foods = inventory.GetAllItems().Where(x => CanEatSilently(player, x)).OrderByDescending(FoodScore).ToList();
            foreach (ItemDrop.ItemData food in foods)
            {
                try { if (player.ConsumeItem(inventory, food, false)) return; }
                catch (Exception ex) { Logger.LogDebug("Auto-eat skipped item: " + ex.Message); }
            }
        }

        private bool IsAtBase(Vector3 position)
        {
            float radius = Mathf.Max(1f, _baseRadius.Value);
            foreach (CraftingStation station in UnityEngine.Object.FindObjectsOfType<CraftingStation>())
            {
                if (station == null || station.gameObject == null) continue;
                string objectName = station.gameObject.name ?? string.Empty;
                string stationName = station.m_name ?? string.Empty;
                bool workbench = objectName.IndexOf("workbench", StringComparison.OrdinalIgnoreCase) >= 0 || stationName.IndexOf("workbench", StringComparison.OrdinalIgnoreCase) >= 0 || stationName.IndexOf("$piece_workbench", StringComparison.OrdinalIgnoreCase) >= 0;
                if (workbench && Vector3.Distance(position, station.transform.position) <= radius) return true;
            }
            return false;
        }

        private void TryRefill(Player player)
        {
            Inventory target = player.GetInventory();
            if (target == null) return;
            long playerId = player.GetPlayerID();
            float radius = Mathf.Max(1f, _containerRadius.Value);
            List<Container> containers = UnityEngine.Object.FindObjectsOfType<Container>().Where(c => c != null && c.gameObject != null && Vector3.Distance(player.transform.position, c.transform.position) <= radius).OrderBy(c => Vector3.Distance(player.transform.position, c.transform.position)).ToList();
            List<ItemDrop.ItemData> availableFoods = new List<ItemDrop.ItemData>();
            foreach (Container container in containers)
            {
                if (!IsSafeStorage(container)) continue;
                bool access;
                try { access = container.CheckAccess(playerId); } catch { access = false; }
                if (!access) continue;
                Inventory source = container.GetInventory();
                if (source != null) availableFoods.AddRange(source.GetAllItems().Where(IsFood));
            }

            int typeCount = Mathf.Clamp(_foodTypesToStock.Value, 1, 3);
            List<string> selectedNames = availableFoods.GroupBy(x => x.m_shared.m_name).Select(g => g.OrderByDescending(FoodScore).First()).OrderByDescending(FoodScore).Take(typeCount).Select(x => x.m_shared.m_name).ToList();
            foreach (Player.Food active in player.GetFoods())
            {
                if (active != null && active.m_item != null && active.m_item.m_shared != null && !selectedNames.Contains(active.m_item.m_shared.m_name)) selectedNames.Insert(0, active.m_item.m_shared.m_name);
            }
            selectedNames = selectedNames.Distinct().Take(typeCount).ToList();

            foreach (string foodName in selectedNames)
            {
                ItemDrop.ItemData sample = availableFoods.FirstOrDefault(x => x.m_shared.m_name == foodName);
                if (sample == null) continue;
                int wanted = Mathf.Max(1, Mathf.CeilToInt(sample.m_shared.m_maxStack * 0.5f));
                int need = wanted - CountFood(target, foodName);
                if (need <= 0) continue;
                foreach (Container container in containers)
                {
                    if (need <= 0) break;
                    if (!IsSafeStorage(container)) continue;
                    bool access;
                    try { access = container.CheckAccess(playerId); } catch { access = false; }
                    if (!access) continue;
                    Inventory source = container.GetInventory();
                    if (source == null) continue;
                    List<ItemDrop.ItemData> sourceItems = new List<ItemDrop.ItemData>(source.GetAllItems());
                    foreach (ItemDrop.ItemData sourceItem in sourceItems)
                    {
                        if (need <= 0) break;
                        if (!IsFood(sourceItem) || sourceItem.m_shared.m_name != foodName) continue;
                        int amount = Math.Min(need, sourceItem.m_stack);
                        ItemDrop.ItemData copy = sourceItem.Clone();
                        copy.m_stack = amount;
                        if (!target.CanAddItem(copy, amount)) return;
                        if (target.AddItem(copy)) { source.RemoveItem(sourceItem, amount); need -= amount; }
                    }
                }
            }
        }

        private static int CountFood(Inventory inventory, string sharedName)
        {
            int count = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems()) if (item != null && item.m_shared != null && item.m_shared.m_name == sharedName) count += item.m_stack;
            return count;
        }

        private static bool IsSafeStorage(Container container)
        {
            string n = container.gameObject.name ?? string.Empty;
            return n.IndexOf("tombstone", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("grave", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("cart", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("wagon", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("ship", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private void TryHarvest(Player player)
        {
            Inventory inventory = player.GetInventory();
            if (inventory == null) return;
            float radius = Mathf.Max(0.5f, _harvestRadius.Value);
            foreach (Pickable pickable in UnityEngine.Object.FindObjectsOfType<Pickable>())
            {
                if (pickable == null || pickable.gameObject == null || Vector3.Distance(player.transform.position, pickable.transform.position) > radius || !pickable.CanBePicked()) continue;
                GameObject prefab = pickable.m_itemPrefab;
                if (prefab == null) continue;
                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                if (drop == null || !IsFood(drop.m_itemData) || !inventory.CanAddItem(prefab, 1)) continue;
                try { pickable.Interact(player, false, false); } catch (Exception ex) { Logger.LogDebug("Auto-harvest failed: " + ex.Message); }
            }
        }
    }
}
