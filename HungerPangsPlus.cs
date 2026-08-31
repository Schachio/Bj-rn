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
        public const string PluginVersion = "1.0.0";

        private ConfigEntry<bool> _autoEat;
        private ConfigEntry<bool> _autoRefill;
        private ConfigEntry<bool> _autoHarvest;
        private ConfigEntry<float> _baseRadius;
        private ConfigEntry<float> _containerRadius;
        private ConfigEntry<float> _harvestRadius;
        private ConfigEntry<int> _targetPerFood;
        private ConfigEntry<float> _eatInterval;
        private float _nextEat;
        private float _nextRefill;
        private float _nextHarvest;

        private void Awake()
        {
            _autoEat = Config.Bind("Auto eat", "Enabled", true, "Automatically eat available food when a food slot can be refreshed.");
            _eatInterval = Config.Bind("Auto eat", "CheckIntervalSeconds", 1.0f, "How often auto-eat checks food slots.");
            _autoRefill = Config.Bind("Base refill", "Enabled", true, "Refill edible food from nearby accessible containers while at base.");
            _baseRadius = Config.Bind("Base refill", "WorkbenchRadius", 20f, "Distance from a workbench that counts as base.");
            _containerRadius = Config.Bind("Base refill", "ContainerRadius", 20f, "Maximum distance to food containers.");
            _targetPerFood = Config.Bind("Base refill", "TargetPerFood", 3, "Target amount of each food type already carried.");
            _autoHarvest = Config.Bind("Travel harvest", "Enabled", true, "Harvest only edible Pickable plants while outside base.");
            _harvestRadius = Config.Bind("Travel harvest", "Radius", 3.5f, "Maximum automatic edible-plant harvesting distance.");
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded");
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return;

            float now = Time.time;
            if (_autoEat.Value && now >= _nextEat)
            {
                _nextEat = now + Mathf.Max(0.25f, _eatInterval.Value);
                TryAutoEat(player);
            }

            bool atBase = IsAtBase(player.transform.position);
            if (atBase)
            {
                if (_autoRefill.Value && now >= _nextRefill)
                {
                    _nextRefill = now + 4f;
                    TryRefill(player);
                }
            }
            else if (_autoHarvest.Value && now >= _nextHarvest)
            {
                _nextHarvest = now + 0.75f;
                TryHarvest(player);
            }
        }

        private static bool IsFood(ItemDrop.ItemData item)
        {
            return item != null && item.m_shared != null &&
                   (item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f || item.m_shared.m_foodEitr > 0f);
        }

        private void TryAutoEat(Player player)
        {
            Inventory inventory = player.GetInventory();
            if (inventory == null) return;

            List<ItemDrop.ItemData> foods = inventory.GetAllItems()
                .Where(IsFood)
                .OrderByDescending(FoodScore)
                .ToList();

            foreach (ItemDrop.ItemData food in foods)
            {
                try
                {
                    if (player.ConsumeItem(inventory, food, false)) return;
                }
                catch
                {
                    // A food that cannot currently be consumed is simply skipped.
                }
            }
        }

        private static float FoodScore(ItemDrop.ItemData item)
        {
            if (!IsFood(item)) return 0f;
            return item.m_shared.m_food + item.m_shared.m_foodStamina + item.m_shared.m_foodEitr;
        }

        private bool IsAtBase(Vector3 position)
        {
            float radius = Mathf.Max(1f, _baseRadius.Value);
            foreach (CraftingStation station in UnityEngine.Object.FindObjectsOfType<CraftingStation>())
            {
                if (station == null || station.gameObject == null) continue;
                string objectName = station.gameObject.name ?? string.Empty;
                string stationName = station.m_name ?? string.Empty;
                bool workbench = objectName.IndexOf("workbench", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 stationName.IndexOf("workbench", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 stationName.IndexOf("$piece_workbench", StringComparison.OrdinalIgnoreCase) >= 0;
                if (workbench && Vector3.Distance(position, station.transform.position) <= radius) return true;
            }
            return false;
        }

        private void TryRefill(Player player)
        {
            Inventory target = player.GetInventory();
            if (target == null) return;

            List<string> carriedFoodNames = target.GetAllItems()
                .Where(IsFood)
                .Select(x => x.m_shared.m_name)
                .Distinct()
                .ToList();
            if (carriedFoodNames.Count == 0) return;

            long playerId = player.GetPlayerID();
            float radius = Mathf.Max(1f, _containerRadius.Value);
            List<Container> containers = UnityEngine.Object.FindObjectsOfType<Container>()
                .Where(c => c != null && c.gameObject != null && Vector3.Distance(player.transform.position, c.transform.position) <= radius)
                .OrderBy(c => Vector3.Distance(player.transform.position, c.transform.position))
                .ToList();

            foreach (string foodName in carriedFoodNames)
            {
                int wanted = Mathf.Clamp(_targetPerFood.Value, 1, 50);
                int need = wanted - CountFood(target, foodName);
                if (need <= 0) continue;

                foreach (Container container in containers)
                {
                    if (need <= 0) break;
                    if (!IsSafeStorage(container)) continue;

                    bool access;
                    try { access = container.CheckAccess(playerId); }
                    catch { access = false; }
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

                        if (target.AddItem(copy))
                        {
                            source.RemoveItem(sourceItem, amount);
                            need -= amount;
                        }
                    }
                }
            }
        }

        private static int CountFood(Inventory inventory, string sharedName)
        {
            int count = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item != null && item.m_shared != null && item.m_shared.m_name == sharedName) count += item.m_stack;
            }
            return count;
        }

        private static bool IsSafeStorage(Container container)
        {
            string n = container.gameObject.name ?? string.Empty;
            return n.IndexOf("tombstone", StringComparison.OrdinalIgnoreCase) < 0 &&
                   n.IndexOf("grave", StringComparison.OrdinalIgnoreCase) < 0 &&
                   n.IndexOf("cart", StringComparison.OrdinalIgnoreCase) < 0 &&
                   n.IndexOf("wagon", StringComparison.OrdinalIgnoreCase) < 0 &&
                   n.IndexOf("ship", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private void TryHarvest(Player player)
        {
            Inventory inventory = player.GetInventory();
            if (inventory == null) return;
            float radius = Mathf.Max(0.5f, _harvestRadius.Value);

            foreach (Pickable pickable in UnityEngine.Object.FindObjectsOfType<Pickable>())
            {
                if (pickable == null || pickable.gameObject == null) continue;
                if (Vector3.Distance(player.transform.position, pickable.transform.position) > radius) continue;
                if (!pickable.CanBePicked()) continue;

                GameObject prefab = pickable.m_itemPrefab;
                if (prefab == null) continue;
                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                if (drop == null || !IsFood(drop.m_itemData)) continue;
                if (!inventory.CanAddItem(prefab, 1)) continue;

                try { pickable.Interact(player, false, false); }
                catch (Exception ex) { Logger.LogDebug("Auto-harvest failed: " + ex.Message); }
            }
        }
    }
}
