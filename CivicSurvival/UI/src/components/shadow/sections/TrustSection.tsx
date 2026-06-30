/**
 * TrustSection - Shadow Reputation / Trust Level
 */

import React, { memo, useMemo } from "react";
import { HoverTip } from "../../shared/common/HoverTip";
import { HelpSection } from "../../shared/common/HelpSection";
import { useTheme, useAccents } from "../../../themes";
import { bindingDataOrDefault, useReputation } from "@hooks/domain";
import { DEFAULT_REPUTATION_DTO } from "../../../types/domainDtos";
import { GlassCase, InlineWarning, ProgressBar, SectionHeader, StatRow } from "../../shared/ui";
import { createSectionStyles } from "./SectionStyles";
import { useLocale } from "../../../locales";

const getTrustColor = (level: number, _accents: ReturnType<typeof useAccents>, theme: ReturnType<typeof useTheme>) => {
    if (level >= 75) return theme.colors.trustInnerCircle;
    if (level >= 50) return theme.colors.trustMedium;
    if (level >= 25) return theme.colors.trustLow;
    return theme.colors.trustFrozen;
};

export const TrustSection = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createSectionStyles(theme, accents), [theme, accents]);

    const repState = useReputation();
    const rep = bindingDataOrDefault(repState, DEFAULT_REPUTATION_DTO);
    const trustLevel = rep.TrustLevel;
    const trustTier = rep.TrustTier;
    const isFrozenOut = rep.IsFrozenOut;
    const offerFrequency = rep.OfferFrequencyMult;
    const trustColor = getTrustColor(trustLevel, accents, theme);
    const frequencyPercent = Math.round(offerFrequency * 100);

    return (
        <GlassCase
            feature="Corruption"
            name="Shadow Trust"
            description="Reputation with shadow vendors. Higher trust pulls in more offers per day; freezing out (Trust 0%) blocks the entire shadow economy until you rebuild relationships."
        >
            <div style={s.section}>
            <SectionHeader
                title={l.t("UI_TRUST_TITLE")}
                help={<HelpSection id="trust" title={l.t("UI_TRUST_TITLE")}>{l.t("HELP_TRUST")}</HelpSection>}
            />

            <StatRow
                label={l.t("UI_TRUST_REPUTATION")}
                value={<HoverTip text={l.t("TIP_TRUST")}>{trustTier} ({trustLevel}%)</HoverTip>}
                color={trustColor}
                emphasis="title"
            />

            <ProgressBar value={trustLevel} color={trustColor} height="4rem" />

            <StatRow
                label={l.t("UI_TRUST_OFFER_RATE")}
                value={<HoverTip text={l.t("TIP_OFFER_RATE")}>{frequencyPercent}%</HoverTip>}
                color={trustColor}
            />

            {isFrozenOut && (
                <InlineWarning accent={theme.colors.errorBright} variant="critical">{l.t("UI_TRUST_FROZEN_OUT")}</InlineWarning>
            )}
        </div>
        </GlassCase>
    );
});
TrustSection.displayName = "TrustSection";
