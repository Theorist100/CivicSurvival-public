/**
 * Enriched donor view data.
 *
 * Raw donor/attention DTO defaults are already guaranteed by safeJsonParse.
 * This hook centralizes donor computed fields so views do not repeat color,
 * availability, and display-value logic inline.
 */

import { useMemo } from "react";
import { useAccents, useTheme } from "../../themes";
import { type TranslationKey } from "../../locales";
import { useDurationFormatter } from "../format";
import { useAttention } from "./useAttention";
import { useDonor } from "./useDonor";
import { bindingDataOrDefault } from "./useDtoBinding";
import { DEFAULT_ATTENTION_DTO, DEFAULT_DONOR_DTO } from "../../types/domainDtos";

const TRUST_MSG_KEYS: Record<number, TranslationKey> = {
    1: "AID_TRUST_MSG_FULL",
    2: "AID_TRUST_MSG_PARTIAL",
    3: "AID_TRUST_MSG_MINIMAL",
    4: "AID_TRUST_MSG_REFUSED",
};

const AID_TIER_KEYS: Record<number, TranslationKey> = {
    0: "AID_TIER_NONE",
    1: "AID_TIER_DEEP_CONCERN",
    2: "AID_TIER_HEADLINES",
    3: "AID_TIER_GLOBAL_SHOCK",
};

export function useDonorData() {
    const donorState = useDonor();
    const attentionState = useAttention();
    const theme = useTheme();
    const accents = useAccents();
    const format = useDurationFormatter();

    const donor = bindingDataOrDefault(donorState, DEFAULT_DONOR_DTO);
    const attention = bindingDataOrDefault(attentionState, DEFAULT_ATTENTION_DTO);

    return useMemo(() => {
        const shockColor = attention.ShockTier === "GlobalShock" ? accents.crisis.accent
            : attention.ShockTier === "Headlines" ? accents.resilience.accent
            : theme.colors.textSecondary;

        const trustColor = donor.TrustLocked ? theme.colors.textSecondary
            : donor.TrustIndex > 75 ? accents.schemes.accent
            : donor.TrustIndex > 50 ? accents.resilience.accent
            : donor.TrustIndex > 25 ? theme.colors.textSecondary
            : accents.crisis.accent;

        return {
            donor,
            attention,
            canOpenConference: donor.DonorStatus === "available",
            shockColor,
            trustColor,
            shockLevelDisplay: format.percent(attention.ShockLevel),
            aidTierKey: AID_TIER_KEYS[donor.AidTierId] ?? "AID_TIER_NONE",
            hasTotalStats: attention.TotalCasualties > 0,
            hasTotalBuildingsDestroyed: attention.TotalBuildingsDestroyed > 0,
            hasWeeklyStats: attention.CasualtiesThisWeek > 0 || attention.BuildingsDestroyedThisWeek > 0,
            hasWeeklyBuildingsDestroyed: attention.BuildingsDestroyedThisWeek > 0,
            exodusRateDisplay: attention.ExodusRatePercentPerDay.toFixed(1),
            totalExodusDisplay: attention.TotalExodus.toLocaleString(),
            trustIndexDisplay: donor.TrustLocked ? "N/A" : format.percent(donor.TrustIndex),
            trustMessageKey: donor.TrustMessageId > 0 ? TRUST_MSG_KEYS[donor.TrustMessageId] ?? null : null,
            hasAidOfferDelta: donor.AidFundsOffered > donor.AidFundsAccessible,
            hasAvailableGenerators: donor.DonorPowerAvailable && donor.DonorGeneratorCount > 0,
            patriotAvailable: donor.DonorDefenseAvailable,
            hasActiveEffects: donor.DonorActiveGenerators > 0 || donor.SanctionsActive,
            hasActiveGenerators: donor.DonorActiveGenerators > 0,
            hasSanctionTradePenalty: donor.SanctionTradePenalty > 0,
            hasCooldown: donor.DonorCooldownDays > 0,
        };
    }, [donor, attention, theme, accents, format]);
}
