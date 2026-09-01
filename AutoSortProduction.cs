using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ValheimAutoSortProduction
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class AutoSortProductionPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "de.valheim.autosortproduction";
        public const string PluginName = "AutoSort Production";
        public const string PluginVersion = "0.1.0";

        internal static AutoSortProductionPlugin Instance = null!;
        internal static ManualLogSource Log = null!;

        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<float> StorageRadius = null!;
        internal static ConfigEntry<float> PickupDelaySeconds = null!;
        internal static ConfigEntry<float> CaptureRadius = null!;
        internal static ConfigEntry<string> SupportedStations = null!;
        internal static ConfigEntry<string> SupportedContainers = null!;
        internal static ConfigEntry<bool> RequireMatchingItem = null!;
        internal static ConfigEntry<bool> PreferPartialStacks = null!;
        internal static ConfigEntry<bool> RestrictToSameCreator = null!;
        internal static ConfigEntry<bool> DebugLogging = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Master switch for the mod.");
            StorageRadius = Config.Bind("General", "StorageRadius", 100f,
                "Radius in metres around the producing station in which chests are searched.");
            PickupDelaySeconds = Config.Bind("General", "PickupDelaySeconds", 5f,
                "How long a production output must lie in the world before AutoSort attempts to store it.");
            CaptureRadius = Config.Bind("Advanced", "CaptureRadius", 8f,
                "Small radius used only to identify the world item that a production station just spawned.");
            RequireMatchingItem = Config.Bind("Sorting", "RequireMatchingItem", true,
                "When true, only chests that already contain this item are eligible. If none match, the item stays on the ground.");
            PreferPartialStacks = Config.Bind("Sorting", "PreferPartialStacks", true,
                "Prefer a chest where an existing stack of the item still has room, then use the nearest matching chest.");
            RestrictToSameCreator = Config.Bind("Multiplayer", "RestrictToSameCreator", true,
                "When true, only player-built chests with the same creator as the production station are used.");
            DebugLogging = Config.Bind("Advanced", "DebugLogging", false,
                "Write detailed capture and storage messages to the BepInEx log.");

            SupportedStations = Config.Bind("Compatibility", "SupportedStations",
                "smelter,charcoal_kiln,blastfurnace,windmill,eitrrefinery,piece_spinningwheel",
                "Comma-separated station prefab names whose Smelter.Spawn output is eligible.");
            SupportedContainers = Config.Bind("Compatibility", "SupportedContainers",
                "piece_chest_wood,piece_chest,piece_chest_blackmetal,piece_chest_private,piece_chest_barrel",
                "Comma-separated player storage prefab names that may receive production output.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Radius={StorageRadius.Value}m Delay={PickupDelaySeconds.Value}s");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        internal static bool IsSupportedStation(Smelter smelter)
        {
            if (smelter == null) return false;
            var name = CleanPrefabName(smelter.gameObject.name);
            return CsvSet(SupportedStations.Value).Contains(name);
        }

        internal static bool IsSupportedContainer(Container container)
        {
            if (container == null) return false;
            var piece = container.GetComponentInParent<Piece>();
            if (piece == null) return false;
            var name = CleanPrefabName(piece.gameObject.name);
            return CsvSet(SupportedContainers.Value).Contains(name);
        }

        internal static HashSet<string> CsvSet(string csv) =>
            new HashSet<string>(
                (csv ?? string.Empty).Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);

        internal static string CleanPrefabName(string name) =>
            (name ?? string.Empty).Replace("(Clone)", string.Empty).Trim();

        internal static void Debug(string message)
        {
            if (DebugLogging.Value) Log.LogInfo(message);
        }

        internal void StartCapture(SpawnCaptureState state)
        {
            StartCoroutine(CaptureSpawnedOutput(state));
        }

        private IEnumerator CaptureSpawnedOutput(SpawnCaptureState state)
        {
            var deadline = Time.time + 1.0f;
            while (Time.time < deadline)
            {
                var candidates = UnityEngine.Object.FindObjectsOfType<ItemDrop>()
                    .Where(x => x != null && x.gameObject != null)
                    .Where(x => !state.ExistingInstanceIds.Contains(x.GetInstanceID()))
                    .Where(x => MatchesOutputPrefab(x, state.OutputPrefabName))
                    .Where(x => Vector3.Distance(x.transform.position, state.SourcePosition) <= CaptureRadius.Value)
                    .OrderBy(x => Vector3.SqrMagnitude(x.transform.position - state.SourcePosition))
                    .ToList();

                var capturedStack = 0;
                foreach (var itemDrop in candidates)
                {
                    if (itemDrop.GetComponent<ProductionOutputTag>() != null) continue;

                    var tag = itemDrop.gameObject.AddComponent<ProductionOutputTag>();
                    tag.Initialize(state.SourcePosition, state.CreatorId, Time.time + PickupDelaySeconds.Value);
                    capturedStack += Math.Max(1, itemDrop.m_itemData?.m_stack ?? 1);

                    Debug($"Captured production output {state.OutputPrefabName} stack={itemDrop.m_itemData?.m_stack ?? 1} from {state.StationPrefabName}");
                    if (capturedStack >= state.ExpectedStack) yield break;
                }

                if (capturedStack > 0) yield break;
                yield return null;
            }

            Debug($"Could not identify spawned world item for {state.OutputPrefabName} from {state.StationPrefabName}.");
        }

        private static bool MatchesOutputPrefab(ItemDrop drop, string prefabName)
        {
            if (drop == null) return false;
            if (drop.m_itemData?.m_dropPrefab != null)
                return string.Equals(drop.m_itemData.m_dropPrefab.name, prefabName, StringComparison.OrdinalIgnoreCase);

            return string.Equals(CleanPrefabName(drop.gameObject.name), prefabName, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class SpawnCaptureState
    {
        internal string StationPrefabName = string.Empty;
        internal string OutputPrefabName = string.Empty;
        internal int ExpectedStack;
        internal Vector3 SourcePosition;
        internal long CreatorId;
        internal HashSet<int> ExistingInstanceIds = new HashSet<int>();
    }

    [HarmonyPatch(typeof(Smelter), "Spawn")]
    internal static class SmelterSpawnPatch
    {
        private static void Prefix(Smelter __instance, string ore, int stack, out SpawnCaptureState? __state)
        {
            __state = null;
            if (!AutoSortProductionPlugin.Enabled.Value || __instance == null) return;
            if (!AutoSortProductionPlugin.IsSupportedStation(__instance)) return;

            var conversion = __instance.m_conversion?
                .FirstOrDefault(c => c?.m_from != null && c.m_to != null &&
                                     string.Equals(c.m_from.gameObject.name, ore, StringComparison.OrdinalIgnoreCase));
            if (conversion?.m_to == null) return;

            var outputPrefabName = conversion.m_to.gameObject.name;
            var sourcePosition = __instance.transform.position;
            var piece = __instance.GetComponent<Piece>();
            var creator = piece != null ? piece.GetCreator() : 0L;

            var existing = UnityEngine.Object.FindObjectsOfType<ItemDrop>()
                .Where(x => x != null && x.gameObject != null)
                .Where(x => Vector3.Distance(x.transform.position, sourcePosition) <= AutoSortProductionPlugin.CaptureRadius.Value)
                .Where(x => x.m_itemData?.m_dropPrefab != null &&
                            string.Equals(x.m_itemData.m_dropPrefab.name, outputPrefabName, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.GetInstanceID());

            var existingSet = new HashSet<int>(existing);

            __state = new SpawnCaptureState
            {
                StationPrefabName = AutoSortProductionPlugin.CleanPrefabName(__instance.gameObject.name),
                OutputPrefabName = outputPrefabName,
                ExpectedStack = Math.Max(1, stack),
                SourcePosition = sourcePosition,
                CreatorId = creator,
                ExistingInstanceIds = existingSet
            };
        }

        private static void Postfix(SpawnCaptureState? __state)
        {
            if (__state == null || AutoSortProductionPlugin.Instance == null) return;
            AutoSortProductionPlugin.Instance.StartCapture(__state);
        }
    }

    internal sealed class ProductionOutputTag : MonoBehaviour
    {
        private Vector3 _sourcePosition;
        private long _creatorId;
        private float _eligibleAt;
        private bool _attempting;

        internal void Initialize(Vector3 sourcePosition, long creatorId, float eligibleAt)
        {
            _sourcePosition = sourcePosition;
            _creatorId = creatorId;
            _eligibleAt = eligibleAt;
        }

        private void Update()
        {
            if (_attempting || Time.time < _eligibleAt) return;
            _attempting = true;
            StartCoroutine(TryStoreAfterPhysicsSettles());
        }

        private IEnumerator TryStoreAfterPhysicsSettles()
        {
            yield return null;

            var drop = GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null)
            {
                Destroy(this);
                yield break;
            }

            var itemNView = GetComponent<ZNetView>();
            if (itemNView != null && itemNView.IsValid() && !itemNView.IsOwner())
            {
                _attempting = false;
                yield break;
            }

            if (StorageRouter.TryStore(drop, _sourcePosition, _creatorId))
            {
                AutoSortProductionPlugin.Debug($"Stored {drop.m_itemData.m_stack}x {drop.m_itemData.m_shared.m_name}");

                if (itemNView != null && itemNView.IsValid() && ZNetScene.instance != null)
                    ZNetScene.instance.Destroy(gameObject);
                else
                    Destroy(gameObject);
            }
            else
            {
                _eligibleAt = Time.time + 5f;
                _attempting = false;
            }
        }
    }

    internal static class StorageRouter
    {
        private static readonly MethodInfo? ContainerSave = AccessTools.Method(typeof(Container), "Save");

        internal static bool TryStore(ItemDrop drop, Vector3 sourcePosition, long creatorId)
        {
            if (drop == null || drop.m_itemData == null) return false;

            var item = drop.m_itemData;
            var containers = UnityEngine.Object.FindObjectsOfType<Container>()
                .Where(AutoSortProductionPlugin.IsSupportedContainer)
                .Where(c => Vector3.Distance(c.transform.position, sourcePosition) <= AutoSortProductionPlugin.StorageRadius.Value)
                .Where(c => CreatorAllowed(c, creatorId))
                .Where(c => ContainerAvailable(c))
                .Select(c => new Candidate(c, item, sourcePosition))
                .Where(c => c.Inventory != null)
                .Where(c => !AutoSortProductionPlugin.RequireMatchingItem.Value || c.HasMatchingItem)
                .Where(c => c.CanAccept)
                .OrderByDescending(c => AutoSortProductionPlugin.PreferPartialStacks.Value && c.HasPartialStack)
                .ThenBy(c => c.DistanceSquared)
                .ToList();

            foreach (var candidate in containers)
            {
                var clone = item.Clone();
                clone.m_stack = item.m_stack;
                if (!candidate.Inventory.CanAddItem(clone, clone.m_stack)) continue;
                if (!candidate.Inventory.AddItem(clone)) continue;

                Persist(candidate.Container);
                return true;
            }

            return false;
        }

        private static bool CreatorAllowed(Container container, long creatorId)
        {
            if (!AutoSortProductionPlugin.RestrictToSameCreator.Value) return true;
            if (creatorId == 0L) return false;
            var piece = container.GetComponentInParent<Piece>();
            return piece != null && piece.GetCreator() == creatorId;
        }

        private static bool ContainerAvailable(Container container)
        {
            if (container == null || container.GetInventory() == null) return false;

            try
            {
                var nview = container.GetComponent<ZNetView>() ?? container.GetComponentInParent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    var zdo = nview.GetZDO();
                    if (zdo != null && zdo.GetInt("InUse", 0) != 0) return false;
                }
            }
            catch
            {
            }

            return true;
        }

        private static void Persist(Container container)
        {
            try
            {
                ContainerSave?.Invoke(container, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                AutoSortProductionPlugin.Log.LogWarning($"Could not explicitly save container: {ex.Message}");
            }
        }

        private sealed class Candidate
        {
            internal readonly Container Container;
            internal readonly Inventory Inventory;
            internal readonly bool HasMatchingItem;
            internal readonly bool HasPartialStack;
            internal readonly bool CanAccept;
            internal readonly float DistanceSquared;

            internal Candidate(Container container, ItemDrop.ItemData item, Vector3 sourcePosition)
            {
                Container = container;
                Inventory = container.GetInventory();
                DistanceSquared = Vector3.SqrMagnitude(container.transform.position - sourcePosition);

                var same = Inventory.GetAllItems()
                    .Where(i => i != null && SameItem(i, item))
                    .ToList();

                HasMatchingItem = same.Count > 0;
                HasPartialStack = same.Any(i => i.m_stack < i.m_shared.m_maxStackSize);

                var probe = item.Clone();
                probe.m_stack = item.m_stack;
                CanAccept = Inventory.CanAddItem(probe, probe.m_stack);
            }

            private static bool SameItem(ItemDrop.ItemData a, ItemDrop.ItemData b)
            {
                if (a.m_dropPrefab != null && b.m_dropPrefab != null)
                    return string.Equals(a.m_dropPrefab.name, b.m_dropPrefab.name, StringComparison.OrdinalIgnoreCase);
                return string.Equals(a.m_shared?.m_name, b.m_shared?.m_name, StringComparison.Ordinal);
            }
        }
    }
}
