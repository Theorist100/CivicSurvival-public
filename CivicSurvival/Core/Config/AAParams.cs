using System;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Config
{
    /// <summary>
    /// All per-type parameters of one <see cref="AAType"/>, aggregated into a single view. The
    /// balance config stores these split across three sections by FUNCTION (combat stats in
    /// <c>AAUnits</c>, crew in <c>Mobilization</c>, resupply in <c>Economy</c>); this struct
    /// reassembles them BY TYPE so every consumer reads one value instead of repeating a
    /// <c>switch (AAType)</c> over flat <c>cfg.{Type}{Param}</c> fields.
    ///
    /// NOTE: <c>AirDefensePrefabData.IsHeritage</c> ("can use heritage credits") is a placement
    /// property of the Bofors prefab, not a balance value — set at setup, not here.
    /// </summary>
    public readonly struct AATypeParams
    {
        public readonly AAType Type;
        public readonly float Range;
        public readonly float InterceptChanceShahed;
        public readonly float InterceptChanceBallistic;
        public readonly int MaxAmmo;
        public readonly int BurstRounds;
        public readonly float CooldownDuration;
        public readonly int CrewRequired;
        public readonly int Price;
        public readonly int ResupplyCost;
        public readonly float ResupplyCooldownHours;

        /// <summary>
        /// Number of waves that must pass between emergency resupplies of this type. 0 = no wave
        /// cooldown (the gun types). Only Patriot is gated per-wave: one full magazine clears a whole
        /// drone wave in a large city, so a second mid-wave top-up would trivialize it.
        /// </summary>
        public readonly int ResupplyCooldownWaves;

        public AATypeParams(AAType type, float range, float interceptShahed, float interceptBallistic,
            int maxAmmo, int burstRounds, float cooldown, int crew, int price, int resupplyCost,
            float resupplyCooldownHours, int resupplyCooldownWaves)
        {
            Type = type;
            Range = range;
            InterceptChanceShahed = interceptShahed;
            InterceptChanceBallistic = interceptBallistic;
            MaxAmmo = maxAmmo;
            BurstRounds = burstRounds;
            CooldownDuration = cooldown;
            CrewRequired = crew;
            Price = price;
            ResupplyCost = resupplyCost;
            ResupplyCooldownHours = resupplyCooldownHours;
            ResupplyCooldownWaves = resupplyCooldownWaves;
        }
    }

    /// <summary>
    /// Single source of per-type AA parameters. <see cref="ForType"/> is the ONLY place that switches
    /// on <see cref="AAType"/> to pull balance values — prefab setup, the forecast, the installation
    /// detector, placement crew checks, resupply costs, the burst-per-engagement count and the UI all
    /// read through it. Adding a new AA type means one new arm here, not a new copy in every consumer.
    ///
    /// Two type-driven specials the flat config does not encode:
    /// - Heritage has no <c>Price</c> field (it is a free reserve grant) → Price = 0.
    /// - Heritage crew lives in <c>AAUnits.HeritageCrewRequired</c>, every other type's crew is in
    ///   <c>Mobilization.{Type}Crew</c>.
    ///
    /// BURST↔AMMO COUPLING: <see cref="AATypeParams.BurstRounds"/> is the shells spent per engagement
    /// (1 shell = 1 tracer; one engagement is still one intercept roll, so burst does not change
    /// intercept balance). Per-type <see cref="AATypeParams.MaxAmmo"/> is scaled by exactly the burst
    /// so the number of engagements stays constant. If you change a <c>*BurstRounds</c> value, scale
    /// the matching <c>*MaxAmmo</c> by the same factor. Current pairing: Heritage 4↔400, Bofors 6↔1200,
    /// Gepard 8↔1600, Patriot 1↔4. Read on the gameplay/visual hot path (per engagement, per tracer);
    /// callers cache the returned view in a local rather than calling ForType repeatedly.
    /// </summary>
    public static class AAParams
    {
        public static AATypeParams ForType(RemoteBalanceConfig cfg, AAType type)
        {
            var aa = cfg.AAUnits;
            var mob = cfg.Mobilization;
            var eco = cfg.Economy;
            return type switch
            {
                AAType.HeritageBofors => new AATypeParams(
                    AAType.HeritageBofors,
                    aa.HeritageRange, aa.HeritageInterceptShahed, aa.HeritageInterceptBallistic,
                    aa.HeritageMaxAmmo, aa.HeritageBurstRounds, aa.HeritageCooldown,
                    aa.HeritageCrewRequired, price: 0,
                    eco.HeritageResupplyCost, eco.HeritageResupplyCooldownHours, resupplyCooldownWaves: 0),

                AAType.Bofors40mm => new AATypeParams(
                    AAType.Bofors40mm,
                    aa.BoforsRange, aa.BoforsInterceptShahed, aa.BoforsInterceptBallistic,
                    aa.BoforsMaxAmmo, aa.BoforsBurstRounds, aa.BoforsCooldown,
                    mob.BoforsCrew, aa.BoforsPrice,
                    eco.BoforsResupplyCost, eco.BoforsResupplyCooldownHours, resupplyCooldownWaves: 0),

                AAType.Gepard => new AATypeParams(
                    AAType.Gepard,
                    aa.GepardRange, aa.GepardInterceptShahed, aa.GepardInterceptBallistic,
                    aa.GepardMaxAmmo, aa.GepardBurstRounds, aa.GepardCooldown,
                    mob.GepardCrew, aa.GepardPrice,
                    eco.GepardResupplyCost, eco.GepardResupplyCooldownHours, resupplyCooldownWaves: 0),

                AAType.PatriotSAM => new AATypeParams(
                    AAType.PatriotSAM,
                    aa.PatriotRange, aa.PatriotInterceptShahed, aa.PatriotInterceptBallistic,
                    aa.PatriotMaxAmmo, aa.PatriotBurstRounds, aa.PatriotCooldown,
                    mob.PatriotCrew, aa.PatriotPrice,
                    eco.PatriotResupplyCost, eco.PatriotResupplyCooldownHours, eco.PatriotResupplyCooldownWaves),

                AAType.HawkSAM => new AATypeParams(
                    AAType.HawkSAM,
                    aa.HawkRange, aa.HawkInterceptShahed, aa.HawkInterceptBallistic,
                    aa.HawkMaxAmmo, aa.HawkBurstRounds, aa.HawkCooldown,
                    mob.HawkCrew, aa.HawkPrice,
                    eco.HawkResupplyCost, eco.HawkResupplyCooldownHours, eco.HawkResupplyCooldownWaves),

                _ => new AATypeParams(
                    AAType.HeritageBofors,
                    aa.HeritageRange, aa.HeritageInterceptShahed, aa.HeritageInterceptBallistic,
                    aa.HeritageMaxAmmo, aa.HeritageBurstRounds, aa.HeritageCooldown,
                    aa.HeritageCrewRequired, price: 0,
                    eco.HeritageResupplyCost, eco.HeritageResupplyCooldownHours, resupplyCooldownWaves: 0),
            };
        }

        /// <summary>
        /// Params of one barrel as fielded through a given source. <see cref="ForType"/> answers what
        /// the barrel is; this answers what you actually get, because the source can change it — the
        /// black market sells the same gun tuned past its official spec, for shadow money.
        ///
        /// Separate from <see cref="ForType"/> on purpose: that one is keyed by type alone and is read
        /// on the gameplay hot path (per engagement, per tracer). This is read at placement and by the
        /// panel — never per shot — so it can afford to know about sources.
        ///
        /// Deliberately NOT a new <see cref="AAType"/> per market variant: the barrel is the same
        /// steel, only its provenance differs, and a type per source would multiply the roster by the
        /// number of ways to buy it.
        /// </summary>
        public static AATypeParams ForOption(RemoteBalanceConfig cfg, AAType type, AAPlacementMode mode)
        {
            var p = ForType(cfg, type);
            if (mode != AAPlacementMode.BlackMarket)
                return p;

            var aa = cfg.AAUnits;
            // Intercept chances are probabilities: a bonus may not push one past certainty.
            return new AATypeParams(
                p.Type,
                p.Range * (1f + aa.BlackMarketRangeBonus),
                Math.Min(1f, p.InterceptChanceShahed * (1f + aa.BlackMarketInterceptBonus)),
                Math.Min(1f, p.InterceptChanceBallistic * (1f + aa.BlackMarketInterceptBonus)),
                p.MaxAmmo,
                p.BurstRounds,
                p.CooldownDuration,
                p.CrewRequired,
                (int)Math.Round(p.Price * aa.BlackMarketPriceMult),
                p.ResupplyCost,
                p.ResupplyCooldownHours,
                p.ResupplyCooldownWaves);
        }
    }
}
