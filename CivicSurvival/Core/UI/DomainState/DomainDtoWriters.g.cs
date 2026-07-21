// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/ui-dto.contract.yaml
// SourceHash:       sha256:35dbbf520b380de18559719c3b738ab5fde5b12ea764031ad7de2a095b96b8d2
// Generator:        scripts/generators/ui_dto.py
// GeneratorVersion: 1.0.0
// ContractVersion:  1.0.0
// GeneratedAt:      2026-05-14T00:00:00Z

using System.Text;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    internal static class DomainDtoWriters
    {
        internal static void WriteEntityRefDtoSegment0(ref DomainJsonHelper.JsonWriter w, in EntityRefDto dto)
        {
            w.Int("Index", dto.Index);
            w.Int("Version", dto.Version);
        }

        internal static void WriteCrashDumpEntrySegment0(ref DomainJsonHelper.JsonWriter w, in CrashDumpEntry dto)
        {
            w.Str("Name", dto.Name ?? string.Empty);
            w.Float("SizeMb", dto.SizeMb);
            w.Str("TimeText", dto.TimeText ?? string.Empty);
        }

        internal static void WriteCivilianDamageDataSegment0(ref DomainJsonHelper.JsonWriter w, in CivilianDamageData dto)
        {
            w.BeginObject("Building");
            WriteEntityRefDtoSegment0(ref w, dto.Building);
            w.EndObject();
            w.Str("Name", dto.Name ?? string.Empty);
            w.Int("HitCount", dto.HitCount);
            w.Int("MaxHits", dto.MaxHits);
            w.Float("DamagePercent", dto.DamagePercent);
            w.Bool("IsRepairing", dto.IsRepairing);
            w.Float("RepairHoursLeft", dto.RepairHoursLeft);
            w.Int("MunicipalRepairCharge", dto.MunicipalRepairCharge);
            w.Int("MunicipalKickbackRepairCharge", dto.MunicipalKickbackRepairCharge);
            w.Int("KickbackRepairAmount", dto.KickbackRepairAmount);
            w.Bool("CanMunicipalRepair", dto.CanMunicipalRepair);
            w.Str("MunicipalRepairLockedReasonId", dto.MunicipalRepairLockedReasonId ?? string.Empty);
            w.Bool("CanKickbackRepair", dto.CanKickbackRepair);
            w.Str("KickbackRepairLockedReasonId", dto.KickbackRepairLockedReasonId ?? string.Empty);
            w.Int("ShadowOpsRepairCharge", dto.ShadowOpsRepairCharge);
            w.Bool("CanShadowRepair", dto.CanShadowRepair);
            w.Str("ShadowRepairLockedReasonId", dto.ShadowRepairLockedReasonId ?? string.Empty);
        }

        internal static void WriteActiveContractEntrySegment0(ref DomainJsonHelper.JsonWriter w, in ActiveContractEntry dto)
        {
            w.Int("EntityIndex", dto.EntityIndex);
            w.Str("BuildingName", dto.BuildingName ?? string.Empty);
            w.Str("ContractType", dto.ContractType ?? string.Empty);
            w.Str("VendorName", dto.VendorName ?? string.Empty);
            w.Float("Quality", dto.Quality);
            w.Int("KickbackAmount", dto.KickbackAmount);
            w.Bool("IsShady", dto.IsShady);
            w.Int("DaysRemaining", dto.DaysRemaining);
        }

        internal static void WritePendingProcurementOfferEntrySegment0(ref DomainJsonHelper.JsonWriter w, in PendingProcurementOfferEntry dto)
        {
            w.Int("EntityIndex", dto.EntityIndex);
            w.Int("EntityVersion", dto.EntityVersion);
            w.Str("Service", dto.Service ?? string.Empty);
            w.Str("ContractType", dto.ContractType ?? string.Empty);
            w.Str("OfficialVendorName", dto.OfficialVendorName ?? string.Empty);
            w.Str("ShadyVendorName", dto.ShadyVendorName ?? string.Empty);
            w.Int("OfficialPrice", dto.OfficialPrice);
            w.Int("ShadyPrice", dto.ShadyPrice);
            w.Int("KickbackOffer", dto.KickbackOffer);
            w.Float("OfficialQuality", dto.OfficialQuality);
            w.Float("ShadyQuality", dto.ShadyQuality);
            w.Bool("CanAcceptShady", dto.CanAcceptShady);
            w.Str("AcceptShadyLockedReasonId", dto.AcceptShadyLockedReasonId ?? string.Empty);
            w.Int("AcceptShadyEffectiveCost", dto.AcceptShadyEffectiveCost);
            w.Str("BuildingName", dto.BuildingName ?? string.Empty);
        }

        internal static void WritePlantWearDataSegment0(ref DomainJsonHelper.JsonWriter w, in PlantWearData dto)
        {
            w.Int("PlantId", dto.PlantId);
            w.Str("Name", dto.Name ?? string.Empty);
            w.Int("CapacityMW", dto.CapacityMW);
            w.Int("CurrentOutputMW", dto.CurrentOutputMW);
            w.Float("WearPercent", dto.WearPercent);
            w.Float("RepairBillablePercent", dto.RepairBillablePercent);
            w.Bool("IsRepairable", dto.IsRepairable);
            w.Bool("IsDestroyed", dto.IsDestroyed);
            w.Bool("IsRepairing", dto.IsRepairing);
            w.Float("RepairHoursLeft", dto.RepairHoursLeft);
            w.Bool("HasExploded", dto.HasExploded);
            w.Bool("IsUnderConstruction", dto.IsUnderConstruction);
            w.Float("ConstructionDaysLeft", dto.ConstructionDaysLeft);
            w.Float("OperationalDamagePercent", dto.OperationalDamagePercent);
            w.Int("OperationalHitCount", dto.OperationalHitCount);
            w.Int("OperationalHitMax", dto.OperationalHitMax);
            w.Float("DisasterDamagePercent", dto.DisasterDamagePercent);
            w.Bool("IsAtRisk", dto.IsAtRisk);
            w.Int("MunicipalRepairCharge", dto.MunicipalRepairCharge);
            w.Int("MunicipalKickbackRepairCharge", dto.MunicipalKickbackRepairCharge);
            w.Int("KickbackRepairAmount", dto.KickbackRepairAmount);
            w.Bool("CanMunicipalRepair", dto.CanMunicipalRepair);
            w.Str("MunicipalRepairLockedReasonId", dto.MunicipalRepairLockedReasonId ?? string.Empty);
            w.Bool("CanKickbackRepair", dto.CanKickbackRepair);
            w.Str("KickbackRepairLockedReasonId", dto.KickbackRepairLockedReasonId ?? string.Empty);
            w.Int("ShadowOpsRepairCharge", dto.ShadowOpsRepairCharge);
            w.Bool("CanShadowRepair", dto.CanShadowRepair);
            w.Str("ShadowRepairLockedReasonId", dto.ShadowRepairLockedReasonId ?? string.Empty);
            w.Int("State", (int)dto.State);
            w.Float("SaturationFactor", dto.SaturationFactor);
            w.Float("FuelAvailabilityPercent", dto.FuelAvailabilityPercent);
            w.Float("FuelFactor", dto.FuelFactor);
            w.Float("RecoveryHours", dto.RecoveryHours);
        }

        internal static void WriteMapBoundsDtoSegment0(ref DomainJsonHelper.JsonWriter w, in MapBoundsDto dto)
        {
            w.Float("MinX", dto.MinX);
            w.Float("MaxX", dto.MaxX);
            w.Float("MinZ", dto.MinZ);
            w.Float("MaxZ", dto.MaxZ);
        }

        internal static void WriteRadarInterceptionDtoSegment0(ref DomainJsonHelper.JsonWriter w, in RadarInterceptionDto dto)
        {
            w.Float("X", dto.X);
            w.Float("Z", dto.Z);
            w.Float("TimeAgo", dto.TimeAgo);
            w.Float("Lifetime", dto.Lifetime);
            w.Bool("Success", dto.Success);
        }

        internal static void WriteRadarTargetDtoSegment0(ref DomainJsonHelper.JsonWriter w, in RadarTargetDto dto)
        {
            w.BeginObject("Entity");
            WriteEntityRefDtoSegment0(ref w, dto.Entity);
            w.EndObject();
            w.Float("X", dto.X);
            w.Float("Z", dto.Z);
            w.Str("Name", dto.Name ?? string.Empty);
            w.Float("SizeX", dto.SizeX);
            w.Float("SizeY", dto.SizeY);
            w.Float("SizeZ", dto.SizeZ);
            w.Float("RotationY", dto.RotationY);
        }

        internal static void WriteRadarThreatDtoSegment0(ref DomainJsonHelper.JsonWriter w, in RadarThreatDto dto)
        {
            w.BeginObject("Entity");
            WriteEntityRefDtoSegment0(ref w, dto.Entity);
            w.EndObject();
            w.Float("X", dto.X);
            w.Float("Z", dto.Z);
            w.Float("Vx", dto.Vx);
            w.Float("Vz", dto.Vz);
            w.Float("Eta", dto.Eta);
            w.Float("Altitude", dto.Altitude);
            w.Str("Type", dto.Type ?? string.Empty);
            w.Str("EvasionStatus", dto.EvasionStatus ?? string.Empty);
            w.Bool("IsIdentified", dto.IsIdentified);
            w.Bool("IsOutbound", dto.IsOutbound);
        }

        internal static void WriteRadarDefenseDtoSegment0(ref DomainJsonHelper.JsonWriter w, in RadarDefenseDto dto)
        {
            w.Float("X", dto.X);
            w.Float("Z", dto.Z);
            w.Float("Range", dto.Range);
        }

        internal static void WriteRadarBroadcastDtoSegment0(ref DomainJsonHelper.JsonWriter w, in RadarBroadcastDto dto)
        {
            w.Float("X", dto.X);
            w.Float("Z", dto.Z);
            w.Float("Range", dto.Range);
        }

        internal static void WriteAaPlacementOptionEntrySegment0(ref DomainJsonHelper.JsonWriter w, in AaPlacementOptionEntry dto)
        {
            w.Str("Prefab", dto.Prefab ?? string.Empty);
            w.Int("Mode", dto.Mode);
            w.Str("NameKey", dto.NameKey ?? string.Empty);
            w.Str("Icon", dto.Icon ?? string.Empty);
            w.Int("Munition", dto.Munition);
            w.Float("Range", dto.Range);
            w.Float("InterceptShahed", dto.InterceptShahed);
            w.Float("InterceptBallistic", dto.InterceptBallistic);
            w.Int("Crew", dto.Crew);
            w.Int("Deployed", dto.Deployed);
            w.Int("Cost", dto.Cost);
            w.Int("CreditsLeft", dto.CreditsLeft);
            w.Int("AffordableCount", dto.AffordableCount);
            w.Bool("CanPlace", dto.CanPlace);
            w.Str("LockedReasonId", dto.LockedReasonId ?? string.Empty);
        }

        internal static void WriteShadowProgramEntrySegment0(ref DomainJsonHelper.JsonWriter w, in ShadowProgramEntry dto)
        {
            w.Int("DistrictIndex", dto.DistrictIndex);
            w.Str("DistrictName", dto.DistrictName ?? string.Empty);
            w.Bool("HasProgram", dto.HasProgram);
            w.Str("Contractor", dto.Contractor ?? string.Empty);
            w.Int("EstimatedCost", dto.EstimatedCost);
            w.Bool("CanModernizeHonest", dto.CanModernizeHonest);
            w.Str("ModernizeHonestLockedReasonId", dto.ModernizeHonestLockedReasonId ?? string.Empty);
            w.Bool("CanModernizeCorrupt", dto.CanModernizeCorrupt);
            w.Str("ModernizeCorruptLockedReasonId", dto.ModernizeCorruptLockedReasonId ?? string.Empty);
            w.Int("KickbackEarned", dto.KickbackEarned);
            w.Int("FireCount", dto.FireCount);
        }

        internal static void WriteVector3IntDtoSegment0(ref DomainJsonHelper.JsonWriter w, in Vector3IntDto dto)
        {
            w.Int("X", dto.X);
            w.Int("Y", dto.Y);
            w.Int("Z", dto.Z);
        }

        internal static void WriteThreatTargetDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ThreatTargetDto dto)
        {
            w.Int("EntityIndex", dto.EntityIndex);
            w.Int("EntityVersion", dto.EntityVersion);
            w.Str("Name", dto.Name ?? string.Empty);
            w.BeginObject("Position");
            WriteVector3IntDtoSegment0(ref w, dto.Position);
            w.EndObject();
            w.Int("ThreatCount", dto.ThreatCount);
            w.Int("MinEtaSeconds", dto.MinEtaSeconds);
        }

        internal static void WriteFocusRangeDtoSegment0(ref DomainJsonHelper.JsonWriter w, in FocusRangeDto dto)
        {
            w.Int("Min", dto.Min);
            w.Int("Max", dto.Max);
        }

        internal static void WriteOfficialTreasuryDtoSegment0(ref DomainJsonHelper.JsonWriter w, in OfficialTreasuryDto dto)
        {
            w.Long("Balance", dto.Balance);
            w.Long("TotalIncome", dto.TotalIncome);
            w.Long("TotalExpenses", dto.TotalExpenses);
        }

        internal static void WriteShadowWalletDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ShadowWalletDto dto)
        {
            w.Long("Available", dto.Available);
            w.Long("LockedBalance", dto.LockedBalance);
            w.Long("TotalAssets", dto.TotalAssets);
            w.Long("ShadowIncome", dto.ShadowIncome);
            w.Long("ShadowExpenses", dto.ShadowExpenses);
        }

        internal static void WriteOperationSlotDtoSegment0(ref DomainJsonHelper.JsonWriter w, in OperationSlotDto dto)
        {
            w.Str("AttackType", dto.AttackType ?? string.Empty);
            w.Str("OperationState", dto.OperationState ?? "idle");
            w.Long("Cost", dto.Cost);
            w.Float("Progress", dto.Progress);
        }

        internal static void WriteResolvedStrikeDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ResolvedStrikeDto dto)
        {
            w.Str("Axis", dto.Axis ?? string.Empty);
            w.Bool("Intercepted", dto.Intercepted);
            w.Bool("NoEffect", dto.NoEffect);
            w.Float("OldValue", dto.OldValue);
            w.Float("NewValue", dto.NewValue);
            w.Long("Seed", dto.Seed);
            w.Int("TargetId", dto.TargetId);
        }

        internal static void WriteAttackTimeEstimateDtoSegment0(ref DomainJsonHelper.JsonWriter w, in AttackTimeEstimateDto dto)
        {
            w.Str("Status", dto.Status ?? "unknown");
            if (dto.MinHours != -1)
            {
                w.Float("MinHours", dto.MinHours);
            }
            if (dto.MaxHours != -1)
            {
                w.Float("MaxHours", dto.MaxHours);
            }
        }

        internal static void WriteCognitiveDistrictEntrySegment0(ref DomainJsonHelper.JsonWriter w, in CognitiveDistrictEntry dto)
        {
            w.Int("DistrictIndex", dto.DistrictIndex);
            w.Str("Name", dto.Name ?? string.Empty);
            w.Float("Integrity", dto.Integrity);
            w.Bool("HasInternet", dto.HasInternet);
            w.Bool("IsCompromised", dto.IsCompromised);
            w.Bool("IsUnzoned", dto.IsUnzoned);
        }

        internal static void WriteCognitiveStratumEntrySegment0(ref DomainJsonHelper.JsonWriter w, in CognitiveStratumEntry dto)
        {
            w.Int("Stratum", dto.Stratum);
            w.Int("Count", dto.Count);
            w.Float("Infection", dto.Infection);
            w.Float("Resistance", dto.Resistance);
            w.Int("AllocatedHouseholds", dto.AllocatedHouseholds);
            w.Int("CoveredHouseholds", dto.CoveredHouseholds);
            w.Int("EnemyReachHouseholds", dto.EnemyReachHouseholds);
            w.Bool("HasFoggedReach", dto.HasFoggedReach);
            w.Float("SignalCoverageFraction", dto.SignalCoverageFraction);
        }

        internal static void WriteCognitivePsyOpsEntrySegment0(ref DomainJsonHelper.JsonWriter w, in CognitivePsyOpsEntry dto)
        {
            w.Int("PsyOpsType", dto.PsyOpsType);
            w.Int("TargetStratum", dto.TargetStratum);
            w.Int("Phase", dto.Phase);
            w.Float("WindowHours", dto.WindowHours);
            w.Float("WindowFraction", dto.WindowFraction);
            w.Float("EtaHours", dto.EtaHours);
            w.Float("Intensity", dto.Intensity);
            w.Bool("IsDominant", dto.IsDominant);
            w.Int("FogState", dto.FogState);
            w.Float("HeroShield", dto.HeroShield);
            w.Int("Blunted", dto.Blunted);
            w.Float("TargetCoverage", dto.TargetCoverage);
            w.Int("ReachHouseholds", dto.ReachHouseholds);
            w.Int("LandedByArchetype", dto.LandedByArchetype);
            w.Int("ContactId", dto.ContactId);
        }

        internal static void WriteNewsPostDtoSegment0(ref DomainJsonHelper.JsonWriter w, in NewsPostDto dto)
        {
            w.Str("PostId", dto.PostId ?? string.Empty);
            w.Str("Source", dto.Source ?? string.Empty);
            w.Str("Title", dto.Title ?? string.Empty);
            w.Str("Body", dto.Body ?? string.Empty);
            w.Str("Mood", dto.Mood ?? "Neutral");
            w.Long("Timestamp", dto.Timestamp);
            w.Str("Category", dto.Category ?? string.Empty);
            w.Str("Scope", dto.Scope ?? "global");
            w.Bool("IsAiGenerated", dto.IsAiGenerated);
        }

        internal static void WriteSocialPostDtoSegment0(ref DomainJsonHelper.JsonWriter w, in SocialPostDto dto)
        {
            w.Str("Author", dto.Author ?? string.Empty);
            w.Str("AuthorName", dto.AuthorName ?? string.Empty);
            w.Str("Message", dto.Message ?? string.Empty);
            w.Str("Mood", dto.Mood ?? "Neutral");
            w.Long("Timestamp", dto.Timestamp);
            w.Bool("IsOfficial", dto.IsOfficial);
            w.Str("AvatarId", dto.AvatarId ?? string.Empty);
        }

        internal static void WriteToastDataDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ToastDataDto dto)
        {
            w.Int("Id", dto.Id);
            w.Str("Type", dto.Type ?? string.Empty);
            w.Int("Priority", dto.Priority);
            w.Str("Title", dto.Title ?? string.Empty);
            w.Str("Message", dto.Message ?? string.Empty);
            w.Str("AcceptLabel", dto.AcceptLabel ?? string.Empty);
            w.Str("RejectLabel", dto.RejectLabel ?? string.Empty);
            w.Float("RemainingSeconds", dto.RemainingSeconds);
            w.Float("Progress", dto.Progress);
            w.Int("ContextData", dto.ContextData);
        }

        internal static void WriteRankTierDtoSegment0(ref DomainJsonHelper.JsonWriter w, in RankTierDto dto)
        {
            w.Str("Name", dto.Name ?? string.Empty);
            w.Long("MinScore", dto.MinScore);
            w.Str("Icon", dto.Icon ?? string.Empty);
        }

        internal static void WriteLeaderboardEntryDtoSegment0(ref DomainJsonHelper.JsonWriter w, in LeaderboardEntryDto dto)
        {
            w.Int("Position", dto.Position);
            w.Str("Nickname", dto.Nickname ?? string.Empty);
            w.Int("WavesSurvived", dto.WavesSurvived);
            w.Long("Score", dto.Score);
            w.Str("RankTier", dto.RankTier ?? string.Empty);
        }

        internal static void WriteWeeklyLeaderboardEntryDtoSegment0(ref DomainJsonHelper.JsonWriter w, in WeeklyLeaderboardEntryDto dto)
        {
            w.Int("Position", dto.Position);
            w.Str("Nickname", dto.Nickname ?? string.Empty);
            w.Int("WavesSurvived", dto.WavesSurvived);
            w.Long("Score", dto.Score);
        }

        internal static void WriteShadowLeaderboardEntryDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ShadowLeaderboardEntryDto dto)
        {
            w.Int("Position", dto.Position);
            w.Str("Nickname", dto.Nickname ?? string.Empty);
            w.Long("Earned", dto.Earned);
            w.Long("Confiscated", dto.Confiscated);
            w.Long("Net", dto.Net);
            w.Str("RankTier", dto.RankTier ?? string.Empty);
        }

        internal static void WriteWeeklyShadowLeaderboardEntryDtoSegment0(ref DomainJsonHelper.JsonWriter w, in WeeklyShadowLeaderboardEntryDto dto)
        {
            w.Int("Position", dto.Position);
            w.Str("Nickname", dto.Nickname ?? string.Empty);
            w.Long("Net", dto.Net);
        }

        internal static void WriteProsperityLeaderboardEntryDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ProsperityLeaderboardEntryDto dto)
        {
            w.Int("Position", dto.Position);
            w.Str("Nickname", dto.Nickname ?? string.Empty);
            w.Int("Waves", dto.Waves);
            w.Int("AvgIndex", dto.AvgIndex);
            w.Long("Score", dto.Score);
            w.Str("RankTier", dto.RankTier ?? string.Empty);
        }

        internal static void WriteWeeklyProsperityLeaderboardEntryDtoSegment0(ref DomainJsonHelper.JsonWriter w, in WeeklyProsperityLeaderboardEntryDto dto)
        {
            w.Int("Position", dto.Position);
            w.Str("Nickname", dto.Nickname ?? string.Empty);
            w.Long("Score", dto.Score);
        }

        internal static void WriteAirDefenseDtoSegment0(ref DomainJsonHelper.JsonWriter w, in AirDefenseDto dto)
        {
            w.Int("AaStations", dto.AaStations);
            w.Bool("SirenActive", dto.SirenActive);
            w.Int("PatriotAmmo", dto.PatriotAmmo);
            w.Int("PatriotMaxAmmo", dto.PatriotMaxAmmo);
            w.Int("PatriotResupplyCost", dto.PatriotResupplyCost);
            w.Int("GunsAmmo", dto.GunsAmmo);
            w.Int("GunsMaxAmmo", dto.GunsMaxAmmo);
            w.Int("GunsResupplyCost", dto.GunsResupplyCost);
            w.Raw("AaRosterJson", dto.AaRosterJson ?? "[]");
        }

        internal static void WriteAirDefenseDtoSegment1(ref DomainJsonHelper.JsonWriter w, in AirDefenseDto dto)
        {
            w.Int("HeritageCredits", dto.HeritageCredits);
            w.Int("HeritageCreditsMax", dto.HeritageCreditsMax);
            w.Bool("PatriotInterceptsDrones", dto.PatriotInterceptsDrones);
            w.Bool("AutoResupplyEnabled", dto.AutoResupplyEnabled);
            w.Str("DefensePolicyName", dto.DefensePolicyName ?? "Humanitarian Shield");
            w.Int("DefensePolicyId", dto.DefensePolicyId);
            w.Int("SpotterPenaltyPercent", dto.SpotterPenaltyPercent);
            w.Int("DonorPatriotCredits", dto.DonorPatriotCredits);
            w.Raw("EmergencyResupplyRequest", dto.EmergencyResupplyRequestJson ?? new RequestResult().ToJson());
            w.Raw("DefensePolicyRequest", dto.DefensePolicyRequestJson ?? new RequestResult().ToJson());
            w.Raw("PatriotDroneToggleRequest", dto.PatriotDroneToggleRequestJson ?? new RequestResult().ToJson());
            w.Raw("AirDefensePlacementRequest", dto.AirDefensePlacementRequestJson ?? RequestResultBridge.Get(RequestResultBridge.AirDefensePlacement).ToJson());
        }

        internal static void WriteArrestedModalPayloadDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ArrestedModalPayloadDto dto)
        {
            w.Int("ChargesCount", dto.ChargesCount);
            w.Long("AssetsSeizedSnapshot", dto.AssetsSeizedSnapshot);
            w.Long("WalletBalanceAfter", dto.WalletBalanceAfter);
            w.Str("LastChoiceResult", dto.LastChoiceResult ?? string.Empty);
        }

        internal static void WriteAttentionDtoSegment0(ref DomainJsonHelper.JsonWriter w, in AttentionDto dto)
        {
            w.Float("ShockLevel", dto.ShockLevel);
            w.Str("ShockTier", dto.ShockTier is null or "None" ? "DeepConcern" : dto.ShockTier);
            w.Int("CasualtiesThisWeek", dto.CasualtiesThisWeek);
            w.Int("BuildingsDestroyedThisWeek", dto.BuildingsDestroyedThisWeek);
            w.Int("CriticalHitsThisWeek", dto.CriticalHitsThisWeek);
            w.Long("TotalCasualties", dto.TotalCasualties);
            w.Long("TotalBuildingsDestroyed", dto.TotalBuildingsDestroyed);
            w.Long("TotalCivilianBuildingsDestroyed", dto.TotalCivilianBuildingsDestroyed);
            w.Long("TotalCriticalHits", dto.TotalCriticalHits);
            w.Bool("ExodusActive", dto.ExodusActive);
            w.Float("BaseExodusRatePercentPerDay", dto.BaseExodusRatePercentPerDay);
            w.Float("ExodusRatePercentPerDay", dto.ExodusRatePercentPerDay);
            w.Float("KineticRatePercentPerDay", dto.KineticRatePercentPerDay);
            w.Float("PsyRatePercentPerDay", dto.PsyRatePercentPerDay);
            w.Int("TotalExodus", dto.TotalExodus);
            w.Int("MonacoFamiliesFled", dto.MonacoFamiliesFled);
            w.Long("MonacoCapitalFled", dto.MonacoCapitalFled);
        }

        internal static void WriteBackupPowerDtoSegment0(ref DomainJsonHelper.JsonWriter w, in BackupPowerDto dto)
        {
            w.Int("BackupCharge", dto.BackupCharge);
            w.Int("GeneratorsRunning", dto.GeneratorsRunning);
            w.Int("NoiseLevel", dto.NoiseLevel);
            w.Int("ProtectedBuildings", dto.ProtectedBuildings);
            w.Int("BackupCapacity", dto.BackupCapacity);
            w.Int("DischargingCount", dto.DischargingCount);
            w.Raw("ShadowProgramsJson", dto.ShadowProgramsJson ?? "[]");
            w.Int("ProcurementCooldown", dto.ProcurementCooldown);
            w.Int("BackupPolicy", dto.BackupPolicy);
            w.Int("HospitalsPowered", dto.HospitalsPowered);
            w.Int("HospitalsTotal", dto.HospitalsTotal);
            w.Int("SchoolsPowered", dto.SchoolsPowered);
            w.Int("SchoolsTotal", dto.SchoolsTotal);
        }

        internal static void WriteBackupPowerDtoSegment1(ref DomainJsonHelper.JsonWriter w, in BackupPowerDto dto)
        {
            w.Raw("ModernizationRequest", dto.ModernizationRequestJson ?? new RequestResult().ToJson());
            w.Raw("BackupPolicyRequest", dto.BackupPolicyRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteBuckwheatDtoSegment0(ref DomainJsonHelper.JsonWriter w, in BuckwheatDto dto)
        {
            w.Double("BuckwheatTons", dto.BuckwheatTons);
            w.Int("ProcurementLevel", dto.ProcurementLevel);
            w.Int("DailyCost", dto.DailyCost);
            w.Int("BaseDailyCost", dto.BaseDailyCost);
            w.Bool("ShadowFunded", dto.ShadowFunded);
        }

        internal static void WriteBuckwheatDtoSegment1(ref DomainJsonHelper.JsonWriter w, in BuckwheatDto dto)
        {
            w.Raw("LastDistributeResult", dto.LastDistributeResultJson ?? new RequestResult().ToJson());
            w.Raw("ProcurementLevelRequest", dto.ProcurementLevelRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteCrisisSweepDtoSegment0(ref DomainJsonHelper.JsonWriter w, in CrisisSweepDto dto)
        {
            w.Int("Mode", dto.Mode);
            w.Bool("HasResult", dto.HasResult);
            w.Double("ComputedAtGameHours", dto.ComputedAtGameHours);
            w.Int("ArchetypeId", dto.ArchetypeId);
            w.Int("PopulationPeak", dto.PopulationPeak);
            w.Int("WarDay", dto.WarDay);
            w.Float("WorstCaseRecoveryBallisticOnly", dto.WorstCaseRecoveryBallisticOnly);
            w.Float("WorstCaseRecoveryMixed", dto.WorstCaseRecoveryMixed);
            w.Bool("IsRecoverableBallisticOnly", dto.IsRecoverableBallisticOnly);
            w.Bool("IsRecoverableMixed", dto.IsRecoverableMixed);
            w.Float("GraceWindowHours", dto.GraceWindowHours);
            w.Float("DroneInterceptBallisticOnly", dto.DroneInterceptBallisticOnly);
            w.Float("DroneInterceptMixed", dto.DroneInterceptMixed);
            w.Int("FreeHeritageGrant", dto.FreeHeritageGrant);
            w.Int("OperationalAaAtVerdict", dto.OperationalAaAtVerdict);
            w.Int("ManpowerTotal", dto.ManpowerTotal);
            w.Int("ManpowerUsed", dto.ManpowerUsed);
            w.Int("ManpowerCasualties", dto.ManpowerCasualties);
            w.Int("ManpowerAvailable", dto.ManpowerAvailable);
            w.Int("AaHeritage", dto.AaHeritage);
            w.Int("AaBofors", dto.AaBofors);
            w.Int("AaGepard", dto.AaGepard);
            w.Int("AaPatriot", dto.AaPatriot);
            w.Float("CoveragePct", dto.CoveragePct);
            w.Float("AreaKm2", dto.AreaKm2);
            w.Float("BallisticInterceptBallisticOnly", dto.BallisticInterceptBallisticOnly);
            w.Float("BallisticInterceptMixed", dto.BallisticInterceptMixed);
            w.Int("BallisticTargets", dto.BallisticTargets);
            w.Int("MissilesSpentOnDrones", dto.MissilesSpentOnDrones);
            w.Bool("PatriotInterceptsDrones", dto.PatriotInterceptsDrones);
            w.Float("CalmHours", dto.CalmHours);
            w.Float("WavePressureAtPeak", dto.WavePressureAtPeak);
            w.Int("SampleCount", dto.SampleCount);
            w.Float("BlackoutProbabilityPct", dto.BlackoutProbabilityPct);
            w.Int("MedianCollapseDay", dto.MedianCollapseDay);
            w.Int("UnsheddableFloorMW", dto.UnsheddableFloorMW);
            w.Int("RepairSlots", dto.RepairSlots);
            w.Long("RepairFundingCash", dto.RepairFundingCash);
            w.Int("RepairTier", dto.RepairTier);
            w.Bool("RepairBudgetLive", dto.RepairBudgetLive);
        }

        internal static void WriteDistrictDtoSegment0(ref DomainJsonHelper.JsonWriter w, in DistrictDto dto)
        {
            w.Int("EntityIndex", dto.EntityIndex);
            w.Int("EntityVersion", dto.EntityVersion);
            w.Str("Name", dto.Name ?? string.Empty);
            w.Bool("IsUnzoned", dto.IsUnzoned);
            w.Bool("ResidentialOff", dto.ResidentialOff);
            w.Bool("CommercialOff", dto.CommercialOff);
            w.Bool("IndustrialOff", dto.IndustrialOff);
            w.Bool("OfficeOff", dto.OfficeOff);
            w.Bool("ServicesOff", dto.ServicesOff);
            w.Int("Schedule", dto.Schedule);
            w.Str("ScheduleName", dto.ScheduleName ?? "Manual");
            w.Bool("ScheduleActive", dto.ScheduleActive);
            w.Int("TotalMW", dto.TotalMW);
            w.Int("ResidentialMW", dto.ResidentialMW);
            w.Int("CommercialMW", dto.CommercialMW);
            w.Int("IndustrialMW", dto.IndustrialMW);
            w.Int("OfficeMW", dto.OfficeMW);
            w.Int("ServicesMW", dto.ServicesMW);
            w.Int("Priority", dto.Priority);
            w.Int("DeliveredMW", dto.DeliveredMW);
            w.Int("ThresholdCutMW", dto.ThresholdCutMW);
            w.Bool("IsVIP", dto.IsVIP);
            w.Bool("IsVIPBypass", dto.IsVIPBypass);
            w.Bool("IsAutoShedded", dto.IsAutoShedded);
            w.Bool("InternetDisabled", dto.InternetDisabled);
            w.Int("ThresholdCutBuildings", dto.ThresholdCutBuildings);
            w.Float("TotalHappinessPenalty", dto.TotalHappinessPenalty);
            w.Float("TotalCommercePenalty", dto.TotalCommercePenalty);
            w.Str("BlackoutSource", dto.BlackoutSource ?? "none");
        }

        internal static void WriteCognitiveDtoSegment0(ref DomainJsonHelper.JsonWriter w, in CognitiveDto dto)
        {
            w.Bool("CognitiveActive", dto.CognitiveActive);
            w.Float("InfectionRate", dto.InfectionRate);
            w.Float("RecoveryRate", dto.RecoveryRate);
            w.Float("PenaltyThreshold", dto.PenaltyThreshold);
            w.Int("TotalDistricts", dto.TotalDistricts);
            w.Int("CompromisedDistricts", dto.CompromisedDistricts);
            w.Int("HeroStatus", dto.HeroStatus);
            w.Int("HeroDeployCost", dto.HeroDeployCost);
            w.Float("HeroInfectionReduction", dto.HeroInfectionReduction);
            w.Float("HeroRecoveryBonus", dto.HeroRecoveryBonus);
            w.Raw("HeroActionRequest", dto.HeroActionRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteCognitiveDtoSegment1(ref DomainJsonHelper.JsonWriter w, in CognitiveDto dto)
        {
            w.Int("HeroArchetype", dto.HeroArchetype);
            w.Float("ArestovychDebtRatio", dto.ArestovychDebtRatio);
            w.Bool("ArestovychDebtCollapsing", dto.ArestovychDebtCollapsing);
            w.Bool("HeroArchetypeSwitchReady", dto.HeroArchetypeSwitchReady);
            w.Int("ProtestRisk", dto.ProtestRisk);
            w.Str("DominantNarrative", dto.DominantNarrative ?? "All quiet on the information front.");
            w.Float("AvgIntegrity", dto.AvgIntegrity);
            w.Int("TotalHouseholds", dto.TotalHouseholds);
            w.Float("AvgInfection", dto.AvgInfection);
            w.Float("AvgResistance", dto.AvgResistance);
            w.Float("AvgTrauma", dto.AvgTrauma);
            w.Int("HouseholdsUnderBlackout", dto.HouseholdsUnderBlackout);
            w.Int("HouseholdsWithEnvy", dto.HouseholdsWithEnvy);
            w.Int("HouseholdsUnderImpact", dto.HouseholdsUnderImpact);
            w.Int("HouseholdsInfected", dto.HouseholdsInfected);
            w.Int("VulnerableHouseholds", dto.VulnerableHouseholds);
            w.Float("AvgBlackoutHours", dto.AvgBlackoutHours);
            w.Float("BlackoutVulnerability", dto.BlackoutVulnerability);
            w.Int("InternetMode", dto.InternetMode);
            w.Float("CommercePenalty", dto.CommercePenalty);
            w.Raw("InternetModeRequest", dto.InternetModeRequestJson ?? new RequestResult().ToJson());
            w.Bool("IpsoActive", dto.IpsoActive);
            w.Int("IpsoIntensity", dto.IpsoIntensity);
            w.Int("IpsoDistrictCount", dto.IpsoDistrictCount);
            w.Int("IpsoTotalDistricts", dto.IpsoTotalDistricts);
            w.Bool("TelemarathonActive", dto.TelemarathonActive);
            w.Int("NarrativeMode", dto.NarrativeMode);
            w.Float("MediaTrust", dto.MediaTrust);
            w.Bool("IsInShock", dto.IsInShock);
            w.Float("ShockHoursRemaining", dto.ShockHoursRemaining);
            w.Float("AudienceFatigue", dto.AudienceFatigue);
            w.Raw("TelemarathonModeRequest", dto.TelemarathonModeRequestJson ?? new RequestResult().ToJson());
            w.Raw("TelemarathonActiveRequest", dto.TelemarathonActiveRequestJson ?? new RequestResult().ToJson());
            w.Bool("CenterBuilt", dto.CenterBuilt);
            w.Bool("CenterOnline", dto.CenterOnline);
            w.Int("CenterTier", dto.CenterTier);
            w.Int("CenterMaxTier", dto.CenterMaxTier);
            w.Int("CenterCost", dto.CenterCost);
            w.Int("CenterUpgradeCost", dto.CenterUpgradeCost);
            w.Float("BroadcastCapacity", dto.BroadcastCapacity);
            w.Float("BroadcastCapacityFree", dto.BroadcastCapacityFree);
            w.Float("CenterReach", dto.CenterReach);
            w.Float("TelecomFactor", dto.TelecomFactor);
            w.Raw("PropagandaCenterPlacementRequest", dto.PropagandaCenterPlacementRequestJson ?? new RequestResult().ToJson());
            w.Raw("PropagandaCenterUpgradeRequest", dto.PropagandaCenterUpgradeRequestJson ?? new RequestResult().ToJson());
            w.Float("RumorFoodRunIntensity", dto.RumorFoodRunIntensity);
            w.Int("SickLeaveActiveCount", dto.SickLeaveActiveCount);
            w.Int("ForecastType", dto.ForecastType);
            w.Int("ForecastStratum", dto.ForecastStratum);
            w.Int("ForecastFog", dto.ForecastFog);
            w.Int("CoverageCapacityHouseholds", dto.CoverageCapacityHouseholds);
            w.Float("AllocWeightPoor", dto.AllocWeightPoor);
            w.Float("AllocWeightMiddle", dto.AllocWeightMiddle);
            w.Float("AllocWeightWealthy", dto.AllocWeightWealthy);
            w.Bool("ShowPsyDebrief", dto.ShowPsyDebrief);
            w.Int("PsyDebriefWave", dto.PsyDebriefWave);
            w.Int("PsyDebriefRaidCount", dto.PsyDebriefRaidCount);
            w.Int("PsyDebriefPropaganda", dto.PsyDebriefPropaganda);
            w.Int("PsyDebriefFakeVideo", dto.PsyDebriefFakeVideo);
            w.Int("PsyDebriefRumor", dto.PsyDebriefRumor);
            w.Int("PsyDebriefBlunted", dto.PsyDebriefBlunted);
            w.Int("PsyDebriefStrataMask", dto.PsyDebriefStrataMask);
            w.Float("PsyDebriefPeakIntensity", dto.PsyDebriefPeakIntensity);
            w.Float("PsyDebriefMediaTrust", dto.PsyDebriefMediaTrust);
            w.Int("PsyDebriefHouseholdsImpact", dto.PsyDebriefHouseholdsImpact);
            w.Bool("PsyDebriefFoodRunActive", dto.PsyDebriefFoodRunActive);
            w.Int("PsyDebriefInfectedDelta", dto.PsyDebriefInfectedDelta);
            w.Bool("ShowPsyInbound", dto.ShowPsyInbound);
            w.Int("PsyInboundWave", dto.PsyInboundWave);
            w.Int("PsyInboundCount", dto.PsyInboundCount);
            w.Int("PsyInboundType", dto.PsyInboundType);
            w.Int("PsyInboundStratum", dto.PsyInboundStratum);
            w.Float("PsyInboundEtaHours", dto.PsyInboundEtaHours);
            w.Bool("PsyInboundCounterReady", dto.PsyInboundCounterReady);
        }

        internal static void WriteCountermeasuresDtoSegment0(ref DomainJsonHelper.JsonWriter w, in CountermeasuresDto dto)
        {
            w.Int("CorruptionScore", dto.CorruptionScore);
            w.Int("Heat", dto.Heat);
            w.Str("HeatLevel", dto.HeatLevel ?? ReasonIds.CounterHeatLevelSafe);
            w.Str("CountermeasuresPhase", dto.CountermeasuresPhase ?? ReasonIds.CounterPhaseIdle);
            w.Int("InvestigationProgress", dto.InvestigationProgress);
            w.Int("ChargesCount", dto.ChargesCount);
            w.Int("ProtestCount", dto.ProtestCount);
            w.Bool("ChoiceRequired", dto.ChoiceRequired);
            w.Int("ChoiceType", dto.ChoiceType);
            w.Int("BribeCost", dto.BribeCost);
            w.Int("BaseBribeCost", dto.BaseBribeCost);
            w.Raw("BribeAvailability", dto.BribeAvailability);
            w.Str("LastChoiceResult", dto.LastChoiceResult ?? "");
            w.Str("CurrentJournalist", dto.CurrentJournalist ?? "");
            w.Bool("IsArrested", dto.IsArrested);
            w.Long("ArrestedAssetsSeized", dto.ArrestedAssetsSeized);
            w.Long("ArrestedWalletAfter", dto.ArrestedWalletAfter);
            w.Str("BribeRiskWarning", dto.BribeRiskWarning ?? "");
            w.Bool("SanctionsSuppressingCorruption", dto.SanctionsSuppressingCorruption);
            w.Raw("LastChoiceRequestResult", dto.LastChoiceRequestResultJson ?? "{\"RequestId\":0,\"Status\":\"idle\",\"ReasonId\":\"\",\"CanonicalEcho\":\"\",\"DiscriminatorKind\":\"none\",\"DiscriminatorValue\":\"\"}");
        }

        internal static void WriteDonorDtoSegment0(ref DomainJsonHelper.JsonWriter w, in DonorDto dto)
        {
            w.Int("DonorUsesRemaining", dto.DonorUsesRemaining);
            w.Int("DonorCooldownDays", dto.DonorCooldownDays);
            w.Str("DonorStatus", dto.DonorStatus ?? "unavailable");
            w.Bool("AvailableViaAttention", dto.AvailableViaAttention);
            w.Int("TrustIndex", dto.TrustIndex);
            w.Float("ScandalPenalty", dto.ScandalPenalty);
            w.Str("DonorExpectedAid", dto.DonorExpectedAid ?? "");
            w.Bool("DonorDialogActive", dto.DonorDialogActive);
            w.Bool("ProducerReady", dto.ProducerReady);
            w.Bool("TrustLocked", dto.TrustLocked);
            w.Str("ProducerReasonId", dto.ProducerReasonId ?? ReasonIds.DonorTrustSourceUnavailable.ToString());
        }

        internal static void WriteDonorDtoSegment1(ref DomainJsonHelper.JsonWriter w, in DonorDto dto)
        {
            w.Int("DonorFundsAmount", dto.DonorFundsAmount);
            w.Int("DonorGeneratorCount", dto.DonorGeneratorCount);
            w.Int("DonorGeneratorMW", dto.DonorGeneratorMW);
            w.Int("DonorPatriotDays", dto.DonorPatriotDays);
            w.Int("AidTierId", dto.AidTierId);
            w.Int("AidFundsOffered", dto.AidFundsOffered);
            w.Int("AidFundsAccessible", dto.AidFundsAccessible);
            w.Bool("PatriotOffered", dto.PatriotOffered);
            w.Bool("PatriotBlocked", dto.PatriotBlocked);
            w.Int("TrustMessageId", dto.TrustMessageId);
            w.Int("BlockedReasonId", dto.BlockedReasonId);
            w.Bool("HasBlockedItems", dto.HasBlockedItems);
            w.Int("DonorActiveGenerators", dto.DonorActiveGenerators);
            w.Bool("SanctionsActive", dto.SanctionsActive);
            w.Int("SanctionDaysRemaining", dto.SanctionDaysRemaining);
            w.Int("SanctionTradePenalty", dto.SanctionTradePenalty);
            w.Raw("DonorDialogRequest", dto.DonorDialogRequestJson ?? new RequestResult().ToJson());
            w.Raw("DonorSelectionRequest", dto.DonorSelectionRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteExportDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ExportDto dto)
        {
            w.Int("ExportPercent", dto.ExportPercent);
            w.Int("ExportedMW", dto.ExportedMW);
            w.Int("DailyIncome", dto.DailyIncome);
            w.Double("OffshoreBalance", dto.OffshoreBalance);
            w.Bool("IsFrozen", dto.IsFrozen);
            w.Int("FreezeReason", dto.FreezeReason);
            w.Raw("ExportAvailability", dto.ExportAvailability);
            w.Raw("ShadowTradeExportRequest", dto.ShadowTradeExportRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteFinanceDtoSegment0(ref DomainJsonHelper.JsonWriter w, in FinanceDto dto)
        {
            w.Long("CityTreasury", dto.CityTreasury);
            w.Long("TotalLiquidity", dto.TotalLiquidity);
            w.BeginObject("OfficialTreasury");
            WriteOfficialTreasuryDtoSegment0(ref w, dto.OfficialTreasury);
            w.EndObject();
            w.BeginObject("ShadowWallet");
            WriteShadowWalletDtoSegment0(ref w, dto.ShadowWallet);
            w.EndObject();
            w.DictLong("Expenses", dto.Expenses);
            w.DictLong("Income", dto.Income);
            w.Long("TotalExpenses", dto.TotalExpenses);
            w.Long("TotalIncome", dto.TotalIncome);
            w.Long("TotalDebt", dto.TotalDebt);
            w.DictLong("DebtBreakdown", dto.DebtBreakdown);
            w.Bool("DebtWarning", dto.DebtWarning);
            w.Bool("DebtRestructured", dto.DebtRestructured);
            w.Float("SanctionsMarkup", dto.SanctionsMarkup);
        }

        internal static void WriteGridWarfareDtoSegment0(ref DomainJsonHelper.JsonWriter w, in GridWarfareDto dto)
        {
            w.Int("ShadowBalance", dto.ShadowBalance);
            w.Int("ShadowLocked", dto.ShadowLocked);
            w.Int("ShadowTotal", dto.ShadowTotal);
            w.Float("EnemyPhysicalAxis", dto.EnemyPhysicalAxis);
            w.Float("EnemyDigitalAxis", dto.EnemyDigitalAxis);
            w.Float("EnemySocialAxis", dto.EnemySocialAxis);
            w.Int("IntelLevel", dto.IntelLevel);
            w.Int("PreferredTargetPhysical", dto.PreferredTargetPhysical);
            w.Int("PreferredTargetDigital", dto.PreferredTargetDigital);
            w.Int("PreferredTargetSocial", dto.PreferredTargetSocial);
            w.Float("EnemyAggregatePressure01", dto.EnemyAggregatePressure01);
            w.Int("DroneStock", dto.DroneStock);
            w.Int("BallisticStock", dto.BallisticStock);
            w.Int("DroneLauncherCount", dto.DroneLauncherCount);
            w.Int("DroneLauncherCost", dto.DroneLauncherCost);
            w.Bool("RespitePhysicalActive", dto.RespitePhysicalActive);
            w.Bool("RespiteDigitalActive", dto.RespiteDigitalActive);
            w.Bool("RespiteSocialActive", dto.RespiteSocialActive);
            w.Float("RespitePhysicalHoursLeft", dto.RespitePhysicalHoursLeft);
            w.Float("RespiteDigitalHoursLeft", dto.RespiteDigitalHoursLeft);
            w.Float("RespiteSocialHoursLeft", dto.RespiteSocialHoursLeft);
            w.Float("ObjectiveProgress", dto.ObjectiveProgress);
        }

        internal static void WriteGridWarfareDtoSegment1(ref DomainJsonHelper.JsonWriter w, in GridWarfareDto dto)
        {
            w.Float("CityStability", dto.CityStability);
            w.Float("StabilityDiscount", dto.StabilityDiscount);
            w.Raw("OperationSlots", dto.OperationSlotsJson ?? "[]");
            w.Raw("ResolvedStrikes", dto.ResolvedStrikesJson ?? "[]");
            w.DictLong("AttackCosts", dto.AttackCosts);
            w.Raw("OperationRequest", dto.OperationRequestJson ?? new RequestResult().ToJson());
            w.Raw("DroneLauncherPlacementRequest", dto.DroneLauncherPlacementRequestJson ?? RequestResultBridge.Get(RequestResultBridge.DroneLauncherPlacement).ToJson());
            w.Bool("GridWarfareUnlocked", dto.GridWarfareUnlocked);
        }

        internal static void WriteImportDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ImportDto dto)
        {
            w.Int("ShadowImportMW", dto.ShadowImportMW);
            w.Int("MaxShadowImportMW", dto.MaxShadowImportMW);
            w.Int("SelectedPresetIndex", dto.SelectedPresetIndex);
            w.Int("ShadowImportCost", dto.ShadowImportCost);
            w.Float("DiscoveryRisk", dto.DiscoveryRisk);
            w.Int("ShadowImportDaysActive", dto.ShadowImportDaysActive);
            w.Bool("IsSanctioned", dto.IsSanctioned);
            w.Int("ShadowImportSanctionDays", dto.ShadowImportSanctionDays);
            w.Raw("ShadowImportAvailability", dto.ShadowImportAvailability);
            w.Bool("IsFrozen", dto.IsFrozen);
            w.Int("FreezeReason", dto.FreezeReason);
            w.Raw("ShadowTradeImportRequest", dto.ShadowTradeImportRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteIntelDtoSegment0(ref DomainJsonHelper.JsonWriter w, in IntelDto dto)
        {
            w.Int("TensionLevel", dto.TensionLevel);
            w.Str("TensionStatus", dto.TensionStatus ?? "LOW");
            w.Str("WaveTypePrediction", dto.WaveTypePrediction ?? "INTEL_UNKNOWN");
            w.Bool("IsMassiveStrike", dto.IsMassiveStrike);
            w.BeginObject("EnergyFocusRange");
            WriteFocusRangeDtoSegment0(ref w, dto.EnergyFocusRange);
            w.EndObject();
            w.BeginObject("InfraFocusRange");
            WriteFocusRangeDtoSegment0(ref w, dto.InfraFocusRange);
            w.EndObject();
            w.BeginObject("ResidentialFocusRange");
            WriteFocusRangeDtoSegment0(ref w, dto.ResidentialFocusRange);
            w.EndObject();
            w.BeginObject("TimeEstimate");
            WriteAttackTimeEstimateDtoSegment0(ref w, dto.TimeEstimate);
            w.EndObject();
            w.Int("EstimatedShaheds", dto.EstimatedShaheds);
            w.Int("EstimatedBallistics", dto.EstimatedBallistics);
            w.Bool("HasInsider", dto.HasInsider);
            w.Long("InsiderCost", dto.InsiderCost);
            w.Long("BaseInsiderCost", dto.BaseInsiderCost);
        }

        internal static void WriteIntelDtoSegment1(ref DomainJsonHelper.JsonWriter w, in IntelDto dto)
        {
            w.Float("TensionPriceMultiplier", dto.TensionPriceMultiplier);
            w.Int("TensionPriceModifierPercent", dto.TensionPriceModifierPercent);
            w.Raw("InsiderRequest", dto.InsiderRequestJson ?? new RequestResult().ToJson());
            w.Int("IntelUpgradeLevel", dto.IntelUpgradeLevel);
            w.Long("IntelUpgradeCost", dto.IntelUpgradeCost);
            w.Raw("IntelUpgradeRequest", dto.IntelUpgradeRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteMaintenanceDtoSegment0(ref DomainJsonHelper.JsonWriter w, in MaintenanceDto dto)
        {
            w.Raw("PendingProcurementOffer", dto.PendingProcurementOfferJson ?? "null");
            w.Int("ShadyContractCount", dto.ShadyContractCount);
            w.Int("TotalContractCount", dto.TotalContractCount);
            w.Raw("ActiveContractsJson", dto.ActiveContractsJson ?? "[]");
            w.Raw("MaintenanceContractRequest", dto.MaintenanceContractRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteModalSnapshotDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ModalSnapshotDto dto)
        {
            w.Str("ActiveId", dto.ActiveId ?? string.Empty);
            w.Int("ActivePriority", dto.ActivePriority);
            w.Raw("ActiveData", dto.ActiveDataJson);
            w.Raw("Queue", dto.QueueJson);
            w.Int("Version", dto.Version);
        }

        internal static void WriteMobilizationDtoSegment0(ref DomainJsonHelper.JsonWriter w, in MobilizationDto dto)
        {
            w.Int("ManpowerAvailable", dto.ManpowerAvailable);
            w.Int("ManpowerUsed", dto.ManpowerUsed);
            w.Int("ManpowerTotal", dto.ManpowerTotal);
            w.Int("ManpowerPercent", dto.ManpowerPercent);
            w.Int("ManpowerBasePool", dto.ManpowerBasePool);
            w.Int("ManpowerCasualties", dto.ManpowerCasualties);
            w.Int("ManpowerPatriotismFactor", dto.ManpowerPatriotismFactor);
            w.Int("ManpowerMoraleFactor", dto.ManpowerMoraleFactor);
            w.Int("ManpowerFatigueFactor", dto.ManpowerFatigueFactor);
            w.Int("ManpowerDodgerFactor", dto.ManpowerDodgerFactor);
            w.Int("ManpowerDodgerCount", dto.ManpowerDodgerCount);
            w.Int("ManpowerDeferral", dto.ManpowerDeferral);
            w.Int("ManpowerDisability", dto.ManpowerDisability);
            w.Bool("IsConscriptionActive", dto.IsConscriptionActive);
            w.Bool("IsWarFatigued", dto.IsWarFatigued);
            w.Bool("IsManpowerCritical", dto.IsManpowerCritical);
            w.Bool("IsManpowerOvercommitted", dto.IsManpowerOvercommitted);
            w.Bool("CallToArmsOnCooldown", dto.CallToArmsOnCooldown);
            w.Bool("ConscriptionReactivationOnCooldown", dto.ConscriptionReactivationOnCooldown);
            w.Int("PredictedConscriptionRelease", dto.PredictedConscriptionRelease);
            w.Bool("SocialPenaltyProducerReady", dto.SocialPenaltyProducerReady);
            w.Str("SocialPenaltyReasonId", dto.SocialPenaltyReasonId ?? ReasonIds.MobSocialPenaltyUnavailable.ToString());
        }

        internal static void WriteMobilizationDtoSegment1(ref DomainJsonHelper.JsonWriter w, in MobilizationDto dto)
        {
            w.Int("WarDay", dto.WarDay);
            w.Raw("CallToArmsRequest", dto.CallToArmsRequestJson ?? new RequestResult().ToJson());
            w.Raw("ConscriptionToggleRequest", dto.ConscriptionToggleRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteNewsDtoSegment0(ref DomainJsonHelper.JsonWriter w, in NewsDto dto)
        {
            w.Int("GlobalOnlineNow", dto.GlobalOnlineNow);
            w.Int("GlobalOnlineHour", dto.GlobalOnlineHour);
            w.Int("GlobalOnlineToday", dto.GlobalOnlineToday);
            w.Int("GlobalOnlineTotal", dto.GlobalOnlineTotal);
            w.Bool("GlobalConnected", dto.GlobalConnected);
            w.Str("GlobalConnectionStatus", dto.GlobalConnectionStatus ?? "Disconnected");
            w.Bool("NetworkConnectionEnabled", dto.NetworkConnectionEnabled);
        }

        internal static void WriteNewsDtoSegment1(ref DomainJsonHelper.JsonWriter w, in NewsDto dto)
        {
            w.Str("PlayerNickname", dto.PlayerNickname ?? "");
            w.Raw("NicknameRequest", dto.NicknameRequestJson ?? new RequestResult().ToJson());
            w.Int("NicknameChangesRemaining", dto.NicknameChangesRemaining);
            w.Bool("NicknameInitialized", dto.NicknameInitialized);
            w.Bool("OnlineConsentRecorded", dto.OnlineConsentRecorded);
        }

        internal static void WritePowerGridDtoSegment0(ref DomainJsonHelper.JsonWriter w, in PowerGridDto dto)
        {
            w.Str("GridStatus", dto.GridStatus ?? "normal");
            w.Int("Production", dto.Production);
            w.Int("Demand", dto.Demand);
            w.Int("Consumption", dto.Consumption);
            w.Float("GameHour", dto.GameHour);
            w.Float("GridFrequency", dto.GridFrequency);
            w.Str("StressZone", dto.StressZone ?? "normal");
            w.Float("StressPercent", dto.StressPercent);
            w.Float("RecoveryHours", dto.RecoveryHours);
            w.Float("CollapseThresholdHours", dto.CollapseThresholdHours);
            w.Bool("ThresholdActive", dto.ThresholdActive);
            w.Int("BuildingsCutCount", dto.BuildingsCutCount);
            w.Int("DeliveredMW", dto.DeliveredMW);
            w.Int("ForcedOffMW", dto.ForcedOffMW);
            w.Int("AutoCutMW", dto.AutoCutMW);
            w.Int("DistrictShedMW", dto.DistrictShedMW);
            w.Int("AutoDispatchShedMW", dto.AutoDispatchShedMW);
            w.Int("CitySchedule", dto.CitySchedule);
            w.Int("EffectiveCityMode", dto.EffectiveCityMode);
            w.Bool("DistrictsOverrideCity", dto.DistrictsOverrideCity);
            w.Raw("CityScheduleAvailability", dto.CityScheduleAvailability);
            w.Bool("AutoDispatchEnabled", dto.AutoDispatchEnabled);
            w.Int("AutoDispatchSheddedCount", dto.AutoDispatchSheddedCount);
            w.Bool("AutoDispatchBlockedByVip", dto.AutoDispatchBlockedByVip);
            w.Int("ShadowBalance", dto.ShadowBalance);
            w.Int("AtRiskPlantCount", dto.AtRiskPlantCount);
            w.Raw("GenerationSources", dto.GenerationSourcesJson ?? "[]");
            w.Raw("CivilianDamage", dto.CivilianDamageJson ?? "[]");
            w.Float("PlantMunicipalRepairHours", dto.PlantMunicipalRepairHours);
            w.Float("PlantShadowOpsRepairHours", dto.PlantShadowOpsRepairHours);
            w.Float("CivilianMunicipalRepairHours", dto.CivilianMunicipalRepairHours);
            w.Float("CivilianShadowOpsRepairHours", dto.CivilianShadowOpsRepairHours);
            w.Raw("PlantRepairRequest", dto.PlantRepairRequestJson ?? new RequestResult().ToJson());
            w.Raw("CivilianRepairRequest", dto.CivilianRepairRequestJson ?? new RequestResult().ToJson());
            w.Raw("AutoDispatchToggleRequest", dto.AutoDispatchToggleRequestJson ?? new RequestResult().ToJson());
            w.Raw("DistrictToggleRequest", dto.DistrictToggleRequestJson ?? new RequestResult().ToJson());
            w.Raw("CitySchedulePeriodRequest", dto.CitySchedulePeriodRequestJson ?? RequestResultBridge.Get(RequestResultBridge.CitySchedule).ToJson());
            w.Raw("DistrictInternetToggleRequest", dto.DistrictInternetToggleRequestJson ?? RequestResultBridge.Get(RequestResultBridge.DistrictInternetToggle).ToJson());
            w.Float("FleetSaturationFactor", dto.FleetSaturationFactor);
            w.Int("CityDispatchableMW", dto.CityDispatchableMW);
            w.Int("CapacityHeadroomMW", dto.CapacityHeadroomMW);
            w.Int("GridExportMW", dto.GridExportMW);
            w.Int("HeadroomWarningMW", dto.HeadroomWarningMW);
            w.Int("LossKnockedOutMW", dto.LossKnockedOutMW);
            w.Int("LossDamageMW", dto.LossDamageMW);
            w.Int("LossSaturationMW", dto.LossSaturationMW);
            w.Int("LossFuelMW", dto.LossFuelMW);
            w.Int("LossAllowedMW", dto.LossAllowedMW);
        }

        internal static void WriteReputationDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ReputationDto dto)
        {
            w.Float("TrustLevel", dto.TrustLevel);
            w.Str("TrustTier", dto.TrustTier ?? "Neutral");
            w.Bool("IsFrozenOut", dto.IsFrozenOut);
            w.Float("OfferFrequencyMult", dto.OfferFrequencyMult);
        }

        internal static void WriteSchemesDtoSegment0(ref DomainJsonHelper.JsonWriter w, in SchemesDto dto)
        {
            w.Int("EmergencyFundWithdraw", dto.EmergencyFundWithdraw);
            w.Double("EmergencyFundBalance", dto.EmergencyFundBalance);
            w.Int("FuelSiphonPercent", dto.FuelSiphonPercent);
            w.Int("DraftDeferralPercent", dto.DraftDeferralPercent);
            w.Int("DraftDisabilityHeadcount", dto.DraftDisabilityHeadcount);
            w.Int("ConstructionKickbackPercent", dto.ConstructionKickbackPercent);
            w.Double("ConstructionKickbackPending", dto.ConstructionKickbackPending);
            w.Raw("EmergencyFundAvailability", dto.EmergencyFundAvailability);
            w.Raw("FuelSiphonAvailability", dto.FuelSiphonAvailability);
            w.Raw("DraftDeferralAvailability", dto.DraftDeferralAvailability);
            w.Raw("ConstructionKickbackAvailability", dto.ConstructionKickbackAvailability);
            w.Raw("CorruptionSchemeRequest", dto.CorruptionSchemeRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteSettingsDtoSegment0(ref DomainJsonHelper.JsonWriter w, in SettingsDto dto)
        {
            w.Int("DifficultyPreset", dto.DifficultyPreset);
            w.Int("BasePreset", dto.BasePreset);
            w.Int("LegalImportMW", dto.LegalImportMW);
            w.Int("LegalExportMW", dto.LegalExportMW);
            w.Bool("ConstructionDelay", dto.ConstructionDelay);
            w.Bool("RandomDisasters", dto.RandomDisasters);
            w.Bool("WinterMultiplier", dto.WinterMultiplier);
            w.Bool("NeighborEnvy", dto.NeighborEnvy);
            w.Bool("BackupPower", dto.BackupPower);
            w.Bool("ProtectCriticalInfra", dto.ProtectCriticalInfra);
            w.Bool("IsExpanded", dto.IsExpanded);
            w.Int("UiTheme", dto.UiTheme);
            w.Bool("TelemetryEnabled", dto.TelemetryEnabled);
            w.Bool("MuteCivicAudio", dto.MuteCivicAudio);
            w.Bool("MuteDroneAudio", dto.MuteDroneAudio);
            w.Bool("MuteAlertAudio", dto.MuteAlertAudio);
            w.Bool("MuteCombatAudio", dto.MuteCombatAudio);
            w.Int("ErrorCount", dto.ErrorCount);
            w.Str("ReportStatus", dto.ReportStatus ?? "");
            w.Str("ReportStatusKey", dto.ReportStatusKey ?? "");
            w.Int("LanguagePreference", dto.LanguagePreference);
            w.Bool("IsUncensored", dto.IsUncensored);
            w.Raw("AvailableLocales", dto.AvailableLocalesJson ?? "[]");
            w.Raw("AvailableThemes", dto.AvailableThemesJson ?? "[]");
            w.Raw("CrashDumps", dto.CrashDumpsJson ?? "[]");
        }

        internal static void WriteSettingsDtoSegment1(ref DomainJsonHelper.JsonWriter w, in SettingsDto dto)
        {
            w.Raw("LocaleRequest", dto.LocaleRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteSettingsLocalizationDtoSegment0(ref DomainJsonHelper.JsonWriter w, in SettingsLocalizationDto dto)
        {
            w.Str("CurrentLocale", dto.CurrentLocale ?? "");
            w.Raw("LocalizationStrings", dto.LocalizationStrings ?? "{}");
            w.Int("LocaleVersion", dto.LocaleVersion);
        }

        internal static void WriteSpotterDtoSegment0(ref DomainJsonHelper.JsonWriter w, in SpotterDto dto)
        {
            w.Int("SpotterCount", dto.SpotterCount);
            w.Int("SpotterPenaltyPercent", dto.SpotterPenaltyPercent);
            w.Int("SpotterRawPenaltyPercent", dto.SpotterRawPenaltyPercent);
            w.Int("SbuVisitCost", dto.SbuVisitCost);
            w.Int("TotalSBUVisits", dto.TotalSBUVisits);
            w.Int("EvacuationCost", dto.EvacuationCost);
            w.Int("TotalEvacuations", dto.TotalEvacuations);
            w.Bool("CounterOSINTActive", dto.CounterOSINTActive);
            w.Int("CounterOSINTDailyCost", dto.CounterOSINTDailyCost);
        }

        internal static void WriteSpotterDtoSegment1(ref DomainJsonHelper.JsonWriter w, in SpotterDto dto)
        {
            w.Raw("SpotterActionRequest", dto.SpotterActionRequestJson ?? new RequestResult().ToJson());
        }

        internal static void WriteThreatDtoSegment0(ref DomainJsonHelper.JsonWriter w, in ThreatDto dto)
        {
            w.Str("WavePhase", dto.WavePhase ?? "calm");
            w.Int("WaveNumber", dto.WaveNumber);
            w.Int("ThreatsExpected", dto.ThreatsExpected);
            w.Int("ThreatsSpawned", dto.ThreatsSpawned);
            w.Int("ThreatsRemaining", dto.ThreatsRemaining);
            w.Int("ThreatsIntercepted", dto.ThreatsIntercepted);
            w.Int("ThreatsHit", dto.ThreatsHit);
            w.Int("ThreatsCrashed", dto.ThreatsCrashed);
            w.Float("TimeInPhase", dto.TimeInPhase);
            w.Float("PhaseEndTime", dto.PhaseEndTime);
            w.Bool("ScenarioStarted", dto.ScenarioStarted);
            w.Bool("ProducerReady", dto.ProducerReady);
            w.Str("WaveDataStatus", dto.WaveDataStatus ?? "noWave");
            w.Bool("WaitingForLaunchWindow", dto.WaitingForLaunchWindow);
            w.Str("EarlyWarningMessage", dto.EarlyWarningMessage ?? "");
            w.Str("IntelReportLabel", dto.IntelReportLabel ?? "");
            w.Str("NoActiveThreatsLabel", dto.NoActiveThreatsLabel ?? "");
            w.Raw("ThreatTargets", dto.ThreatTargetsJson ?? "[]");
            w.Raw("RadarThreats", dto.RadarThreatsJson ?? "[]");
            w.Raw("RadarTargets", dto.RadarTargetsJson ?? "[]");
            w.Raw("RadarDefenses", dto.RadarDefensesJson ?? "[]");
            w.Raw("RadarBroadcasts", dto.RadarBroadcastsJson ?? "[]");
            w.Raw("MapBounds", dto.MapBoundsJson ?? "{\"MinX\":-7168,\"MaxX\":7168,\"MinZ\":-7168,\"MaxZ\":7168}");
            w.Int("IdentifyTrackedEntity", dto.IdentifyTrackedEntity);
            w.Float("IdentifyProgress", dto.IdentifyProgress);
            w.Bool("IdentifyConfirmed", dto.IdentifyConfirmed);
            w.Bool("IdentifyFocusActive", dto.IdentifyFocusActive);
            w.Bool("ShowDebriefing", dto.ShowDebriefing);
            w.Int("DebriefingWave", dto.DebriefingWave);
            w.Int("DebriefingIntercepted", dto.DebriefingIntercepted);
            w.Int("DebriefingHits", dto.DebriefingHits);
            w.Int("DebriefingShotsFired", dto.DebriefingShotsFired);
            w.Int("DebriefingCasualties", dto.DebriefingCasualties);
            w.Int("DebriefingDamageCost", dto.DebriefingDamageCost);
            w.Long("DebriefingInfraDamageCost", dto.DebriefingInfraDamageCost);
            w.Int("DebriefingCrashed", dto.DebriefingCrashed);
            w.Int("DebriefingTotalThreats", dto.DebriefingTotalThreats);
            w.Float("DebriefingEfficiency", dto.DebriefingEfficiency);
            w.Raw("RadarInterceptions", dto.RadarInterceptionsJson ?? "[]");
            w.Float("CameraX", dto.CameraX);
            w.Float("CameraZ", dto.CameraZ);
        }

    }

    public partial struct EntityRefDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteEntityRefDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct CrashDumpEntry
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteCrashDumpEntrySegment0(ref w, this);
            w.End();
        }
    }

    public partial struct CivilianDamageData
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteCivilianDamageDataSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct ActiveContractEntry
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteActiveContractEntrySegment0(ref w, this);
            w.End();
        }
    }

    public partial struct PendingProcurementOfferEntry
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WritePendingProcurementOfferEntrySegment0(ref w, this);
            w.End();
        }
    }

    public partial struct PlantWearData
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WritePlantWearDataSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct MapBoundsDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteMapBoundsDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct RadarInterceptionDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteRadarInterceptionDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct RadarTargetDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteRadarTargetDtoSegment0(ref w, this);
            w.End();
        }

        public static RadarTargetDto FromRuntime(in CivicSurvival.Core.Utils.RadarTargetDto r)
            => new RadarTargetDto
            {
                Entity = new EntityRefDto(r.Entity.Index, r.Entity.Version),
                X = r.X,
                Z = r.Z,
                Name = r.Name ?? string.Empty,
                SizeX = r.SizeX,
                SizeY = r.SizeY,
                SizeZ = r.SizeZ,
                RotationY = r.RotationY,
            };
    }

    public partial struct RadarThreatDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteRadarThreatDtoSegment0(ref w, this);
            w.End();
        }

        public static RadarThreatDto FromRuntime(in CivicSurvival.Core.Utils.RadarThreatDto r)
            => new RadarThreatDto
            {
                Entity = new EntityRefDto(r.Entity.Index, r.Entity.Version),
                X = r.X,
                Z = r.Z,
                Vx = r.Vx,
                Vz = r.Vz,
                Eta = r.Eta,
                Altitude = System.Math.Clamp(r.Y / RadarThreatDto.AltitudeCeiling, 0f, 1f),
                Type = r.Type ?? string.Empty,
                EvasionStatus = r.EvasionStatus ?? string.Empty,
                IsIdentified = r.IsIdentified,
                IsOutbound = r.IsOutbound,
            };
    }

    public partial struct RadarDefenseDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteRadarDefenseDtoSegment0(ref w, this);
            w.End();
        }

        public static RadarDefenseDto FromRuntime(in CivicSurvival.Core.Utils.RadarDefenseDto r)
            => new RadarDefenseDto
            {
                X = r.X,
                Z = r.Z,
                Range = r.Range,
            };
    }

    public partial struct RadarBroadcastDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteRadarBroadcastDtoSegment0(ref w, this);
            w.End();
        }

        public static RadarBroadcastDto FromRuntime(in CivicSurvival.Core.Utils.RadarBroadcastDto r)
            => new RadarBroadcastDto
            {
                X = r.X,
                Z = r.Z,
                Range = r.Range,
            };
    }

    public partial struct AaPlacementOptionEntry
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteAaPlacementOptionEntrySegment0(ref w, this);
            w.End();
        }
    }

    public partial struct ShadowProgramEntry
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteShadowProgramEntrySegment0(ref w, this);
            w.End();
        }
    }

    public partial struct Vector3IntDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteVector3IntDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct ThreatTargetDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteThreatTargetDtoSegment0(ref w, this);
            w.End();
        }

        public static ThreatTargetDto FromRuntime(in CivicSurvival.Core.Utils.ThreatTargetDto r)
            => new ThreatTargetDto
            {
                EntityIndex = r.EntityIndex,
                EntityVersion = r.EntityVersion,
                Name = r.Name ?? string.Empty,
                Position = new Vector3IntDto((int)r.Position.x, (int)r.Position.y, (int)r.Position.z),
                ThreatCount = r.ThreatCount,
                MinEtaSeconds = r.MinEtaSeconds,
            };
    }

    public partial struct FocusRangeDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteFocusRangeDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct OfficialTreasuryDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteOfficialTreasuryDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct ShadowWalletDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteShadowWalletDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct OperationSlotDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteOperationSlotDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct ResolvedStrikeDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteResolvedStrikeDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct AttackTimeEstimateDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteAttackTimeEstimateDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct CognitiveDistrictEntry
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteCognitiveDistrictEntrySegment0(ref w, this);
            w.End();
        }
    }

    public partial struct CognitiveStratumEntry
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteCognitiveStratumEntrySegment0(ref w, this);
            w.End();
        }
    }

    public partial struct CognitivePsyOpsEntry
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteCognitivePsyOpsEntrySegment0(ref w, this);
            w.End();
        }
    }

    public partial struct NewsPostDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteNewsPostDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct SocialPostDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteSocialPostDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct ToastDataDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteToastDataDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct RankTierDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteRankTierDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct LeaderboardEntryDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteLeaderboardEntryDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct WeeklyLeaderboardEntryDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteWeeklyLeaderboardEntryDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct ShadowLeaderboardEntryDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteShadowLeaderboardEntryDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct WeeklyShadowLeaderboardEntryDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteWeeklyShadowLeaderboardEntryDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct ProsperityLeaderboardEntryDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteProsperityLeaderboardEntryDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct WeeklyProsperityLeaderboardEntryDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteWeeklyProsperityLeaderboardEntryDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct AirDefenseDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteAirDefenseDtoSegment0(ref w, this);
            WriteEligibility(w);
            DomainDtoWriters.WriteAirDefenseDtoSegment1(ref w, this);
            w.End();
        }
    }

    public partial struct ArrestedModalPayloadDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteArrestedModalPayloadDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct AttentionDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteAttentionDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct BackupPowerDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteBackupPowerDtoSegment0(ref w, this);
            WriteEligibility(w);
            DomainDtoWriters.WriteBackupPowerDtoSegment1(ref w, this);
            w.End();
        }
    }

    public partial struct BuckwheatDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteBuckwheatDtoSegment0(ref w, this);
            WriteEligibility(w);
            DomainDtoWriters.WriteBuckwheatDtoSegment1(ref w, this);
            w.End();
        }
    }

    public partial struct CrisisSweepDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteCrisisSweepDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct DistrictDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteDistrictDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct CognitiveDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteCognitiveDtoSegment0(ref w, this);
            WriteEligibility(w);
            DomainDtoWriters.WriteCognitiveDtoSegment1(ref w, this);
            w.End();
        }
    }

    public partial struct CountermeasuresDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteCountermeasuresDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct DonorDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteDonorDtoSegment0(ref w, this);
            WriteEligibility(w);
            DomainDtoWriters.WriteDonorDtoSegment1(ref w, this);
            w.End();
        }
    }

    public partial struct ExportDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteExportDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct FinanceDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteFinanceDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct GridWarfareDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteGridWarfareDtoSegment0(ref w, this);
            WriteEligibility(w);
            DomainDtoWriters.WriteGridWarfareDtoSegment1(ref w, this);
            w.End();
        }
    }

    public partial struct ImportDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteImportDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct IntelDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteIntelDtoSegment0(ref w, this);
            WriteEligibility(w);
            DomainDtoWriters.WriteIntelDtoSegment1(ref w, this);
            w.End();
        }
    }

    public partial struct MaintenanceDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteMaintenanceDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct ModalSnapshotDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteModalSnapshotDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct MobilizationDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteMobilizationDtoSegment0(ref w, this);
            WriteEligibility(w);
            DomainDtoWriters.WriteMobilizationDtoSegment1(ref w, this);
            w.End();
        }
    }

    public partial struct NewsDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteNewsDtoSegment0(ref w, this);
            WriteEligibility(w);
            DomainDtoWriters.WriteNewsDtoSegment1(ref w, this);
            w.End();
        }
    }

    public partial struct PowerGridDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WritePowerGridDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct ReputationDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteReputationDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct SchemesDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteSchemesDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct SettingsDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteSettingsDtoSegment0(ref w, this);
            WriteEligibility(w);
            DomainDtoWriters.WriteSettingsDtoSegment1(ref w, this);
            w.End();
        }
    }

    public partial struct SettingsLocalizationDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteSettingsLocalizationDtoSegment0(ref w, this);
            w.End();
        }
    }

    public partial struct SpotterDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteSpotterDtoSegment0(ref w, this);
            WriteEligibility(w);
            DomainDtoWriters.WriteSpotterDtoSegment1(ref w, this);
            w.End();
        }
    }

    public partial struct ThreatDto
    {
        public void WriteTo(StringBuilder sb)
        {
            var w = new DomainJsonHelper.JsonWriter(sb);
            DomainDtoWriters.WriteThreatDtoSegment0(ref w, this);
            w.End();
        }
    }

}
