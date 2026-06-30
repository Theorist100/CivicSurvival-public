using System;
using System.Collections.Generic;
using CivicSurvival.Core.Components.Domain.Power;
using Unity.Entities;

namespace CivicSurvival.Core.Components.CrossDomain
{
    public readonly struct PowerCapacityPlantSnapshot : IEquatable<PowerCapacityPlantSnapshot>
    {
        private const int HashSeed = 17;
        private const int HashMultiplier = 31;

        public PowerCapacityPlantSnapshot(
            Entity plant,
            PlantKind kind,
            CapacityChannel channel,
            int originalCapacityKW,
            int effectiveCapacityKW,
            int currentOutputKW,
            bool isCollapsed,
            bool isUnderConstruction,
            bool isUnderRepair,
            float explosionDamagePercent,
            float operationalDamagePercent,
            float disasterDamagePercent,
            float saturationFactor = 1f,
            float recoveryHours = 0f,
            float fuelAvailability = 1f,
            float fuelFactor = 1f)
        {
            Plant = plant;
            Kind = kind;
            Channel = channel;
            OriginalCapacityKW = originalCapacityKW;
            EffectiveCapacityKW = effectiveCapacityKW;
            CurrentOutputKW = currentOutputKW;
            IsCollapsed = isCollapsed;
            IsUnderConstruction = isUnderConstruction;
            IsUnderRepair = isUnderRepair;
            ExplosionDamagePercent = explosionDamagePercent;
            OperationalDamagePercent = operationalDamagePercent;
            DisasterDamagePercent = disasterDamagePercent;
            SaturationFactor = saturationFactor;
            RecoveryHours = recoveryHours;
            FuelAvailability = fuelAvailability;
            FuelFactor = fuelFactor;
        }

        public Entity Plant { get; }
        public PlantKind Kind { get; }

        /// <summary>
        /// Resolver capacity channel (<c>GridProducer</c> / <c>EmergencyBattery</c> /
        /// <c>OutsideConnection</c>), threaded verbatim from <c>PowerCapacityIndexState.Channel</c>.
        /// The exact axis the resolver sums into <see cref="PowerCapacitySnapshot.NameplateKW"/>
        /// (GridProducer only) — readers that need the nameplate inclusion set filter on this
        /// directly instead of reconstructing it from <see cref="Kind"/>.
        /// </summary>
        public CapacityChannel Channel { get; }
        public int OriginalCapacityKW { get; }

        /// <summary>
        /// Synchronous knockout indicator = <c>round(OriginalCapacity × factor)</c> where
        /// factor folds damage and construction (collapse/repair ⇒ 0). Written by the
        /// resolver in the same tick the factor changes, so disaster/wear gates see
        /// collapse/repair without the vanilla <c>m_Capacity</c> propagation lag. NOT a
        /// weather-accurate output — for renewables this is nameplate × damage.
        /// </summary>
        public int EffectiveCapacityKW { get; }

        /// <summary>
        /// Weather-accurate dispatchable output (vanilla <c>ElectricityProducer.m_Capacity</c>,
        /// which is actual production for the tick, not nameplate). For UI "current output"
        /// display. Reflects wind/sun/water modulation; do NOT use as a knockout gate (a
        /// healthy windmill reads ~0 in calm weather).
        /// </summary>
        public int CurrentOutputKW { get; }
        public bool IsCollapsed { get; }
        public bool IsUnderConstruction { get; }
        public bool IsUnderRepair { get; }
        public float ExplosionDamagePercent { get; }
        public float OperationalDamagePercent { get; }
        public float DisasterDamagePercent { get; }

        /// <summary>
        /// Effective surplus-saturation factor ∈ [SaturationFloor, 1] (1 = no penalty).
        /// For UI per-station badge (Фаза 4); DTO pass-through lands in Фаза 4.
        /// </summary>
        public float SaturationFactor { get; }

        /// <summary>
        /// Estimated game-hours for the saturation factor to climb from its current value to the
        /// fleet target under the asymmetric inertia (0 when no up-ramp is pending). For the
        /// Фаза-4 inertia tooltip ("62%→100% (~9h)"); DTO pass-through lands in Фаза 4.
        /// </summary>
        public float RecoveryHours { get; }

        /// <summary>
        /// Raw fuel-stockpile fraction ∈ [0,1] (1 = full / non-thermal). For the UI per-station
        /// fuel badge (Фаза 4); DTO pass-through lands in Фаза 4.
        /// </summary>
        public float FuelAvailability { get; }

        /// <summary>
        /// Post-sigmoid fuel OUTPUT factor ∈ [MinOutputAtZero, 1] (1 = no penalty / non-thermal /
        /// feature off). The value actually folded into the plant's capacity. The UI fuel badge
        /// gates on THIS (penalty active?) while displaying <see cref="FuelAvailability"/> — the
        /// sigmoid's buffer forgives ordinary supply dips, so gating on the raw fraction would
        /// flag perfectly healthy plants.
        /// </summary>
        public float FuelFactor { get; }

        public bool Equals(PowerCapacityPlantSnapshot other)
            => Plant.Index == other.Plant.Index
               && Plant.Version == other.Plant.Version
               && Kind == other.Kind
               && Channel == other.Channel
               && OriginalCapacityKW == other.OriginalCapacityKW
               && EffectiveCapacityKW == other.EffectiveCapacityKW
               && CurrentOutputKW == other.CurrentOutputKW
               && IsCollapsed == other.IsCollapsed
               && IsUnderConstruction == other.IsUnderConstruction
               && IsUnderRepair == other.IsUnderRepair
               && ExplosionDamagePercent.Equals(other.ExplosionDamagePercent)
               && OperationalDamagePercent.Equals(other.OperationalDamagePercent)
               && DisasterDamagePercent.Equals(other.DisasterDamagePercent)
               && SaturationFactor.Equals(other.SaturationFactor)
               && RecoveryHours.Equals(other.RecoveryHours)
               && FuelAvailability.Equals(other.FuelAvailability)
               && FuelFactor.Equals(other.FuelFactor);

        public override bool Equals(object? obj)
            => obj is PowerCapacityPlantSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HashSeed;
                hash = (hash * HashMultiplier) + Plant.Index;
                hash = (hash * HashMultiplier) + Plant.Version;
                hash = (hash * HashMultiplier) + (int)Kind;
                hash = (hash * HashMultiplier) + (int)Channel;
                hash = (hash * HashMultiplier) + OriginalCapacityKW;
                hash = (hash * HashMultiplier) + EffectiveCapacityKW;
                hash = (hash * HashMultiplier) + CurrentOutputKW;
                hash = (hash * HashMultiplier) + IsCollapsed.GetHashCode();
                hash = (hash * HashMultiplier) + IsUnderConstruction.GetHashCode();
                hash = (hash * HashMultiplier) + IsUnderRepair.GetHashCode();
                hash = (hash * HashMultiplier) + ExplosionDamagePercent.GetHashCode();
                hash = (hash * HashMultiplier) + OperationalDamagePercent.GetHashCode();
                hash = (hash * HashMultiplier) + DisasterDamagePercent.GetHashCode();
                hash = (hash * HashMultiplier) + SaturationFactor.GetHashCode();
                hash = (hash * HashMultiplier) + RecoveryHours.GetHashCode();
                hash = (hash * HashMultiplier) + FuelAvailability.GetHashCode();
                hash = (hash * HashMultiplier) + FuelFactor.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(PowerCapacityPlantSnapshot left, PowerCapacityPlantSnapshot right)
            => left.Equals(right);

        public static bool operator !=(PowerCapacityPlantSnapshot left, PowerCapacityPlantSnapshot right)
            => !left.Equals(right);
    }

    public readonly struct PowerCapacitySnapshot : IEquatable<PowerCapacitySnapshot>
    {
        private const int HashSeed = 17;
        private const int HashMultiplier = 31;

        public PowerCapacitySnapshot(
            int dispatchableMW,
            PowerCapacityPlantSnapshot[] plants,
            int nameplateKW = 0,
            float fleetTargetFactor = 1f,
            int largestPlantKW = 0,
            int intermittentTypeCount = 0,
            int cityDispatchableMW = 0)
        {
            DispatchableMW = dispatchableMW;
            Plants = plants ?? Array.Empty<PowerCapacityPlantSnapshot>();
            NameplateKW = nameplateKW;
            FleetTargetFactor = fleetTargetFactor;
            LargestPlantKW = largestPlantKW;
            IntermittentTypeCount = intermittentTypeCount;
            CityDispatchableMW = cityDispatchableMW;
        }

        public int DispatchableMW { get; }
        public IReadOnlyList<PowerCapacityPlantSnapshot> Plants { get; }

        /// <summary>
        /// Channel-correct built nameplate Σ <c>PlantBaseCapacity.OriginalCapacity</c> over
        /// grid producers (<c>CapacityChannel.GridProducer</c>), in kW — excludes OutsideConnection
        /// and EmergencyBattery, plants still under construction-delay, and knocked-out ruins
        /// (collapse / repair window / fully damaged — see <c>PowerCapacityMath.IsKnockedOut</c>:
        /// counting ruins would make every successful enemy strike raise the survivors' surplus
        /// surcharge). Degradation does NOT touch this, so a spam city cannot hide over-build
        /// behind its own degraded output. Read by Фаза 1 (ratio) and Фаза 7
        /// (surplus-attracts-strikes via IPowerCapacitySnapshotReader). Authored by the resolve
        /// pass (PlantResolveJob → PublishResolveResults) every tick (independent of the
        /// saturation toggle).
        /// </summary>
        public int NameplateKW { get; }

        /// <summary>
        /// Fleet surplus-saturation TARGET factor ∈ [SaturationFloor, 1] (not the weighted-effective —
        /// that jitters under strikes). For the Фаза-4 "fleet КПД" aggregate. 1 when feature off / no fleet.
        /// </summary>
        public float FleetTargetFactor { get; }

        /// <summary>
        /// Largest single grid-producer nameplate (kW), same channel/construction/knockout filter
        /// as <see cref="NameplateKW"/>. Base of the N+1 unit buffer: consumers forgive a reserve of
        /// min(LargestPlantKW, UnitBufferCapMW) before counting surplus — plants come in build
        /// quanta, and losing the biggest unit must not black out the city. Read by the Фаза-7
        /// strike axis (WaveScheduler) and mirrored by the degradation axis (resolver-internal).
        /// </summary>
        public int LargestPlantKW { get; }

        /// <summary>
        /// Distinct INTERMITTENT generation types present (Wind/Solar — see
        /// <c>PowerPlantUtils.IsIntermittent</c>), 0..2, same channel/construction/knockout
        /// filter as <see cref="NameplateKW"/>. Base of the diversity headroom on the Фаза-7
        /// strike axis: each intermittent type widens the surplus-free threshold by
        /// <c>GenerationSaturation.HeadroomPerType</c> (weather-dependent sources genuinely
        /// need backup reserve; stable types earn nothing — counting every brand let
        /// "diverse spam" farm the threshold). Mirrors the degradation axis exactly.
        /// </summary>
        public int IntermittentTypeCount { get; }

        /// <summary>
        /// Σ min(CurrentOutputKW, EffectiveCapacityKW)/1000 (integer division PER PLANT)
        /// over the GridProducer channel — dispatchable potential of the CITY fleet only,
        /// without OutsideConnection (import) and EmergencyBattery. Formula and rounding
        /// order match INFRA OUTPUT (EquipmentUISystem.ResolveCurrentOutputMW + the sum in
        /// InfrastructureContent.tsx), but the plant SET is wider: the INFRA table drops
        /// plants below Engineering.SmallPlantCapacityKw and plants without a wear sidecar,
        /// while this aggregate counts the whole channel. Hence SURPLUS(POWER) ≡
        /// OUTPUT(INFRA) + output of plants &lt; SmallPlantCapacityKw − load.
        /// <see cref="DispatchableMW"/> (with import/batteries) serves
        /// RECOVERY/AutoDispatch — do not mix the two semantics.
        /// </summary>
        public int CityDispatchableMW { get; }

        public static PowerCapacitySnapshot Empty { get; } =
            new(0, Array.Empty<PowerCapacityPlantSnapshot>());

        public bool Equals(PowerCapacitySnapshot other)
        {
            if (DispatchableMW != other.DispatchableMW)
                return false;
            if (CityDispatchableMW != other.CityDispatchableMW)
                return false;
            if (NameplateKW != other.NameplateKW)
                return false;
            if (!FleetTargetFactor.Equals(other.FleetTargetFactor))
                return false;
            if (LargestPlantKW != other.LargestPlantKW)
                return false;
            if (IntermittentTypeCount != other.IntermittentTypeCount)
                return false;
            if (ReferenceEquals(Plants, other.Plants))
                return true;
            if (Plants.Count != other.Plants.Count)
                return false;

            for (int i = 0; i < Plants.Count; i++)
            {
                if (!Plants[i].Equals(other.Plants[i]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj)
            => obj is PowerCapacitySnapshot other && Equals(other);

        public static bool operator ==(PowerCapacitySnapshot left, PowerCapacitySnapshot right)
            => left.Equals(right);

        public static bool operator !=(PowerCapacitySnapshot left, PowerCapacitySnapshot right)
            => !left.Equals(right);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HashSeed;
                hash = (hash * HashMultiplier) + DispatchableMW;
                hash = (hash * HashMultiplier) + CityDispatchableMW;
                hash = (hash * HashMultiplier) + NameplateKW;
                hash = (hash * HashMultiplier) + FleetTargetFactor.GetHashCode();
                hash = (hash * HashMultiplier) + LargestPlantKW;
                hash = (hash * HashMultiplier) + IntermittentTypeCount;
                for (int i = 0; i < Plants.Count; i++)
                    hash = (hash * HashMultiplier) + Plants[i].GetHashCode();
                return hash;
            }
        }
    }
}
