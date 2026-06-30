using System;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Config
{
    /// <summary>
    /// Gun-AA grouping for the consolidated "restock all guns" emergency resupply, and the
    /// single definition of which AA types count as guns. The three gun types
    /// (40mm Bofors / Gepard / Heritage Bofors) share ONE player resupply button and are the
    /// only types the calm-phase auto trickle refills; the Patriot SAM is deliberately absent
    /// from both — it has its own dear flat cost and resupply cooldown, so it is refilled only
    /// through its own button, never automatically.
    ///
    /// This is the ONE place that answers "which types are guns" and "what does restocking all
    /// guns cost", so the UI affordability gate (AirDefenseUISystem) and the request processor
    /// (AirDefenseActionRequestSystem) never disagree on the price shown vs. the price charged.
    /// </summary>
    public static class AAResupplyGroups
    {
        /// <summary>
        /// Sentinel id passed through the EmergencyResupply trigger to mean "all gun types at
        /// once". Not a valid <see cref="AAType"/> (guns have no single type) — negative so it can
        /// never collide with a real enum member. The UI sends this; the request handler maps it
        /// to <c>EmergencyResupplyKind.EmergencyGuns</c>.
        /// </summary>
        public const int GunsResupplyTypeId = -1;

        /// <summary>
        /// The gun AA types, restocked together by the single emergency button and by the
        /// calm-phase auto trickle. <see cref="AAType.PatriotSAM"/> is intentionally excluded.
        /// </summary>
        public static readonly AAType[] GunTypes =
        {
            AAType.Bofors40mm,
            AAType.Gepard,
            AAType.HeritageBofors
        };

        public static bool IsGunType(AAType type) =>
            type == AAType.Bofors40mm || type == AAType.Gepard || type == AAType.HeritageBofors;

        /// <summary>
        /// Combined flat emergency cost of restocking every gun type that currently has a
        /// deficit. Each gun type with at least one under-supplied live installation contributes
        /// its own fixed <see cref="AATypeParams.ResupplyCost"/>; full types cost nothing. Shared
        /// by the UI gate and the backend charge so the displayed price equals the charged price.
        /// </summary>
        public static int GunsResupplyCost(RemoteBalanceConfig cfg, Func<AAType, bool> hasDeficit)
        {
            int cost = 0;
            foreach (var type in GunTypes)
            {
                if (hasDeficit(type))
                    cost += AAParams.ForType(cfg, type).ResupplyCost;
            }
            return cost;
        }
    }
}
