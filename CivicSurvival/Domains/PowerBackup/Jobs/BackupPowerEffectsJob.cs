using Game.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.PowerBackup.Jobs
{
    /// <summary>
    /// Burst-compiled job for backup power effects: degradation and fire risk.
    /// Fire intents are stored on BackupPower and applied later by the effects system;
    /// no job-owned NativeContainers are used as cross-frame result mailboxes.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    [WithNone(typeof(Deleted))]
    public partial struct BackupPowerEffectsJob : IJobEntity
    {
        public float DtScale;
        public uint RandomSeed;
        public uint EffectTick;

        public float FireRiskHomeBattery;
        public float FireRiskBusinessUps;
        public float FireRiskIndustrialBattery;
        public float FireRiskDieselGenerator;

        // Counterfeit battery fire risk multipliers ((Index,Version) -> multiplier).
        [ReadOnly] public NativeHashMap<long, float> CounterfeitFireRiskMap;

        public void Execute(Entity entity, ref BackupPower backup)
        {
            if (backup.Type == BackupPowerType.None)
                return;

            if (backup.Type == BackupPowerType.DieselGenerator)
                ProcessGenerator(entity, ref backup);
            else
                ProcessBattery(entity, ref backup);
        }

        private void ProcessGenerator(Entity entity, ref BackupPower backup)
        {
            if (!backup.IsDischarging || backup.FuelHours <= 0)
                return;

            var rng = CreateEntityRandom(entity, backup, 0xD1E5E100u);
            float fireChance = math.min(1f, FireRiskDieselGenerator * 0.01f * DtScale);
            if (rng.NextFloat() >= fireChance)
                return;

            backup.Degradation = math.min(backup.Degradation + 0.1f, BackupPower.MAX_DEGRADATION);
            backup.FuelHours = 0;
            backup.PendingFireType = backup.Type;
        }

        private void ProcessBattery(Entity entity, ref BackupPower backup)
        {
            if (!backup.IsDischarging || backup.CurrentChargeWh <= 0)
                return;

            float baseRisk = GetFireRisk(backup.Type);

            long buildingKey = backup.Building.Packed;
            if (CounterfeitFireRiskMap.IsCreated &&
                CounterfeitFireRiskMap.TryGetValue(buildingKey, out float counterfeitMult))
            {
                baseRisk *= counterfeitMult;
            }

#pragma warning disable CIVIC247 // Probability — near-zero fire chance is valid.
            float fireChance = math.min(1f, baseRisk * (1f + backup.Degradation * 2f) * 0.001f * DtScale);
#pragma warning restore CIVIC247

            var rng = CreateEntityRandom(entity, backup, 0xB477E12Bu);
            if (rng.NextFloat() >= fireChance)
                return;

            backup.Degradation = math.min(backup.Degradation + 0.1f, BackupPower.MAX_DEGRADATION);
            backup.CurrentChargeWh /= 2;
            backup.PendingFireType = backup.Type;
        }

        private Random CreateEntityRandom(Entity entity, BackupPower backup, uint salt)
        {
            uint seed = math.hash(new uint4(
                RandomSeed ^ EffectTick ^ salt,
                (uint)entity.Index,
                (uint)entity.Version,
                ((uint)backup.Building.Index << 16) ^ (uint)backup.Building.Version));
            return new Random(seed == 0 ? 0x42504553u : seed);
        }

        private float GetFireRisk(BackupPowerType type)
        {
            return type switch
            {
                BackupPowerType.HomeBattery => FireRiskHomeBattery,
                BackupPowerType.BusinessUPS => FireRiskBusinessUps,
                BackupPowerType.IndustrialBattery => FireRiskIndustrialBattery,
                BackupPowerType.DieselGenerator => FireRiskDieselGenerator,
                BackupPowerType.None => 0f,
#pragma warning disable CIVIC019 // Burst job cannot throw — 0f is safe fallback.
                _ => 0f
#pragma warning restore CIVIC019
            };
        }
    }
}
