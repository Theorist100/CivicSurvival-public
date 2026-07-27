using System;
using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// ECS Singleton for Emergency Fund runtime state.
    /// Single Writer: EmergencyFundSystem (runtime withdrawals).
    /// Config (WithdrawPercent) lives in EmergencyFundSettings — written by CSRS.
    ///
    /// Mechanics:
    /// - City has emergency fund (starts at $500k from BalanceConfig)
    /// - Withdrawn amount goes to offshore account (handled by EmergencyFundSystem)
    /// - Without fund: disaster penalties doubled
    /// </summary>
    public struct EmergencyFundSingleton : IComponentData
    {
        /// <summary>Initial emergency fund balance.</summary>
#pragma warning disable CIVIC167 // ECS IComponentData: decimal not supported in Burst/blittable
        public double InitialBalance;
#pragma warning restore CIVIC167

        /// <summary>Total amount withdrawn from fund.</summary>
        public double WithdrawnAmount;

        /// <summary>Current remaining balance (clamped to 0).</summary>
        public readonly double CurrentBalance => Math.Max(0, InitialBalance - WithdrawnAmount);

        /// <summary>True if fund is depleted (2x disaster penalties).</summary>
        public readonly bool IsDepleted => CurrentBalance <= 0;

        /// <summary>Penalty multiplier for disasters when fund is low/empty.</summary>
        public readonly float DisasterPenaltyMultiplier
        {
            get
            {
                if (InitialBalance <= 0)
                    return 1f;

                double currentBalance = CurrentBalance;
                if (currentBalance <= 0)
                    return BalanceConfig.Current.EmergencyFund.NoFundPenaltyMult;
                if (currentBalance < InitialBalance * BalanceConfig.Current.EmergencyFund.LowFundThreshold)
                    return BalanceConfig.Current.EmergencyFund.LowFundPenaltyMult;
                return 1f;
            }
        }

        public void SetDefaults() => this = Default;

        /// <summary>Default state from config.</summary>
        public static EmergencyFundSingleton Default => new()
        {
            InitialBalance = BalanceConfig.Current.EmergencyFund.InitialBalance,
            WithdrawnAmount = 0
        };

        public static void EnsureExists(EntityManager em)
        {
            var entity = CivicSingleton.EnsurePaired(
                em,
                Default,
                EmergencyFundSettings.Default,
                new EnsurePairedPolicy<EmergencyFundSingleton, EmergencyFundSettings>
            {
                EnsureShape = EnsureShape
            });
            em.SetName(entity, "EmergencyFundSingleton");
        }

        private static void EnsureShape(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<EmergencyFundSettings>(entity))
                em.AddComponentData(entity, EmergencyFundSettings.Default);
        }
    }
}


