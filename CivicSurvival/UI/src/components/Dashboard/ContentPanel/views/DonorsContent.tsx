/**
 * DonorsContent - Allied Support (World Shock, Trust, Aid Matrix)
 * ALLIES domain → AID view (currently locked, future: part of HYBRID OPS)
 */

import React, { memo, useMemo } from "react";
import { useTheme, useAccents, hexToRgba, formatMoney } from "@themes";
import { useLocale } from "@locales";
import { createShadowViewsStyles } from "../ShadowViews.styles";
import { HelpSection } from "../../../shared/common/HelpSection";
import { HoverTip, HoverTipTarget } from "../../../shared/common/HoverTip";
import { GlassCase, ProgressBar, SectionHeader, SectionTitle, StatRow } from "../../../shared/ui";
import { useDonorActions, useRequestAction } from "@hooks/actions";
import { useDonorData } from "../../../../hooks/domain";

type DonorData = ReturnType<typeof useDonorData>;

const DonorsContentReady = memo(({ data }: { data: DonorData }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const donorActions = useDonorActions();
    const s = useMemo(() => createShadowViewsStyles(theme, accents), [theme, accents]);
    const { donor, attention } = data;
    const fundsSelection = useRequestAction(() => {
        donorActions.selectDonorFunds();
        return true;
    }, donor.DonorSelectionRequest);
    const powerSelection = useRequestAction(() => {
        donorActions.selectDonorPower();
        return true;
    }, donor.DonorSelectionRequest);
    const defenseSelection = useRequestAction(() => {
        donorActions.selectDonorDefense();
        return true;
    }, donor.DonorSelectionRequest);
    const openDialog = useRequestAction(() => {
        donorActions.openDonorConference();
        return true;
    }, donor.DonorDialogRequest);
    const closeDialog = useRequestAction(() => {
        donorActions.closeDonorConference();
        return true;
    }, donor.DonorDialogRequest);
    const selectionPending = fundsSelection.isPending || powerSelection.isPending || defenseSelection.isPending;
    const dialogPending = openDialog.isPending || closeDialog.isPending || donor.DonorDialogRequest.Status === "pending";
    const donorRequestError = donor.DonorSelectionRequest.Status === "failed"
        ? donor.DonorSelectionRequest.ReasonId
        : donor.DonorDialogRequest.Status === "failed"
            ? donor.DonorDialogRequest.ReasonId
            : "";

    return (
        <div style={s.container}>
            {/* World Shock */}
            <div style={s.section}>
                <SectionHeader
                    title={l.t("AID_WORLD_ATTENTION")}
                    help={<HelpSection id="donor" title={l.t("AID_TITLE")}>{l.t("HELP_DONOR")}</HelpSection>}
                />

                <StatRow
                    label={l.t("AID_SHOCK_LEVEL")}
                    value={data.shockLevelDisplay}
                    color={data.shockColor}
                    emphasis="title"
                />
                <ProgressBar value={attention.ShockLevel} color={data.shockColor} height="4rem" />

                <StatRow
                    label={l.t("AID_TIER")}
                    value={l.t(data.aidTierKey)}
                    color={data.shockColor}
                />

                {/* T1-6: Total + Past 7 Days hybrid stats */}
                {data.hasTotalStats && (
                    <div style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textMuted, marginTop: theme.spacing.xs }}>
                        {l.t("AID_TOTAL_CASUALTIES", attention.TotalCasualties)}
                        {data.hasTotalBuildingsDestroyed && ` · ${l.t("AID_TOTAL_BUILDINGS", attention.TotalBuildingsDestroyed)}`}
                    </div>
                )}
                {data.hasWeeklyStats && (
                    <div style={{ fontSize: theme.typography.sizeXS, color: accents.crisis.accent, marginTop: theme.spacing.xs }}>
                        {l.t("AID_WEEKLY_CASUALTIES", attention.CasualtiesThisWeek)}
                        {data.hasWeeklyBuildingsDestroyed && ` · ${l.t("AID_WEEKLY_BUILDINGS", attention.BuildingsDestroyedThisWeek)}`}
                    </div>
                )}
            </div>

            {/* Exodus Warning */}
            {attention.ExodusActive && (
                <div style={{ ...s.section, background: hexToRgba(accents.crisis.accent, 0.12), border: `2rem solid ${accents.crisis.accent}` }}>
                    <SectionTitle color={accents.crisis.accent}>{l.t("AID_EXODUS_TITLE")}</SectionTitle>
                    <StatRow
                        label={l.t("AID_RATE")}
                        value={l.t("AID_RATE_VALUE", data.exodusRateDisplay)}
                        color={accents.crisis.accent}
                    />
                    <StatRow
                        label={l.t("AID_TOTAL_LEFT")}
                        value={data.totalExodusDisplay}
                        color={theme.colors.textPrimary}
                    />
                </div>
            )}

            {/* Trust Index */}
            <div style={s.section}>
                <SectionTitle>{l.t("AID_TRUST_TITLE")}</SectionTitle>

                <StatRow
                    label={l.t("AID_TRUST_INDEX")}
                    value={data.trustIndexDisplay}
                    color={data.trustColor}
                    emphasis="title"
                />
                <ProgressBar value={donor.TrustIndex} color={data.trustColor} height="4rem" />

                {data.trustMessageKey && (
                    <div style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textMuted, marginTop: theme.spacing.xs }}>
                        {l.t(data.trustMessageKey)}
                    </div>
                )}
            </div>

            {/* Aid Available */}
            <div style={s.section}>
                <SectionTitle>{l.t("AID_AVAILABLE")}</SectionTitle>

                <StatRow
                    label={l.t("AID_FUNDS")}
                    value={(
                        <>
                            {formatMoney(donor.AidFundsAccessible)}
                            {data.hasAidOfferDelta && (
                                <span style={{ color: theme.colors.textMuted, fontWeight: 400 }}>
                                    {` (${l.t("AID_OFFERED")}: ${formatMoney(donor.AidFundsOffered)})`}
                                </span>
                            )}
                        </>
                    )}
                    color={accents.schemes.accent}
                    emphasis="title"
                />

                {data.hasAvailableGenerators && (
                    <StatRow
                        label={l.t("AID_GENERATORS")}
                        value={l.t("UI_AID_GENERATORS_VALUE", donor.DonorGeneratorCount, donor.DonorGeneratorMW)}
                        color={accents.operations.accent}
                    />
                )}

                <StatRow
                    label={l.t("AID_PATRIOT")}
                    value={data.patriotAvailable
                        ? l.t("UI_DONOR_AVAILABLE")
                        : donor.DonorDefenseLockedReasonId
                            ? l.t("UI_DONOR_BLOCKED")
                            : l.t("UI_DONOR_NEEDS_SHOCK")}
                    color={donor.DonorDefenseAvailable || donor.DonorDefenseLockedReasonId ? accents.crisis.accent : theme.colors.textMuted}
                />
            </div>

            {/* Active Effects */}
            {data.hasActiveEffects && (
                <div style={s.section}>
                    <SectionTitle>{l.t("AID_ACTIVE_EFFECTS")}</SectionTitle>

                    {data.hasActiveGenerators && (
                        <StatRow
                            label={l.t("UI_DONOR_GENERATORS")}
                            value={l.t("AID_ACTIVE_COUNT", donor.DonorActiveGenerators)}
                            color={accents.operations.accent}
                        />
                    )}

                    {donor.SanctionsActive && (
                        <StatRow
                            label={l.t("UI_DONOR_SANCTIONS")}
                            value={(
                                <>
                                    {l.t("AID_DAYS_LEFT", donor.SanctionDaysRemaining)}
                                    {data.hasSanctionTradePenalty && (
                                        <span style={{ marginLeft: theme.spacing.sm, color: accents.crisis.accent }}>
                                            {l.t("AID_COMMERCE_PENALTY", donor.SanctionTradePenalty)}
                                        </span>
                                    )}
                                </>
                            )}
                            color={accents.crisis.accent}
                        />
                    )}
                </div>
            )}

            {/* Conference Button */}
            <div style={s.section}>
                <SectionTitle>{l.t("AID_CONFERENCE")}</SectionTitle>

                {donor.DonorDialogActive ? (
                    <div style={{ display: "flex", flexDirection: "column" as const, minHeight: 0 }}>
                        <div style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textMuted, marginBottom: theme.spacing.xs }}>
                            {l.t("AID_CONFERENCE_PROGRESS")}
                        </div>
                        <div style={{ fontSize: theme.typography.sizeXS, color: accents.resilience.accent, marginBottom: theme.spacing.xs }}>
                            {l.t("AID_CONFERENCE_LIVE_WARNING")}
                        </div>
                        <HoverTip text={!donor.DonorFundsAvailable && donor.DonorFundsLockedReasonId ? l.tDynamic(donor.DonorFundsLockedReasonId) : ""}>
                            <button
                                style={{ ...s.buttonDisabled(accents.schemes.accent, selectionPending || !donor.DonorFundsAvailable), marginBottom: theme.spacing.xs }}
                                onClick={fundsSelection.execute}
                                disabled={selectionPending || !donor.DonorFundsAvailable}
                            >
                                {l.t("AID_SELECT_FUNDS", formatMoney(donor.AidFundsAccessible))}
                            </button>
                        </HoverTip>
                        <HoverTip text={!donor.DonorPowerAvailable && donor.DonorPowerLockedReasonId ? l.tDynamic(donor.DonorPowerLockedReasonId) : ""}>
                            <button
                                style={{ ...s.buttonDisabled(accents.operations.accent, selectionPending || !donor.DonorPowerAvailable), marginBottom: theme.spacing.xs }}
                                onClick={powerSelection.execute}
                                disabled={selectionPending || !donor.DonorPowerAvailable}
                            >
                                {l.t("AID_SELECT_POWER", donor.DonorGeneratorCount, donor.DonorGeneratorMW)}
                            </button>
                        </HoverTip>
                        <HoverTip text={!donor.DonorDefenseAvailable && donor.DonorDefenseLockedReasonId ? l.tDynamic(donor.DonorDefenseLockedReasonId) : ""}>
                            <button
                                style={{ ...s.buttonDisabled(accents.crisis.accent, selectionPending || !donor.DonorDefenseAvailable), marginBottom: theme.spacing.xs }}
                                onClick={defenseSelection.execute}
                                disabled={selectionPending || !donor.DonorDefenseAvailable}
                            >
                                {l.t("AID_SELECT_DEFENSE")}
                            </button>
                        </HoverTip>
                        <button
                            style={s.buttonDisabled(theme.colors.textSecondary, dialogPending)}
                            onClick={closeDialog.execute}
                            disabled={dialogPending}
                        >
                            {dialogPending ? l.t("UI_PROCESSING") : l.t("UI_CLOSE")}
                        </button>
                    </div>
                ) : (
                    <HoverTipTarget text={donor.SanctionsActive ? l.t("AID_SANCTIONS_WARNING") : null}>
                        <button
                            style={donor.SanctionsActive ? s.buttonDisabled(accents.crisis.accent, !data.canOpenConference || dialogPending) : s.buttonDisabled(accents.schemes.accent, !data.canOpenConference || dialogPending)}
                            onClick={() => data.canOpenConference && openDialog.execute()}
                            disabled={!data.canOpenConference || dialogPending}
                        >
                            {dialogPending ? l.t("UI_PROCESSING") : l.t("AID_OPEN_CONFERENCE")}
                        </button>
                    </HoverTipTarget>
                )}

                {donorRequestError && (
                    <div style={{ fontSize: theme.typography.sizeXS, color: theme.colors.error, marginTop: theme.spacing.xs }}>
                        {l.tDynamic(donorRequestError)}
                    </div>
                )}

                <div style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textMuted, marginTop: theme.spacing.xs }}>
                    {l.t("AID_USES_REMAINING", donor.DonorUsesRemaining)}
                    {data.hasCooldown && ` · ${l.t("AID_COOLDOWN_DAYS", donor.DonorCooldownDays)}`}
                </div>
            </div>
        </div>
    );
});
DonorsContentReady.displayName = "DonorsContentReady";

export const DonorsContent = memo(() => {
    const diplomacyData = useDonorData();
    return (
        <GlassCase
            feature="Diplomacy"
            name="Allied Support"
            description="World shock index, trust with donors, available aid (funds, generators, Patriot batteries), conference invitations, and sanction pressure. The diplomatic feedback loop on top of your war effort."
        >
            <DonorsContentReady data={diplomacyData} />
        </GlassCase>
    );
});
DonorsContent.displayName = "DonorsContent";
