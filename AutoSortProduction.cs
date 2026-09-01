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
            Enabled = Config.Bind("General", "Enabled", true, "Master switch for the mod.");
            StorageRadius = Config.Bind("General", "StorageRadius", 100f, "Chest search radius in metres.");
            PickupDelaySeconds = Config.Bind("General", "PickupDelaySeconds", 20f, "Seconds output stays on ground before sorting.");
            CaptureRadius = Config.Bind("Advanced", "CaptureRadius", 8f, "Radius used to identify freshly spawned production output.");
            RequireMatchingItem = Config.Bind("Sorting", "RequireMatchingItem", true, "Only use chests already containing the item.");
            PreferPartialStacks = Config.Bind("Sorting", "PreferPartialStacks", true, "Prefer chests with a partial matching stack.");
            RestrictToSameCreator = Config.Bind("Multiplayer", "RestrictToSameCreator", true, "Only route to chests with the same creator as the station.");
            DebugLogging = Config.Bind("Advanced", "DebugLogging", false, "Enable detailed logging.");
            SupportedStations = Config.Bind("Compatibility", "SupportedStations", "smelter,charcoal_kiln,blastfurnace,windmill,eitrrefinery,piece_spinningwheel", "Supported station prefab names.");
            SupportedContainers = Config.Bind("Compatibility", "SupportedContainers", "piece_chest_wood,piece_chest,piece_chest_blackmetal,piece_chest_private,piece_chest_barrel", "Supported chest prefab names.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Radius={StorageRadius.Value}m Delay={PickupDelaySeconds.Value}s");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        internal static HashSet<string> CsvSet(string csv) => new HashSet<string>((csv ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
        internal static string CleanPrefabName(string name) => (name ?? "").Replace("(Clone)", "").Trim();
        internal static bool IsSupportedStation(Smelter smelter) => smelter != null && CsvSet(SupportedStations.Value).Contains(CleanPrefabName(smelter.gameObject.name));

        internal static bool IsSupportedContainer(Container container)
        {
            if (container == null) return false;
            Piece piece = container.GetComponentInParent<Piece>();
            return piece != null && CsvSet(SupportedContainers.Value).Contains(CleanPrefabName(piece.gameObject.name));
        }

        internal static void Debug(string message)
        {
            if (DebugLogging.Value) Log.LogInfo(message);
        }

        internal void StartCapture(SpawnCaptureState state) => StartCoroutine(CaptureSpawnedOutput(state));

        private IEnumerator CaptureSpawnedOutput(SpawnCaptureState state)
        {
            float deadline = Time.time + 1f;
            while (Time.time < deadline)
            {
                List<ItemDrop> candidates = UnityEngine.Object.FindObjectsOfType<ItemDrop>()
                    .Where(x => x != null && !state.ExistingInstanceIds.Contains(x.GetInstanceID()))
                    .Where(x => MatchesOutputPrefab(x, state.OutputPrefabName))
                    .Where(x => Vector3.Distance(x.transform.position, state.SourcePosition) <= CaptureRadius.Value)
                    .OrderBy(x => Vector3.SqrMagnitude(x.transform.position - state.SourcePosition))
                    .ToList();

                int capturedStack = 0;
                foreach (ItemDrop itemDrop in candidates)
                {
                    if (itemDrop.GetComponent<ProductionOutputTag>() != null) continue;
                    ProductionOutputTag tag = itemDrop.gameObject.AddComponent<ProductionOutputTag>();
                    tag.Initialize(state.SourcePosition, state.CreatorId, Time.time + PickupDelaySeconds.Value);
                    capturedStack += Math.Max(1, itemDrop.m_itemData != null ? itemDrop.m_itemData.m_stack : 1);
                    Debug($"Captured {state.OutputPrefabName} from {state.StationPrefabName}");
                    if (capturedStack >= state.ExpectedStack) yield break;
                }

                if (capturedStack > 0) yield break;
                yield return null;
            }
        }

        private static bool MatchesOutputPrefab(ItemDrop drop, string prefabName)
        {
            if (drop == null) return false;
            if (drop.m_itemData != null && drop.m_itemData.m_dropPrefab != null)
                return string.Equals(drop.m_itemData.m_dropPrefab.name, prefabName, StringComparison.OrdinalIgnoreCase);
            return string.Equals(CleanPrefabName(drop.gameObject.name), prefabName, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class SpawnCaptureState
    {
        internal string StationPrefabName = "";
        internal string OutputPrefabName = "";
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
            if (!AutoSortProductionPlugin.Enabled.Value || __instance == null || !AutoSortProductionPlugin.IsSupportedStation(__instance)) return;

            Smelter.ItemConversion conversion = __instance.m_conversion.FirstOrDefault(c => c != null && c.m_from != null && c.m_to != null && string.Equals(c.m_from.gameObject.name, ore, StringComparison.OrdinalIgnoreCase));
            if (conversion == null || conversion.m_to == null) return;

            string outputPrefabName = conversion.m_to.gameObject.name;
            Vector3 sourcePosition = __instance.transform.position;
            Piece piece = __instance.GetComponent<Piece>();
            long creator = piece != null ? piece.GetCreator() : 0L;

            HashSet<int> existing = new HashSet<int>(UnityEngine.Object.FindObjectsOfType<ItemDrop>()
                .Where(x => x != null && Vector3.Distance(x.transform.position, sourcePosition) <= AutoSortProductionPlugin.CaptureRadius.Value)
                .Where(x => x.m_itemData != null && x.m_itemData.m_dropPrefab != null && string.Equals(x.m_itemData.m_dropPrefab.name, outputPrefabName, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.GetInstanceID()));

            __state = new SpawnCaptureState
            {
                StationPrefabName = AutoSortProductionPlugin.CleanPrefabName(__instance.gameObject.name),
                OutputPrefabName = outputPrefabName,
                ExpectedStack = Math.Max(1, stack),
                SourcePosition = sourcePosition,
                CreatorId = creator,
                ExistingInstanceIds = existing
            };
        }

        private static void Postfix(SpawnCaptureState? __state)
        {
            if (__state != null && AutoSortProductionPlugin.Instance != null)
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
            StartCoroutine(TryStore());
        }

        private IEnumerator TryStore()
        {
            yield return null;
            ItemDrop drop = GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null)
            {
                Destroy(this);
                yield break;
            }

            ZNetView nview = GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner())
            {
                _attempting = false;
                yield break;
            }

            if (StorageRouter.TryStore(drop, _sourcePosition, _creatorId))
            {
                if (nview != null && nview.IsValid() && ZNetScene.instance != null) ZNetScene.instance.Destroy(gameObject);
                else Destroy(gameObject);
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
            ItemDrop.ItemData item = drop.m_itemData;
            List<Candidate> candidates = UnityEngine.Object.FindObjectsOfType<Container>()
                .Where(AutoSortProductionPlugin.IsSupportedContainer)
                .Where(c => Vector3.Distance(c.transform.position, sourcePosition) <= AutoSortProductionPlugin.StorageRadius.Value)
                .Where(c => CreatorAllowed(c, creatorId))
                .Select(c => new Candidate(c, item, sourcePosition))
                .Where(c => c.Inventory != null)
                .Where(c => !AutoSortProductionPlugin.RequireMatchingItem.Value || c.HasMatchingItem)
                .Where(c => c.CanAccept)
                .OrderByDescending(c => AutoSortProductionPlugin.PreferPartialStacks.Value && c.HasPartialStack)
                .ThenBy(c => c.DistanceSquared)
                .ToList();

            foreach (Candidate candidate in candidates)
            {
                ItemDrop.ItemData clone = item.Clone();
                clone.m_stack = item.m_stack;
                if (!candidate.Inventory.CanAddItem(clone, clone.m_stack)) continue;
                if (!candidate.Inventory.AddItem(clone)) continue;
                try { ContainerSave?.Invoke(candidate.Container, Array.Empty<object>()); } catch { }
                return true;
            }
            return false;
        }

        private static bool CreatorAllowed(Container container, long creatorId)
        {
            if (!AutoSortProductionPlugin.RestrictToSameCreator.Value) return true;
            if (creatorId == 0L) return false;
            Piece piece = container.GetComponentInParent<Piece>();
            return piece != null && piece.GetCreator() == creatorId;
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
                List<ItemDrop.ItemData> same = Inventory.GetAllItems().Where(i => i != null && SameItem(i, item)).ToList();
                HasMatchingItem = same.Count > 0;
                HasPartialStack = same.Any(i => i.m_stack < i.m_shared.m_maxStackSize);
                ItemDrop.ItemData probe = item.Clone();
                probe.m_stack = item.m_stack;
                CanAccept = Inventory.CanAddItem(probe, probe.m_stack);
            }

            private static bool SameItem(ItemDrop.ItemData a, ItemDrop.ItemData b)
            {
                if (a.m_dropPrefab != null && b.m_dropPrefab != null)
                    return string.Equals(a.m_dropPrefab.name, b.m_dropPrefab.name, StringComparison.OrdinalIgnoreCase);
                return string.Equals(a.m_shared.m_name, b.m_shared.m_name, StringComparison.Ordinal);
            }
        }
    }
}
