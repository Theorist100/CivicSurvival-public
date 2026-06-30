using Unity.Entities;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.AirDefense.Logic
{
    /// <summary>
    /// Shared creation of the manpower-release / casualty request entities emitted when an AA
    /// installation leaves play. Used by both AA-removal paths so the request shape stays single-source:
    /// the combat-loss path (AACrewReleaseSystem — threat destruction, survivor/KIA split) and the
    /// player-demolition path (AAPlayerDemolitionSystem — full release, no casualties).
    /// Each returns the ECB command count for the producing system's diagnostics counter.
    /// </summary>
    public static class AACrewRequests
    {
        public static int CreateReleaseRequest(EntityCommandBuffer ecb, int count, AAType aaType, Entity aaEntity)
        {
            var requestEntity = ecb.CreateEntity();
            ecb.AddComponent(requestEntity, new ManpowerReleaseRequest
            {
                Count = count,
                AATypeHash = (int)aaType,
                EntityIndex = aaEntity.Index,
                EntityVersion = aaEntity.Version
            });
            RequestMetaWriter.AddInternal(ecb, requestEntity, nameof(ManpowerReleaseRequest), aaEntity.Index.ToString());
            return 2; // CreateEntity + AddComponent
        }

        public static int CreateCasualtyRequest(EntityCommandBuffer ecb, int count, AAType aaType, Entity aaEntity)
        {
            var requestEntity = ecb.CreateEntity();
            ecb.AddComponent(requestEntity, new CasualtyReportRequest
            {
                Count = count,
                AATypeHash = (int)aaType,
                EntityIndex = aaEntity.Index,
                EntityVersion = aaEntity.Version
            });
            RequestMetaWriter.AddInternal(ecb, requestEntity, nameof(CasualtyReportRequest), aaEntity.Index.ToString());
            return 2; // CreateEntity + AddComponent
        }
    }
}
