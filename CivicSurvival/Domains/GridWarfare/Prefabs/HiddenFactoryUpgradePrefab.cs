using System.Collections.Generic;
using Game.Prefabs;
using UnityEngine;

namespace CivicSurvival.Domains.GridWarfare.Prefabs
{
    /// <summary>
    /// SPIKE (Phase 3.0b §B.0, GO/NO-GO): proves the mod can BUILD a vanilla in-place
    /// <see cref="ServiceUpgrade"/> prefab in code and have it surface in a building's
    /// vanilla upgrade panel — the one unproven step of the hybrid entry design (every
    /// prefab in the mod so far is a <c>.cok</c> from the Asset Editor; nothing was ever
    /// assembled programmatically). This is the spike's only deliverable: the prefab object
    /// + its building binding. Detector / sidecar / production / reveal are phases B.1–B.6.
    ///
    /// WHY THIS SHAPE (decompile-grounded, CS2 1.5.5):
    /// - The carrier is a plain <see cref="ObjectPrefab"/>, NOT a <c>BuildingPrefab</c> /
    ///   <c>BuildingExtensionPrefab</c>. Two reasons, both load-bearing:
    ///   1. <c>UpgradeToolSystem.TrySetPrefab</c> (Game.Tools/UpgradeToolSystem.cs:143)
    ///      accepts the prefab only if it <c>is ObjectPrefab</c> AND <c>Has&lt;ServiceUpgrade&gt;()</c>
    ///      AND does NOT have <c>PlaceableObjectData</c>. <c>BuildingPrefab.GetPrefabComponents</c>
    ///      adds <c>PlaceableObjectData</c> → the tool would reject it. A bare ObjectPrefab
    ///      stays in-place: no model, no lot, the tool uses the target building's own
    ///      Transform as the control point (UpgradeToolSystem.cs:255-271).
    ///   2. <c>ServiceUpgrade.GetPrefabComponents</c> (ServiceUpgrade.cs:45) adds
    ///      <c>PlaceableObjectData</c>/<c>PlaceableInfoviewItem</c> only when the prefab also
    ///      carries a <c>BuildingPrefab</c> component — which it does not here.
    /// - A <see cref="UIObject"/> component is REQUIRED, not cosmetic. Both the building's
    ///   own panel (<c>UpgradesSection.Visible</c> / <c>OnProcess</c>, UpgradesSection.cs:120,154)
    ///   and the upgrade menu (<c>UpgradeMenuUISystem.BindUpgrades</c>, line 293) filter the
    ///   <c>BuildingUpgradeElement</c> buffer by <c>HasComponent&lt;UIObjectData&gt;</c> on the
    ///   upgrade entity. Without UIObject → no UIObjectData → the upgrade never shows.
    ///   <c>m_Group</c> is left null on purpose: <c>UIObject.LateInitialize</c> (UIObject.cs:73)
    ///   tolerates a null group (writes <c>m_Group = Entity.Null</c>) — it just isn't added to
    ///   any toolbar menu, which is exactly what the hybrid entry wants (entry is the building
    ///   panel, not a toolbar).
    /// - <c>m_UpgradeCost = 0</c>: the design takes Shadow Cash separately in the detector
    ///   (B.2). Vanilla money is not charged.
    /// - <c>m_ForbidMultiple = true</c>: one hidden-factory upgrade per building.
    /// - <c>GetUpgradeComponents</c> is empty (ServiceUpgrade adds nothing to the archetype set
    ///   beyond the vanilla <c>ServiceUpgradeBuilding</c>/<c>ServiceUpgradeData</c> it always
    ///   carries on the upgrade prefab itself). The upgrade does not change the building's
    ///   function — its only trace on the building is the vanilla <c>InstalledUpgrade</c> entry
    ///   our type, which the B.2 detector reads (read-only, same as PowerCapacityClassifier).
    ///
    /// REGISTRATION: the carrier is created here, then <c>PrefabSystem.AddPrefab</c> registers
    /// it. AddPrefab only creates the prefab entity + Created tag; it does NOT run
    /// Initialize/LateInitialize. Vanilla <c>PrefabInitializeSystem</c> picks up the Created
    /// entity on its next tick and runs every component's Initialize → LateInitialize. That is
    /// what makes <c>ServiceUpgrade.LateInitialize</c> → <c>BuildingPrefab.AddUpgrade</c> inject
    /// the <c>BuildingUpgradeElement</c> into the bound building prefab(s) and
    /// <c>RefreshArchetype</c> widen their archetype to include <c>InstalledUpgrade</c>. So the
    /// binding lands one frame after registration, asynchronously — caller does not see it
    /// inline.
    /// </summary>
    internal static class HiddenFactoryUpgradePrefab
    {
        /// <summary>Prefab name for the spike's in-place hidden-factory upgrade.</summary>
        public const string PrefabName = "HiddenFactoryUpgrade";

        /// <summary>
        /// Assemble the minimal in-place ServiceUpgrade carrier bound to the given vanilla
        /// building prefabs. Returns the prefab to be registered via PrefabSystem.AddPrefab.
        /// Registration and Initialize/LateInitialize are the caller's / vanilla's job (see
        /// class remarks).
        /// </summary>
        public static ObjectPrefab Build(IReadOnlyList<BuildingPrefab> targetBuildings)
        {
            // ObjectPrefab is a ScriptableObject; CreateInstance is the only legal way to make
            // one (it has no usable ctor for ECS prefab use). Name must be set before AddPrefab
            // (PrefabID is derived from it).
            var prefab = ScriptableObject.CreateInstance<ObjectPrefab>();
            prefab.name = PrefabName;

            // ServiceUpgrade component: the actual upgrade definition. AddComponent creates the
            // ScriptableObject component, names it, links prefab back-ref, and appends to
            // prefab.components — the same path the Asset Editor uses.
            var upgrade = prefab.AddComponent<ServiceUpgrade>();
            upgrade.m_UpgradeCost = 0u;       // Shadow Cash charged separately in B.2 detector
            upgrade.m_XPReward = 0;
            upgrade.m_ForbidMultiple = true;  // one hidden factory per building
            upgrade.m_MaxPlacementOffset = -1;
            upgrade.m_MaxPlacementDistance = 0f;

            // Bind to the building prefab(s) whose panel should show the upgrade. ServiceUpgrade
            // .LateInitialize iterates m_Buildings and calls AddUpgrade on each → injects
            // BuildingUpgradeElement into that building's prefab archetype.
            var buildings = new BuildingPrefab[targetBuildings.Count];
            for (int i = 0; i < targetBuildings.Count; i++)
                buildings[i] = targetBuildings[i];
            upgrade.m_Buildings = buildings;

            // UIObject: required so UIObjectData lands on the upgrade entity, the gate both the
            // building panel and the upgrade menu use to surface the upgrade. Null group keeps
            // it out of toolbar menus (entry is the building panel — the hybrid design).
            var ui = prefab.AddComponent<UIObject>();
            ui.m_Group = null;
            ui.m_Priority = 0;
            ui.m_Icon = string.Empty;
            ui.m_IsDebugObject = false;

            return prefab;
        }
    }
}
