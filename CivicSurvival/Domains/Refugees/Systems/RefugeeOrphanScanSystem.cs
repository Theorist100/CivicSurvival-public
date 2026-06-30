using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Refugees.Data;

namespace CivicSurvival.Domains.Refugees.Systems
{
    /// <summary>
    /// Reactive orphan detector for refugee households.
    ///
    /// A refugee is "orphaned" when the park its m_TempHome points at is destroyed by a
    /// strike. There is no reactive cross-domain signal for that destruction inside the
    /// Refugees domain: parks die in the ThreatDamage domain (which Refugees may not
    /// import — Axiom 5), and vanilla never repairs HomelessHousehold.m_TempHome at
    /// runtime when a building is destroyed.
    ///
    /// But the destruction itself IS observable without a cross-domain import: vanilla
    /// stamps <see cref="Deleted"/> on the destroyed park for at least one frame before
    /// the entity is recycled (the same Deleted-frame vanilla PropertyRenterSystem reacts
    /// to when clearing renters). So this system gates on the PRESENCE of a deleted park
    /// rather than polling the live-park count every frame:
    ///  - RequireForUpdate(m_DeletedParkQuery): the system is scheduled ONLY on a frame
    ///    where a park carries Deleted — i.e. a park was just destroyed. No per-frame tick.
    ///  - RequireForUpdate(m_RefugeeQuery): and only while refugees exist at all.
    ///
    /// On that frame it walks the refugees once, and for any whose m_TempHome is no longer
    /// a live park it re-arms NeedsRefugeeRelocation (enabled = true). RefugeeMigrationSystem
    /// (registered after this system) then relocates them into a live park on its next pass.
    ///
    /// No post-load handling is needed: a park destroyed before a save leaves its refugees
    /// with a dangling m_TempHome, and RefugeeMigrationSystem.ValidateAfterLoad re-derives
    /// their enabled bit from that durable m_TempHome on load.
    /// </summary>
    [ActIndependent]
    public partial class RefugeeOrphanScanSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("RefugeeOrphanScanSystem");

        private EntityQuery m_RefugeeQuery;
        private EntityQuery m_LiveParkQuery;
        private EntityQuery m_DeletedParkQuery;

        // ECB barrier for the structural AddComponent<NeedsRefugeeRelocation> that re-arms
        // the presence gate on an orphaned refugee.
        private GameSimulationEndBarrier m_ECBSystem = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_RefugeeQuery = GetEntityQuery(
                ComponentType.ReadOnly<RefugeeHousehold>(),
                ComponentType.ReadOnly<HomelessHousehold>(),
                ComponentType.Exclude<Deleted>()
            );

            // Live parks (durable truth for orphan detection): a refugee whose m_TempHome
            // is NOT in this set has lost its shelter.
            m_LiveParkQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Park>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            );

            // Reactive trigger: a park stamped Deleted this frame (destroyed). Tool-preview
            // parks (Temp) are excluded so a cancelled placement never fires the scan.
            m_DeletedParkQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Park>(),
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.Exclude<Temp>()
            );

            // AND of both RequireForUpdate calls: scheduled only on a frame where a park was
            // destroyed AND refugees exist. Off these frames the system gets zero ticks.
            RequireForUpdate(m_DeletedParkQuery);
            RequireForUpdate(m_RefugeeQuery);

            m_ECBSystem = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            Log.Info("Created");
        }

        [CompletesDependency("OnUpdateImpl: reactive (gated by a Deleted park query), runs only on the frame a sheltering park is destroyed; ToEntityArray on the live-park query then. Off the hot path — not a [HotPathSystem]")]
        protected override void OnUpdateImpl()
        {
            // A park carries Deleted this frame — re-arm the relocation marker on any refugee
            // whose m_TempHome is no longer a live park.
            var parkArray = m_LiveParkQuery.ToEntityArray(Allocator.Temp);
            var parkSet = new NativeHashSet<Entity>(parkArray.Length == 0 ? 1 : parkArray.Length, Allocator.Temp);
            for (int i = 0; i < parkArray.Length; i++)
                parkSet.Add(parkArray[i]);

            EntityCommandBuffer ecb = default;
            bool hasEcb = false;
            int orphaned = 0;
            foreach (var (homelessRef, entity) in
                SystemAPI.Query<RefRO<HomelessHousehold>>()
                .WithAll<RefugeeHousehold>()
                .WithNone<Deleted>()
                .WithEntityAccess())
            {
                Entity tempHome = homelessRef.ValueRO.m_TempHome;
                if (parkSet.Contains(tempHome))
                    continue;

                // m_TempHome points at a destroyed/missing park. Border refugees already
                // carry the marker from spawn — skip them so we only Add it to genuine
                // orphans (a refugee whose live park just died, whose marker was removed
                // when it was first relocated there). AddComponent is a structural change → ECB,
                // allocated lazily once we know there is at least one orphan to re-arm.
                if (SystemAPI.HasComponent<NeedsRefugeeRelocation>(entity))
                    continue;

                if (!hasEcb)
                {
                    ecb = m_ECBSystem.CreateCommandBuffer();
                    hasEcb = true;
                }
                ecb.AddComponent<NeedsRefugeeRelocation>(entity);
                orphaned++;
            }

            if (parkArray.IsCreated) parkArray.Dispose();
            if (parkSet.IsCreated) parkSet.Dispose();

            if (orphaned > 0)
                Log.Info($"Re-armed relocation marker on {orphaned} orphaned refugee households (sheltering park destroyed)");
        }
    }
}
