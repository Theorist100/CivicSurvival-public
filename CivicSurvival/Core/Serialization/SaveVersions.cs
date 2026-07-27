namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Централізований реєстр версій серіалізації.
    ///
    /// PRE-RELEASE: Використовуємо одну глобальну версію.
    /// POST-RELEASE: Розіб'ємо на доменні версії для granular control.
    ///
    /// ПРАВИЛО: До релізу тримаємо GLOBAL = 1.
    /// Бамп версії потребує version guards у кожному Deserialize, який змінює форму даних.
    /// </summary>
    public static class SaveVersions
    {
        /// <summary>
        /// Глобальна версія моду.
        /// Keep at 1 during pre-release unless every affected reader has explicit guards.
        /// </summary>
        public const byte GLOBAL = 1;

        // ══════════════════════════════════════════════════════════════
        // POST-RELEASE: Розкоментувати та мігрувати на доменні версії
        // ══════════════════════════════════════════════════════════════
        //
        // public const byte Core = 1;           // GameTimeSystem, HelpStateSystem
        // public const byte Blackout = 1;       // BlackoutSystem, DistrictPenaltySystem
        // public const byte AirDefense = 1;     // AirDefenseSystem, IntelSystem, etc.
        // public const byte Threats = 1;        // WaveScheduler, WaveExecutor, ThreatSpawnSystem, etc.
        // public const byte Economics = 1;      // CrisisEconomicsSystem, ShadowWalletSystem
        // public const byte Scenario = 1;       // ScenarioStateMachine, NarrativeSystem
        // public const byte PowerBackup = 1;    // BackupPower*, LoadShedding
        // public const byte Corruption = 1;     // ShadowReputation, FuelSiphoning
        // public const byte GridWarfare = 1;    // CityStability, PlayerAttack
        // public const byte Mobilization = 1;   // MobilizationSystem
        // public const byte Refugees = 1;       // RefugeeSpawn, Integration, Migration
        // public const byte Tutorial = 1;       // CrisisTutorialSystem
    }
}
