using System;
using System.Collections.Generic;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Config
{
    /// <summary>
    /// Gun-AA grouping for the consolidated "restock all guns" emergency resupply. Gun types share
    /// ONE player resupply button and are the only types the calm-phase auto trickle refills; the
    /// Patriot SAM is deliberately absent from both — it has its own dear flat cost and resupply
    /// cooldown, so it is refilled only through its own button, never automatically.
    ///
    /// "Which types are guns" is NOT decided here — it is <see cref="AmmoKind.Shells"/>, derived
    /// from the type's weapon class in <see cref="AATypeWeapon"/>. It used to be a second hand-kept
    /// roster next to that one, and the two only agreed by hand: the weapon class excludes Patriot
    /// (so a new type defaults to autocannon), while the roster listed guns explicitly (so a new
    /// type defaulted to NOT a gun) — a new AA would have silently dropped out of resupply and auto
    /// refill. One truth now, deny nothing by omission.
    ///
    /// What this class still owns: "what does restocking all guns cost", so the UI affordability
    /// gate (AirDefenseUISystem) and the request processor (AirDefenseActionRequestSystem) never
    /// disagree on the price shown vs. the price charged.
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
        /// Sentinel id for "all missile types at once" (Patriot + Hawk), the rocket-kind mirror of
        /// <see cref="GunsResupplyTypeId"/>. Negative so it can never collide with a real
        /// <see cref="AAType"/>; distinct from the guns sentinel. The UI sends this; the request
        /// handler maps it to <c>EmergencyResupplyKind.EmergencyRockets</c>.
        /// </summary>
        public const int RocketsResupplyTypeId = -2;

        /// <summary>
        /// The gun AA types, restocked together by the single emergency button and by the
        /// calm-phase auto trickle. Derived from the munition kind, so a new autocannon joins the
        /// button the day it exists. Missile types fall out on their own — they eat rockets.
        /// </summary>
        public static readonly IReadOnlyList<AAType> GunTypes = BuildTypes(AmmoKind.Shells);

        /// <summary>
        /// The missile AA types (Patriot, Hawk), restocked together by the single "restock rockets"
        /// button. Derived from the munition kind, so a new missile launcher joins the button the
        /// day it exists — no roster to edit here.
        /// </summary>
        public static readonly IReadOnlyList<AAType> RocketTypes = BuildTypes(AmmoKind.Rockets);

        public static bool IsGunType(AAType type) => type.AmmoKind() == AmmoKind.Shells;
        public static bool IsRocketType(AAType type) => type.AmmoKind() == AmmoKind.Rockets;

        private static IReadOnlyList<AAType> BuildTypes(AmmoKind kind)
        {
            var list = new List<AAType>(AATypes.All.Count);
            foreach (var type in AATypes.All)
            {
                if (type.AmmoKind() == kind)
                    list.Add(type);
            }
            return list.AsReadOnly();
        }

        /// <summary>
        /// Combined flat emergency cost of restocking every gun type that currently has a
        /// deficit. Each gun type with at least one under-supplied live installation contributes
        /// its own fixed <see cref="AATypeParams.ResupplyCost"/>; full types cost nothing. Shared
        /// by the UI gate and the backend charge so the displayed price equals the charged price.
        /// </summary>
        public static int GunsResupplyCost(RemoteBalanceConfig cfg, Func<AAType, bool> hasDeficit)
            => GroupResupplyCost(cfg, GunTypes, hasDeficit);

        /// <summary>
        /// Combined flat emergency cost of restocking every missile type that is eligible right now
        /// — <paramref name="eligible"/> folds in both a live ammo deficit AND being off the per-type
        /// wave cooldown, so the price equals exactly what the batch will charge (mirrors
        /// <see cref="GunsResupplyCost"/>, which has no cooldown term because guns refill freely).
        /// </summary>
        public static int RocketsResupplyCost(RemoteBalanceConfig cfg, Func<AAType, bool> eligible)
            => GroupResupplyCost(cfg, RocketTypes, eligible);

        private static int GroupResupplyCost(RemoteBalanceConfig cfg, IReadOnlyList<AAType> types, Func<AAType, bool> include)
        {
            int cost = 0;
            for (int i = 0; i < types.Count; i++)
            {
                if (include(types[i]))
                    cost += AAParams.ForType(cfg, types[i]).ResupplyCost;
            }
            return cost;
        }
    }
}
