using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct GridStressPersistState
    {
        public GridStressPersistState(
            double lastGameHour,
            byte lastZone,
            float stressHours,
            float currentFrequency,
            bool isCollapsed,
            byte zone,
            float recoveryHoursRemaining,
            float collapseThresholdHours)
        {
            LastGameHour = lastGameHour;
            LastZone = lastZone;
            StressHours = stressHours;
            CurrentFrequency = currentFrequency;
            IsCollapsed = isCollapsed;
            Zone = zone;
            RecoveryHoursRemaining = recoveryHoursRemaining;
            CollapseThresholdHours = collapseThresholdHours;
        }

        public double LastGameHour { get; }
        public byte LastZone { get; }
        public float StressHours { get; }
        public float CurrentFrequency { get; }
        public bool IsCollapsed { get; }
        public byte Zone { get; }
        public float RecoveryHoursRemaining { get; }
        public float CollapseThresholdHours { get; }
    }

    public static class GridStressCodec
    {
        private const float DefaultFrequency = 50f;
        private const float DefaultCollapseThresholdHours = 2f;
        public const byte MaxLastZone = 3;

        public static void Write<TWriter>(in GridStressPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 8);
            KeyedSerializer.WriteField(writer, "m_LastGameHour", state.LastGameHour);
            KeyedSerializer.WriteEnumByteField(writer, "m_LastZone", state.LastZone);
            KeyedSerializer.WriteField(writer, "strH", state.StressHours);
            KeyedSerializer.WriteField(writer, "freq", state.CurrentFrequency);
            KeyedSerializer.WriteField(writer, "coll", state.IsCollapsed);
            KeyedSerializer.WriteEnumByteField(writer, "zone", state.Zone);
            KeyedSerializer.WriteField(writer, "recH", state.RecoveryHoursRemaining);
            KeyedSerializer.WriteField(writer, "thrH", state.CollapseThresholdHours);
        }

        public static void Read<TReader>(TReader reader, out GridStressPersistState state)
            where TReader : IReader
        {
            double lastGameHour = -1.0;
            byte lastZone = 0;
            float stressHours = 0f;
            float currentFrequency = DefaultFrequency;
            bool isCollapsed = false;
            byte zone = 0;
            float recoveryHoursRemaining = 0f;
            float collapseThresholdHours = DefaultCollapseThresholdHours;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_LastGameHour":
                        lastGameHour = KeyedSerializer.ReadSafeDouble(reader, tag, "m_LastGameHour", -1.0);
                        break;
                    case "m_LastZone":
                        lastZone = ReadBoundedEnumByte(reader, tag, "m_LastZone", 0, MaxLastZone, 0);
                        break;
                    case "strH":
                        stressHours = KeyedSerializer.ReadSafeFloat(reader, tag, "strH", -2f, 1000f, 0f);
                        break;
                    case "freq":
                        currentFrequency = KeyedSerializer.ReadSafeFloat(reader, tag, "freq", 0f, 100f, DefaultFrequency);
                        break;
                    case "coll":
                        isCollapsed = KeyedSerializer.ReadBool(reader, tag, "coll");
                        break;
                    case "zone":
                        zone = ReadBoundedEnumByte(reader, tag, "zone", 0, MaxLastZone, 0);
                        break;
                    case "recH":
                        recoveryHoursRemaining = KeyedSerializer.ReadSafeFloat(reader, tag, "recH", 0f, 1000f, 0f);
                        break;
                    case "thrH":
                        collapseThresholdHours = KeyedSerializer.ReadSafeFloat(reader, tag, "thrH", 0.1f, 1000f, DefaultCollapseThresholdHours);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new GridStressPersistState(lastGameHour, lastZone, stressHours, currentFrequency, isCollapsed, zone, recoveryHoursRemaining, collapseThresholdHours);
        }

        private static byte ReadBoundedEnumByte<TReader>(
            TReader reader,
            TypeTag tag,
            string key,
            byte min,
            byte max,
            byte defaultValue)
            where TReader : IReader
        {
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.EnumByte, key))
                return defaultValue;

            reader.Read(out byte value);
            return value >= min && value <= max ? value : defaultValue;
        }
    }

    public readonly struct EquipmentWearPersistState
    {
        public EquipmentWearPersistState(int nextPlantId, double gameHour, double lastGameHour)
        {
            NextPlantId = nextPlantId;
            GameHour = gameHour;
            LastGameHour = lastGameHour;
        }

        public int NextPlantId { get; }
        public double GameHour { get; }
        public double LastGameHour { get; }
    }

    public static class EquipmentWearCodec
    {
        public static void Write<TWriter>(in EquipmentWearPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "m_NextPlantId", state.NextPlantId);
            KeyedSerializer.WriteField(writer, "m_GameHour", state.GameHour);
            KeyedSerializer.WriteField(writer, "m_LastGameHour", state.LastGameHour);
        }

        public static void Read<TReader>(TReader reader, out EquipmentWearPersistState state)
            where TReader : IReader
        {
            int nextPlantId = 1;
            double gameHour = 0.0;
            double lastGameHour = -1.0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_NextPlantId":
                        nextPlantId = KeyedSerializer.ReadBoundedInt(reader, tag, "m_NextPlantId", 1, int.MaxValue, 1);
                        break;
                    case "m_GameHour":
                        gameHour = KeyedSerializer.ReadSafeDouble(reader, tag, "m_GameHour", 0.0);
                        break;
                    case "m_LastGameHour":
                        lastGameHour = KeyedSerializer.ReadSafeDouble(reader, tag, "m_LastGameHour", -1.0);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }
            state = new EquipmentWearPersistState(nextPlantId, gameHour, lastGameHour);
        }
    }

    public readonly struct WinterMultiplierPersistState
    {
        public WinterMultiplierPersistState(float lastMultiplier, bool wasWinterActive)
        {
            LastMultiplier = lastMultiplier;
            WasWinterActive = wasWinterActive;
        }

        public float LastMultiplier { get; }
        public bool WasWinterActive { get; }
    }

    public static class WinterMultiplierCodec
    {
        public static void Write<TWriter>(in WinterMultiplierPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);
            KeyedSerializer.WriteField(writer, "m_LastMultiplier", state.LastMultiplier);
            KeyedSerializer.WriteField(writer, "m_WasWinterActive", state.WasWinterActive);
        }

        public static void Read<TReader>(TReader reader, out WinterMultiplierPersistState state)
            where TReader : IReader
        {
            float lastMultiplier = 1.0f;
            bool wasWinterActive = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_LastMultiplier":
                        lastMultiplier = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "m_LastMultiplier", 1.0f);
                        break;
                    case "m_WasWinterActive":
                        wasWinterActive = KeyedSerializer.ReadBool(reader, tag, "m_WasWinterActive");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new WinterMultiplierPersistState(lastMultiplier, wasWinterActive);
        }
    }

    public readonly struct PowerPlantDisasterPersistState
    {
        public PowerPlantDisasterPersistState(double gameHour, float lastCheckHour)
        {
            GameHour = gameHour;
            LastCheckHour = lastCheckHour;
        }

        public double GameHour { get; }
        public float LastCheckHour { get; }
    }

    public static class PowerPlantDisasterCodec
    {
        public static void Write<TWriter>(in PowerPlantDisasterPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);
            KeyedSerializer.WriteField(writer, "m_GameHour", state.GameHour);
            KeyedSerializer.WriteField(writer, "m_LastCheckHour", state.LastCheckHour);
        }

        public static void Read<TReader>(TReader reader, out PowerPlantDisasterPersistState state)
            where TReader : IReader
        {
            double gameHour = 0.0;
            float lastCheckHour = -1f;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_GameHour":
                        gameHour = KeyedSerializer.ReadSafeDouble(reader, tag, "m_GameHour", 0.0);
                        break;
                    case "m_LastCheckHour":
                        lastCheckHour = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "m_LastCheckHour", -1f);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new PowerPlantDisasterPersistState(gameHour, lastCheckHour);
        }
    }

    public readonly struct CountermeasuresUIPersistState
    {
        public CountermeasuresUIPersistState(bool arrestedDismissed)
        {
            ArrestedDismissed = arrestedDismissed;
        }

        public bool ArrestedDismissed { get; }
    }

    public static class CountermeasuresUICodec
    {
        public static void Write<TWriter>(in CountermeasuresUIPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "m_ArrestedDismissed", state.ArrestedDismissed);
        }

        public static void Read<TReader>(TReader reader, out CountermeasuresUIPersistState state)
            where TReader : IReader
        {
            bool arrestedDismissed = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_ArrestedDismissed":
                        arrestedDismissed = KeyedSerializer.ReadBool(reader, tag, "m_ArrestedDismissed");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new CountermeasuresUIPersistState(arrestedDismissed);
        }
    }

    public readonly struct CrisisEconomicsPersistState
    {
        public CrisisEconomicsPersistState(
            bool crisisActive,
            int crisisStartDay,
            bool loansBlocked,
            int savedCreditworthiness,
            bool hasSavedCreditworthiness,
            bool tourismPenaltyApplied,
            float preWarCommercePenalty,
            bool preWarLoansBlocked)
        {
            CrisisActive = crisisActive;
            CrisisStartDay = crisisStartDay;
            LoansBlocked = loansBlocked;
            SavedCreditworthiness = savedCreditworthiness;
            HasSavedCreditworthiness = hasSavedCreditworthiness;
            TourismPenaltyApplied = tourismPenaltyApplied;
            PreWarCommercePenalty = preWarCommercePenalty;
            PreWarLoansBlocked = preWarLoansBlocked;
        }

        public bool CrisisActive { get; }
        public int CrisisStartDay { get; }
        public bool LoansBlocked { get; }
        public int SavedCreditworthiness { get; }
        public bool HasSavedCreditworthiness { get; }
        public bool TourismPenaltyApplied { get; }
        public float PreWarCommercePenalty { get; }
        public bool PreWarLoansBlocked { get; }
    }

    public static class CrisisEconomicsCodec
    {
        public const int MaxCrisisStartDay = 100000;
        public const int MaxSavedCreditworthiness = 1000000000;

        public static void Write<TWriter>(in CrisisEconomicsPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 8);
            KeyedSerializer.WriteField(writer, "m_CrisisActive", state.CrisisActive);
            KeyedSerializer.WriteField(writer, "m_CrisisStartDay", state.CrisisStartDay);
            KeyedSerializer.WriteField(writer, "m_LoansBlocked", state.LoansBlocked);
            KeyedSerializer.WriteField(writer, "m_SavedCreditworthiness", state.SavedCreditworthiness);
            KeyedSerializer.WriteField(writer, "m_HasSavedCreditworthiness", state.HasSavedCreditworthiness);
            KeyedSerializer.WriteField(writer, "m_TourismPenaltyApplied", state.TourismPenaltyApplied);
            KeyedSerializer.WriteField(writer, "m_PreWarCommercePenalty", state.PreWarCommercePenalty);
            KeyedSerializer.WriteField(writer, "m_PreWarLoansBlocked", state.PreWarLoansBlocked);
        }

        public static void Read<TReader>(TReader reader, out CrisisEconomicsPersistState state)
            where TReader : IReader
        {
            bool crisisActive = false;
            int crisisStartDay = 0;
            bool loansBlocked = false;
            int savedCreditworthiness = 0;
            bool hasSavedCreditworthiness = false;
            bool tourismPenaltyApplied = false;
            float preWarCommercePenalty = 0f;
            bool preWarLoansBlocked = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_CrisisActive":
                        crisisActive = KeyedSerializer.ReadBool(reader, tag, "m_CrisisActive");
                        break;
                    case "m_CrisisStartDay":
                        crisisStartDay = KeyedSerializer.ReadBoundedInt(reader, tag, "m_CrisisStartDay", 0, MaxCrisisStartDay, 0);
                        break;
                    case "m_LoansBlocked":
                        loansBlocked = KeyedSerializer.ReadBool(reader, tag, "m_LoansBlocked");
                        break;
                    case "m_SavedCreditworthiness":
                        savedCreditworthiness = KeyedSerializer.ReadBoundedInt(reader, tag, "m_SavedCreditworthiness", 0, MaxSavedCreditworthiness, 0);
                        break;
                    case "m_HasSavedCreditworthiness":
                        hasSavedCreditworthiness = KeyedSerializer.ReadBool(reader, tag, "m_HasSavedCreditworthiness");
                        break;
                    case "m_TourismPenaltyApplied":
                        tourismPenaltyApplied = KeyedSerializer.ReadBool(reader, tag, "m_TourismPenaltyApplied");
                        break;
                    case "m_PreWarCommercePenalty":
                        preWarCommercePenalty = KeyedSerializer.ReadSafeFloat(reader, tag, "m_PreWarCommercePenalty", 0f, 1f, 0f);
                        break;
                    case "m_PreWarLoansBlocked":
                        preWarLoansBlocked = KeyedSerializer.ReadBool(reader, tag, "m_PreWarLoansBlocked");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new CrisisEconomicsPersistState(
                crisisActive,
                crisisStartDay,
                loansBlocked,
                savedCreditworthiness,
                hasSavedCreditworthiness,
                tourismPenaltyApplied,
                preWarCommercePenalty,
                preWarLoansBlocked);
        }
    }

    public readonly struct CrisisMonitorPersistState
    {
        public CrisisMonitorPersistState(
            float crisisLevel,
            int totalPopulation,
            int affectedPopulation,
            int lastUpdateDay)
        {
            CrisisLevel = crisisLevel;
            TotalPopulation = totalPopulation;
            AffectedPopulation = affectedPopulation;
            LastUpdateDay = lastUpdateDay;
        }

        public float CrisisLevel { get; }
        public int TotalPopulation { get; }
        public int AffectedPopulation { get; }
        public int LastUpdateDay { get; }
    }

    public static class CrisisMonitorCodec
    {
        public const int MaxPopulation = 10000000;
        public const int MaxDay = 100000;

        public static void Write<TWriter>(in CrisisMonitorPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 4);
            KeyedSerializer.WriteField(writer, "crisisLevel", state.CrisisLevel);
            KeyedSerializer.WriteField(writer, "totalPop", state.TotalPopulation);
            KeyedSerializer.WriteField(writer, "affectedPop", state.AffectedPopulation);
            KeyedSerializer.WriteField(writer, "lastUpdateDay", state.LastUpdateDay);
        }

        public static void Read<TReader>(TReader reader, out CrisisMonitorPersistState state)
            where TReader : IReader
        {
            float crisisLevel = 0f;
            int totalPopulation = 0;
            int affectedPopulation = 0;
            int lastUpdateDay = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "crisisLevel":
                        crisisLevel = KeyedSerializer.ReadSafeFloat(reader, tag, "crisisLevel", 0f, 100f, 0f);
                        break;
                    case "totalPop":
                        totalPopulation = KeyedSerializer.ReadBoundedInt(reader, tag, "totalPop", 0, MaxPopulation, 0);
                        break;
                    case "affectedPop":
                        affectedPopulation = KeyedSerializer.ReadBoundedInt(reader, tag, "affectedPop", 0, MaxPopulation, 0);
                        break;
                    case "lastUpdateDay":
                        lastUpdateDay = KeyedSerializer.ReadBoundedInt(reader, tag, "lastUpdateDay", 0, MaxDay, 0);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new CrisisMonitorPersistState(
                crisisLevel,
                totalPopulation,
                affectedPopulation,
                lastUpdateDay);
        }
    }

    public readonly struct ScandalPersistState
    {
        public ScandalPersistState(
            float scandalPenalty,
            int lastScandalDay,
            int lastProcessedDay,
            ulong randomState,
            bool baselineSeeded)
        {
            ScandalPenalty = scandalPenalty;
            LastScandalDay = lastScandalDay;
            LastProcessedDay = lastProcessedDay;
            RandomState = randomState;
            BaselineSeeded = baselineSeeded;
        }

        public float ScandalPenalty { get; }
        public int LastScandalDay { get; }
        public int LastProcessedDay { get; }
        public ulong RandomState { get; }
        public bool BaselineSeeded { get; }
    }

    public static class ScandalCodec
    {
        public const float AbsoluteScandalPenaltyMax = 100f;
        public const int MaxDay = 100000;

        public static void Write<TWriter>(in ScandalPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 5);
            KeyedSerializer.WriteField(writer, "scandalPenalty", state.ScandalPenalty);
            KeyedSerializer.WriteField(writer, "lastScandalDay", state.LastScandalDay);
            KeyedSerializer.WriteField(writer, "lastProcessedDay", state.LastProcessedDay);
            KeyedSerializer.WriteField(writer, "randomState", unchecked((long)state.RandomState));
            KeyedSerializer.WriteField(writer, "baselineSeeded", state.BaselineSeeded);
        }

        public static void Read<TReader>(TReader reader, out ScandalPersistState state)
            where TReader : IReader
        {
            float scandalPenalty = 0f;
            int lastScandalDay = -1;
            int lastProcessedDay = -1;
            ulong randomState = 0;
            bool? baselineSeeded = null;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "scandalPenalty":
                        scandalPenalty = KeyedSerializer.ReadSafeFloat(reader, tag, "scandalPenalty", 0f, AbsoluteScandalPenaltyMax, 0f);
                        break;
                    case "lastScandalDay":
                        lastScandalDay = KeyedSerializer.ReadBoundedInt(reader, tag, "lastScandalDay", -1, MaxDay, -1);
                        break;
                    case "lastProcessedDay":
                        lastProcessedDay = KeyedSerializer.ReadBoundedInt(reader, tag, "lastProcessedDay", -1, MaxDay, -1);
                        break;
                    case "randomState":
                        if (KeyedSerializer.ExpectTag(reader, tag, TypeTag.I64, "randomState"))
                        {
                            reader.Read(out long value);
                            randomState = unchecked((ulong)value);
                        }
                        break;
                    case "baselineSeeded":
                        baselineSeeded = KeyedSerializer.ReadBool(reader, tag, "baselineSeeded");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new ScandalPersistState(
                scandalPenalty,
                lastScandalDay,
                lastProcessedDay,
                randomState,
                baselineSeeded ?? (lastScandalDay >= 0));
        }
    }

    public readonly struct DonorConferencePersistState
    {
        public DonorConferencePersistState(
            int usesRemaining,
            float cooldownDaysRemaining,
            int activeGenerators,
            bool sanctionsActive,
            float sanctionDaysRemaining,
            float tradePenalty,
            int generatorMW,
            int gameDay,
            int generatorDecayCounter,
            float importTrustPenalty,
            bool hasLastReplenishedAct,
            Act lastReplenishedAct,
            bool sawLastReplenishedAct,
            bool sawUsesRemaining,
            bool sawGeneratorMW)
        {
            UsesRemaining = usesRemaining;
            CooldownDaysRemaining = cooldownDaysRemaining;
            ActiveGenerators = activeGenerators;
            SanctionsActive = sanctionsActive;
            SanctionDaysRemaining = sanctionDaysRemaining;
            TradePenalty = tradePenalty;
            GeneratorMW = generatorMW;
            GameDay = gameDay;
            GeneratorDecayCounter = generatorDecayCounter;
            ImportTrustPenalty = importTrustPenalty;
            HasLastReplenishedAct = hasLastReplenishedAct;
            LastReplenishedAct = lastReplenishedAct;
            SawLastReplenishedAct = sawLastReplenishedAct;
            SawUsesRemaining = sawUsesRemaining;
            SawGeneratorMW = sawGeneratorMW;
        }

        public int UsesRemaining { get; }
        public float CooldownDaysRemaining { get; }
        public int ActiveGenerators { get; }
        public bool SanctionsActive { get; }
        public float SanctionDaysRemaining { get; }
        public float TradePenalty { get; }
        public int GeneratorMW { get; }
        public int GameDay { get; }
        public int GeneratorDecayCounter { get; }
        public float ImportTrustPenalty { get; }
        public bool HasLastReplenishedAct { get; }
        public Act LastReplenishedAct { get; }
        public bool SawLastReplenishedAct { get; }
        public bool SawUsesRemaining { get; }
        public bool SawGeneratorMW { get; }
    }

    public static class DonorConferenceCodec
    {
        public const float MaxDonorDays = 3650f;
        public const float MaxTradePenalty = 1f;
        public const float AbsoluteTradePenaltyMax = 100f;
        public const float MaxImportTrustPenalty = 100f;
        public const int MaxGameDay = 100000;
        public const int MaxGeneratorMW = 100000;

        public static void Write<TWriter>(in DonorConferencePersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 12);
            KeyedSerializer.WriteField(writer, "usesRemaining", state.UsesRemaining);
            KeyedSerializer.WriteField(writer, "cooldownDays", state.CooldownDaysRemaining);
            KeyedSerializer.WriteField(writer, "activeGenerators", state.ActiveGenerators);
            KeyedSerializer.WriteField(writer, "sanctionsActive", state.SanctionsActive);
            KeyedSerializer.WriteField(writer, "sanctionDays", state.SanctionDaysRemaining);
            KeyedSerializer.WriteField(writer, "tradePenalty", state.TradePenalty);
            KeyedSerializer.WriteField(writer, "stateGeneratorMW", state.GeneratorMW);
            KeyedSerializer.WriteField(writer, "gameDay", state.GameDay);
            KeyedSerializer.WriteField(writer, "generatorDecayCounter", state.GeneratorDecayCounter);
            KeyedSerializer.WriteField(writer, "importTrustPenalty", state.ImportTrustPenalty);
            KeyedSerializer.WriteField(writer, "hasLastReplenishedAct", state.HasLastReplenishedAct);
            KeyedSerializer.WriteField(writer, "lastReplenishedAct", (int)state.LastReplenishedAct);
        }

        public static void Read<TReader>(
            TReader reader,
            int defaultUsesRemaining,
            int defaultGeneratorMW,
            int maxUses,
            int maxGenerators,
            out DonorConferencePersistState state)
            where TReader : IReader
        {
            int usesRemaining = defaultUsesRemaining;
            float cooldownDaysRemaining = 0f;
            int activeGenerators = 0;
            bool sanctionsActive = false;
            float sanctionDaysRemaining = 0f;
            float tradePenalty = 0f;
            int generatorMW = defaultGeneratorMW;
            int gameDay = 0;
            int generatorDecayCounter = 0;
            float importTrustPenalty = 0f;
            bool hasLastReplenishedAct = false;
            Act lastReplenishedAct = Act.PreWar;
            bool sawLastReplenishedAct = false;
            bool sawUsesRemaining = false;
            bool sawGeneratorMW = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "usesRemaining":
                        usesRemaining = KeyedSerializer.ReadClampedInt(reader, tag, "usesRemaining", 0, maxUses, defaultUsesRemaining);
                        sawUsesRemaining = true;
                        break;
                    case "cooldownDays":
                        cooldownDaysRemaining = KeyedSerializer.ReadSafeFloat(reader, tag, "cooldownDays", 0f, MaxDonorDays, 0f);
                        break;
                    case "activeGenerators":
                        activeGenerators = KeyedSerializer.ReadClampedInt(reader, tag, "activeGenerators", 0, maxGenerators);
                        break;
                    case "sanctionsActive":
                        sanctionsActive = KeyedSerializer.ReadBool(reader, tag, "sanctionsActive");
                        break;
                    case "sanctionDays":
                        sanctionDaysRemaining = KeyedSerializer.ReadSafeFloat(reader, tag, "sanctionDays", 0f, MaxDonorDays, 0f);
                        break;
                    case "tradePenalty":
                        tradePenalty = KeyedSerializer.ReadSafeFloat(reader, tag, "tradePenalty", 0f, MaxTradePenalty, 0f);
                        break;
                    case "stateGeneratorMW":
                        generatorMW = KeyedSerializer.ReadClampedInt(reader, tag, "stateGeneratorMW", -1, MaxGeneratorMW, defaultGeneratorMW);
                        sawGeneratorMW = true;
                        break;
                    case "gameDay":
                        gameDay = KeyedSerializer.ReadMonotonicCounter(reader, tag, "gameDay", 0, MaxGameDay);
                        break;
                    case "generatorDecayCounter":
                        generatorDecayCounter = KeyedSerializer.ReadClampedInt(reader, tag, "generatorDecayCounter", 0, (int)MaxDonorDays);
                        break;
                    case "importTrustPenalty":
                        importTrustPenalty = KeyedSerializer.ReadSafeFloat(reader, tag, "importTrustPenalty", 0f, MaxImportTrustPenalty, 0f);
                        break;
                    case "hasLastReplenishedAct":
                        hasLastReplenishedAct = KeyedSerializer.ReadBool(reader, tag, "hasLastReplenishedAct");
                        sawLastReplenishedAct = true;
                        break;
                    case "lastReplenishedAct":
                    {
                        int raw = KeyedSerializer.ReadBoundedInt(reader, tag, "lastReplenishedAct", 0, 255, 0);
                        lastReplenishedAct = System.Enum.IsDefined(typeof(Act), (byte)raw)
                            ? (Act)(byte)raw
                            : Act.PreWar;
                        sawLastReplenishedAct = true;
                        break;
                    }
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new DonorConferencePersistState(
                usesRemaining,
                cooldownDaysRemaining,
                activeGenerators,
                sanctionsActive,
                sanctionDaysRemaining,
                tradePenalty,
                generatorMW,
                gameDay,
                generatorDecayCounter,
                importTrustPenalty,
                hasLastReplenishedAct,
                lastReplenishedAct,
                sawLastReplenishedAct,
                sawUsesRemaining,
                sawGeneratorMW);
        }
    }
}
