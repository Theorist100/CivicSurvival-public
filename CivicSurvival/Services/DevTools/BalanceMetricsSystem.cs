#if DEBUG
using System;
using CivicSurvival.Core.Features.Wellbeing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Game;
using Unity.Entities;
using Colossal.Logging;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Services.DevTools
{
    /// <summary>Data point for history charts.</summary>
    public struct HistoryPoint
    {
        public float GameHour { get; set; }
        public float Value { get; set; }
    }

    /// <summary>
    /// DEBUG ONLY: Power/district balance metrics, blackout tracking, history charts, CSV logging.
    /// </summary>
    [ActIndependent]
    [GameModeGatedSystem(GameMode.Game, "Debug UI metrics tick only in loaded game mode; main menu/editor have no city to measure.")]
    public partial class BalanceMetricsSystem : ThrottledUISystemBase
    {
        private static readonly LogContext Log = new("BalanceMetrics");

        private EntityQuery m_PowerGridQuery;
        private EntityQuery m_DistrictPowerQuery;
        private EntityQuery m_CountermeasuresQuery;
        private BufferLookup<DistrictPowerEntry> m_DistrictPowerEntryLookup;
        private IDistrictStateReader? m_DistrictState;

        private ProfiledBinding<float> m_SeverityScore = null!;
        private ProfiledBinding<float> m_BlackoutPercent = null!;
        private ProfiledBinding<float> m_AvgHappinessPenalty = null!;
        private ProfiledBinding<float> m_AvgCommercePenalty = null!;
        private ProfiledBinding<int> m_AffectedDistricts = null!;
        private ProfiledBinding<int> m_TotalDistricts = null!;
        private ProfiledBinding<int> m_PowerBalance = null!;
        private ProfiledBinding<int> m_Production = null!;
        private ProfiledBinding<int> m_Consumption = null!;
        private ProfiledBinding<int> m_BlackoutedMW = null!;
        private ProfiledBinding<int> m_ResidentialMW = null!;
        private ProfiledBinding<int> m_CommercialMW = null!;
        private ProfiledBinding<int> m_IndustrialMW = null!;
        private ProfiledBinding<int> m_OfficeMW = null!;
        private ProfiledBinding<string> m_CityType = null!;
        private ProfiledBinding<int> m_BuildingsInBlackout = null!;
        private ProfiledBinding<float> m_BlackoutDurationMinutes = null!;
        private ProfiledBinding<string> m_PowerHistory = null!;
        private ProfiledBinding<string> m_SeverityHistory = null!;
        private ProfiledBinding<string> m_CorruptionHistory = null!;

        private const int HISTORY_SIZE = 24;
        private List<HistoryPoint> m_PowerProductionHistory = new(HISTORY_SIZE);
        private List<HistoryPoint> m_PowerConsumptionHistory = new(HISTORY_SIZE);
        private List<HistoryPoint> m_SeverityScoreHistory = new(HISTORY_SIZE);
        private List<HistoryPoint> m_CorruptionScoreHistory = new(HISTORY_SIZE);

        internal float CurrentSeverity { get; private set; }
        internal float CurrentBlackoutPercent { get; private set; }
        internal float CurrentHappiness { get; private set; }
        internal float CurrentCommerce { get; private set; }
        internal int CurrentAffected { get; private set; }

        private float m_BlackoutStartHour = -1f;
        private float m_TotalBlackoutMinutes;

        // CIVIC050: Pre-allocated scratch StringBuilders for CSV/history serialization
        private readonly StringBuilder m_CsvSbScratch = new();
        private readonly StringBuilder m_PowerHistorySbScratch = new();
        private readonly StringBuilder m_HistorySbScratch = new();

        // OPTIMIZATION: Updated from 60 to 500 frames (~8 seconds)
        protected override int UpdateInterval => 500;

        private float m_LastLogGameHour = -1f;
        private string m_CsvPath = "";
        private bool m_CsvInitialized;
        private bool m_CsvWarningLogged;

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            ResetRuntimeState();
            // Gate ticks to game mode: in main menu / editor the system has no
            // city to measure and CSV/binding writes would clobber the previous
            // session. ThrottledUISystemBase runs in UIUpdate (independent of
            // sim pause), so a per-call guard is the only alternative — toggling
            // Enabled here is one vanilla-event-driven point of control instead.
            Enabled = (mode == GameMode.Game);
            try
            {
                if (World != null && World.IsCreated)
                    base.OnGamePreload(purpose, mode);
            }
            catch (InvalidOperationException ex)
            {
                Log.Warn($"[DEBUG] {nameof(BalanceMetricsSystem)} OnGamePreload skipped: {ex}");
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PowerGridQuery = GetEntityQuery(ComponentType.ReadOnly<PowerGridSingleton>());
            m_DistrictPowerQuery = GetEntityQuery(ComponentType.ReadOnly<DistrictPowerBufferSingleton>());
            m_CountermeasuresQuery = GetEntityQuery(ComponentType.ReadOnly<CountermeasuresCoreFsm>());
            m_DistrictPowerEntryLookup = GetBufferLookup<DistrictPowerEntry>(true);

            m_CsvPath = Path.Combine(ModPaths.LogsDirectory, ModPaths.BalanceMetricsFile);
            CreateBindings();

            // Initial state: stay off until OnGamePreload confirms GameMode.Game.
            // Mod.OnLoad runs in main menu boot, so without this we would tick
            // once before any preload event fires.
#pragma warning disable CIVIC093 // Intentional lifecycle gate: Enabled is re-toggled by OnGamePreload when GameMode.Game arrives, not an error/failure path that needs Mod.Log.Warn.
            Enabled = false;
#pragma warning restore CIVIC093
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            if (m_DistrictState == null && ServiceRegistry.IsInitialized)
                m_DistrictState = ServiceRegistry.TryGet<IDistrictStateReader>();
        }

        private void ResetRuntimeState()
        {
            m_PowerProductionHistory.Clear();
            m_PowerConsumptionHistory.Clear();
            m_SeverityScoreHistory.Clear();
            m_CorruptionScoreHistory.Clear();
            m_BlackoutStartHour = -1f;
            m_TotalBlackoutMinutes = 0f;
            m_LastLogGameHour = -1f;
            m_CsvInitialized = false;
            m_CsvWarningLogged = false;
            m_CsvSbScratch.Clear();
            m_PowerHistorySbScratch.Clear();
            m_HistorySbScratch.Clear();
        }

        private void CreateBindings()
        {
            m_SeverityScore = new ProfiledBinding<float>(Group, Debug_SeverityScore, 0f);
            m_BlackoutPercent = new ProfiledBinding<float>(Group, Debug_BlackoutPercent, 0f);
            m_AvgHappinessPenalty = new ProfiledBinding<float>(Group, Debug_HappinessPenalty, 0f);
            m_AvgCommercePenalty = new ProfiledBinding<float>(Group, Debug_CommercePenalty, 0f);
            m_AffectedDistricts = new ProfiledBinding<int>(Group, Debug_AffectedDistricts, 0);
            m_TotalDistricts = new ProfiledBinding<int>(Group, Debug_TotalDistricts, 0);
            m_PowerBalance = new ProfiledBinding<int>(Group, Debug_PowerBalance, 0);
            m_Production = new ProfiledBinding<int>(Group, Debug_Production, 0);
            m_Consumption = new ProfiledBinding<int>(Group, Debug_Consumption, 0);
            m_BlackoutedMW = new ProfiledBinding<int>(Group, Debug_BlackoutedMW, 0);
            m_ResidentialMW = new ProfiledBinding<int>(Group, Debug_ResidentialMW, 0);
            m_CommercialMW = new ProfiledBinding<int>(Group, Debug_CommercialMW, 0);
            m_IndustrialMW = new ProfiledBinding<int>(Group, Debug_IndustrialMW, 0);
            m_OfficeMW = new ProfiledBinding<int>(Group, Debug_OfficeMW, 0);
            m_CityType = new ProfiledBinding<string>(Group, Debug_CityType, "Mixed");
            m_BuildingsInBlackout = new ProfiledBinding<int>(Group, Debug_BuildingsInBlackout, 0);
            m_BlackoutDurationMinutes = new ProfiledBinding<float>(Group, Debug_BlackoutDuration, 0f);
            m_PowerHistory = new ProfiledBinding<string>(Group, Debug_PowerHistory, "[]");
            m_SeverityHistory = new ProfiledBinding<string>(Group, Debug_SeverityHistory, "[]");
            m_CorruptionHistory = new ProfiledBinding<string>(Group, Debug_CorruptionHistory, "[]");

            AddBinding(m_SeverityScore.Binding);
            AddBinding(m_BlackoutPercent.Binding);
            AddBinding(m_AvgHappinessPenalty.Binding);
            AddBinding(m_AvgCommercePenalty.Binding);
            AddBinding(m_AffectedDistricts.Binding);
            AddBinding(m_TotalDistricts.Binding);
            AddBinding(m_PowerBalance.Binding);
            AddBinding(m_Production.Binding);
            AddBinding(m_Consumption.Binding);
            AddBinding(m_BlackoutedMW.Binding);
            AddBinding(m_ResidentialMW.Binding);
            AddBinding(m_CommercialMW.Binding);
            AddBinding(m_IndustrialMW.Binding);
            AddBinding(m_OfficeMW.Binding);
            AddBinding(m_CityType.Binding);
            AddBinding(m_BuildingsInBlackout.Binding);
            AddBinding(m_BlackoutDurationMinutes.Binding);
            AddBinding(m_PowerHistory.Binding);
            AddBinding(m_SeverityHistory.Binding);
            AddBinding(m_CorruptionHistory.Binding);
        }

        protected override void OnThrottledUpdate()
        {
            m_DistrictPowerEntryLookup.Update(this);
            CalculateMetrics();
            TryLogToCSV();
        }

        private void CalculateMetrics()
        {
            if (!ServiceRegistry.IsInitialized) return;
            if (!m_DistrictPowerQuery.TryGetSingletonEntity<DistrictPowerBufferSingleton>(out var singletonEntity)) return;
            m_DistrictState ??= ServiceRegistry.TryGet<IDistrictStateReader>();
            if (m_DistrictState == null) return;
            if (!m_DistrictPowerEntryLookup.TryGetBuffer(singletonEntity, out var powerBuffer)) return;

            var snapshot = m_DistrictState.TakeSnapshot();

            int totalDistricts = powerBuffer.Length;
            int affectedDistricts = 0;
            float totalHappiness = 0f;
            float totalCommerce = 0f;
            int blackoutedMW = 0;
            int totalMW = 0;
            int totalResidentialMW = 0;
            int totalCommercialMW = 0;
            int totalIndustrialMW = 0;
            int totalOfficeMW = 0;
            int buildingsInBlackout = 0;

            for (int i = 0; i < powerBuffer.Length; i++)
            {
                var entry = powerBuffer[i];
                int districtIndex = entry.District.Index;
                var power = entry.Data;

                totalMW += power.TotalMW;
                totalResidentialMW += power.ResidentialMW;
                totalCommercialMW += power.CommercialMW;
                totalIndustrialMW += power.IndustrialMW;
                totalOfficeMW += power.OfficeMW;

                if (snapshot.IsDistrictInBlackout(districtIndex))
                {
                    blackoutedMW += power.TotalMW;
                    buildingsInBlackout += power.BuildingCount;
                }

#pragma warning disable CIVIC097 // DistrictStateSnapshot keys are logical district ids, not raw entity refs.
                if (snapshot.DistrictPenalties.TryGetValue(districtIndex, out var penalties))
#pragma warning restore CIVIC097
                {
                    affectedDistricts++;
                    totalHappiness += penalties.TotalHappinessPenalty;
                    totalCommerce += penalties.TotalCommercePenalty;
                }
            }

            CurrentBlackoutPercent = totalMW > 0 ? (float)blackoutedMW / totalMW * 100f : 0f;
            CurrentHappiness = affectedDistricts > 0 ? totalHappiness / affectedDistricts : 0f;
            CurrentCommerce = affectedDistricts > 0 ? totalCommerce / affectedDistricts : 0f;
            CurrentAffected = affectedDistricts;
            CurrentSeverity =
                CurrentBlackoutPercent * 0.4f +
                (CurrentHappiness * 100f) * 0.3f +
                (CurrentCommerce * 100f) * 0.3f;

            float gameHour = m_DistrictState.GameHour;
            float absoluteGameHour = GetAbsoluteGameHour(gameHour);
            if (affectedDistricts > 0)
            {
                if (m_BlackoutStartHour < 0)
                    m_BlackoutStartHour = absoluteGameHour;
                else
                {
                    float duration = Math.Max(0f, absoluteGameHour - m_BlackoutStartHour);
                    m_TotalBlackoutMinutes = duration * 60f;
                }
            }
            else
            {
                m_BlackoutStartHour = -1f;
                m_TotalBlackoutMinutes = 0f;
            }

            string cityType = DetermineCityType(totalResidentialMW, totalCommercialMW, totalIndustrialMW, totalOfficeMW);

            int powerBalance = 0;
            int production = 0;
            int consumption = 0;
            if (m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var grid))
            {
                powerBalance = grid.Balance / 1000;
                production = grid.Production / 1000;
                consumption = grid.Consumption / 1000;
            }

            m_SeverityScore.Update(CurrentSeverity);
            m_BlackoutPercent.Update(CurrentBlackoutPercent);
            m_AvgHappinessPenalty.Update(CurrentHappiness);
            m_AvgCommercePenalty.Update(CurrentCommerce);
            m_AffectedDistricts.Update(CurrentAffected);
            m_TotalDistricts.Update(totalDistricts);
            m_PowerBalance.Update(powerBalance);
            m_Production.Update(production);
            m_Consumption.Update(consumption);
            m_BlackoutedMW.Update(blackoutedMW);
            m_ResidentialMW.Update(totalResidentialMW);
            m_CommercialMW.Update(totalCommercialMW);
            m_IndustrialMW.Update(totalIndustrialMW);
            m_OfficeMW.Update(totalOfficeMW);
            m_CityType.Update(cityType);
            m_BuildingsInBlackout.Update(buildingsInBlackout);
            m_BlackoutDurationMinutes.Update(m_TotalBlackoutMinutes);
        }

        private string DetermineCityType(int res, int com, int ind, int off)
        {
            int total = res + com + ind + off;
            if (total == 0) return "Empty";

            float resPercent = (float)res / total * 100f;
            float comPercent = (float)com / total * 100f;
            float indPercent = (float)ind / total * 100f;
            float offPercent = (float)off / total * 100f;

            if (resPercent > 40) return $"Residential ({resPercent:F0}%)";
            if (indPercent > 40) return $"Industrial ({indPercent:F0}%)";
            if (comPercent > 40) return $"Commercial ({comPercent:F0}%)";
            if (offPercent > 40) return $"Office ({offPercent:F0}%)";

            var sectors = new[] {
                (resPercent, "Res"),
                (comPercent, "Com"),
                (indPercent, "Ind"),
                (offPercent, "Off")
            };
            Array.Sort(sectors, (a, b) => b.Item1.CompareTo(a.Item1));
            return $"Mixed ({sectors[0].Item2}/{sectors[1].Item2})";
        }

        private void TryLogToCSV()
        {
            if (!ServiceRegistry.IsInitialized) return;
            if (m_DistrictState == null) return;

            float gameHour = m_DistrictState.GameHour;
            float absoluteGameHour = GetAbsoluteGameHour(gameHour);
            if ((int)absoluteGameHour == (int)m_LastLogGameHour) return;
            m_LastLogGameHour = absoluteGameHour;

            RecordHistoryPoint(gameHour);

            if (!m_CsvInitialized)
            {
                InitializeCsv();
                m_CsvInitialized = true;
            }

            try
            {
                int csvBalance = 0;
                int csvProduction = 0;
                int csvConsumption = 0;
                if (m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var csvGrid))
                {
                    csvBalance = csvGrid.Balance / 1000;
                    csvProduction = csvGrid.Production / 1000;
                    csvConsumption = csvGrid.Consumption / 1000;
                }

                m_CsvSbScratch.Clear();
                m_CsvSbScratch.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                m_CsvSbScratch.Append(',');
                m_CsvSbScratch.Append(gameHour.ToString("F1", CultureInfo.InvariantCulture));
                m_CsvSbScratch.Append(',');
                m_CsvSbScratch.Append(CurrentSeverity.ToString("F2", CultureInfo.InvariantCulture));
                m_CsvSbScratch.Append(',');
                m_CsvSbScratch.Append(CurrentBlackoutPercent.ToString("F2", CultureInfo.InvariantCulture));
                m_CsvSbScratch.Append(',');
                m_CsvSbScratch.Append((CurrentHappiness * 100f).ToString("F1", CultureInfo.InvariantCulture));
                m_CsvSbScratch.Append(',');
                m_CsvSbScratch.Append((CurrentCommerce * 100f).ToString("F1", CultureInfo.InvariantCulture));
                m_CsvSbScratch.Append(',');
                m_CsvSbScratch.Append(CurrentAffected);
                m_CsvSbScratch.Append(',');
                m_CsvSbScratch.Append(csvBalance);
                m_CsvSbScratch.Append(',');
                m_CsvSbScratch.Append(csvProduction);
                m_CsvSbScratch.Append(',');
                m_CsvSbScratch.Append(csvConsumption);
                m_CsvSbScratch.AppendLine();

                File.AppendAllText(m_CsvPath, m_CsvSbScratch.ToString());
                m_CsvWarningLogged = false;
            }
            catch (Exception ex)
            {
                if (!m_CsvWarningLogged)
                {
                    m_CsvWarningLogged = true;
                    Log.WarnException("[DEBUG] Failed to write CSV", ex);
                }
            }
        }

        private void InitializeCsv()
        {
            try
            {
                const string header = "timestamp,gameHour,severityScore,blackoutPercent,happinessPenalty,commercePenalty,affectedDistricts,powerBalance,production,consumption\n";
                string? dir = Path.GetDirectoryName(m_CsvPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                AtomicFileWriter.WriteAllText(m_CsvPath, header);
                Log.Info($"[DEBUG] CSV initialized: {m_CsvPath}");
            }
            catch (Exception ex)
            {
                if (!m_CsvWarningLogged)
                {
                    m_CsvWarningLogged = true;
                    Log.WarnException("[DEBUG] Failed to initialize CSV", ex);
                }
            }
        }

        private static float GetAbsoluteGameHour(float fallbackHour)
        {
            if (!GameTimeSystem.TryGetGameHours(out var gameHours))
                return fallbackHour;
            return gameHours > 0f ? gameHours : fallbackHour;
        }

        private void RecordHistoryPoint(float gameHour)
        {
            int histProduction = 0;
            int histConsumption = 0;
            if (m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var histGrid))
            {
                histProduction = histGrid.Production / 1000;
                histConsumption = histGrid.Consumption / 1000;
            }

            AddToHistory(m_PowerProductionHistory, gameHour, histProduction);
            AddToHistory(m_PowerConsumptionHistory, gameHour, histConsumption);
            AddToHistory(m_SeverityScoreHistory, gameHour, CurrentSeverity);

            float corruptionScore = 0f;
            if (m_CountermeasuresQuery.TryGetSingleton<CountermeasuresCoreFsm>(out var cmCore))
                corruptionScore = cmCore.CorruptionScore;
            AddToHistory(m_CorruptionScoreHistory, gameHour, corruptionScore);

            m_PowerHistory.Update(SerializePowerHistory());
            m_SeverityHistory.Update(SerializeHistory(m_SeverityScoreHistory));
            m_CorruptionHistory.Update(SerializeHistory(m_CorruptionScoreHistory));
        }

        private void AddToHistory(List<HistoryPoint> history, float gameHour, float value)
        {
            if (history.Count >= HISTORY_SIZE)
                history.RemoveAt(0);
            history.Add(new HistoryPoint { GameHour = gameHour, Value = value });
        }

        private string SerializePowerHistory()
        {
            m_PowerHistorySbScratch.Clear();
            m_PowerHistorySbScratch.Append("{\"production\":[");
            for (int i = 0; i < m_PowerProductionHistory.Count; i++)
            {
                if (i > 0) m_PowerHistorySbScratch.Append(',');
                var p = m_PowerProductionHistory[i];
                // Explicit digit-placeholder custom format (not the standard "F" specifier): in
                // the Unity Mono runtime composite "{:F0}" on a boxed float emitted a literal
                // "F" prefix ("v":F6652), breaking JSON parse on the UI side. "0.0"/"0" use only
                // digit placeholders, so they can never be misread as "F"+number.
                m_PowerHistorySbScratch.Append("{\"h\":")
                    .Append(p.GameHour.ToString("0.0", CultureInfo.InvariantCulture))
                    .Append(",\"v\":")
                    .Append(p.Value.ToString("0", CultureInfo.InvariantCulture))
                    .Append('}');
            }
            m_PowerHistorySbScratch.Append("],\"consumption\":[");
            for (int i = 0; i < m_PowerConsumptionHistory.Count; i++)
            {
                if (i > 0) m_PowerHistorySbScratch.Append(',');
                var p = m_PowerConsumptionHistory[i];
                // Explicit digit-placeholder custom format (not the standard "F" specifier): in
                // the Unity Mono runtime composite "{:F0}" on a boxed float emitted a literal
                // "F" prefix ("v":F6652), breaking JSON parse on the UI side. "0.0"/"0" use only
                // digit placeholders, so they can never be misread as "F"+number.
                m_PowerHistorySbScratch.Append("{\"h\":")
                    .Append(p.GameHour.ToString("0.0", CultureInfo.InvariantCulture))
                    .Append(",\"v\":")
                    .Append(p.Value.ToString("0", CultureInfo.InvariantCulture))
                    .Append('}');
            }
            m_PowerHistorySbScratch.Append("]}");
            return m_PowerHistorySbScratch.ToString();
        }

        private string SerializeHistory(List<HistoryPoint> history)
        {
            m_HistorySbScratch.Clear();
            m_HistorySbScratch.Append('[');
            for (int i = 0; i < history.Count; i++)
            {
                if (i > 0) m_HistorySbScratch.Append(',');
                var p = history[i];
                // Digit-placeholder custom format, not the standard "F" specifier — see the
                // SerializePowerHistory note: Mono composite "{:F1}" on a boxed float emitted a
                // literal "F" prefix and broke UI JSON parse.
                m_HistorySbScratch.Append("{\"h\":")
                    .Append(p.GameHour.ToString("0.0", CultureInfo.InvariantCulture))
                    .Append(",\"v\":")
                    .Append(p.Value.ToString("0.0", CultureInfo.InvariantCulture))
                    .Append('}');
            }
            m_HistorySbScratch.Append(']');
            return m_HistorySbScratch.ToString();
        }

        protected override void OnDestroy()
        {
            Log.Info($"[DEBUG] {nameof(BalanceMetricsSystem)} destroyed");
            base.OnDestroy();
        }
    }
}
#endif
