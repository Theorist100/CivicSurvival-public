/**
 * CognitiveOpsSection - Right column operations panel (Sandwich 2.0)
 *
 * Structure:
 * - Zone 1 (10%): Global Network Mode (OPEN/FIREWALL/BLACKOUT)
 * - Zone 2 (55%): District list with integrity bars + ISOLATE buttons (scrollable)
 * - Zone 3 (35%): Tabs [INFO WAR | RELIEF | OPS]
 */

import React, { memo, useState, useMemo } from "react";
import { useTheme, useAccents, hexToRgba } from "../../../../themes";
import { useLocale, type TranslationKey } from "../../../../locales";
import { cognitiveDistricts$, isCognitiveDistrictDto } from "@hooks/bindings/coreBindings";
import { currentAct$, Act } from "@hooks/bindings/scenarioDirectorBindings";
import { bindingDataOrDefault, useCognitive } from "@hooks/domain";
import { useSafeJsonArray, useSafeNumber } from "@hooks/useSafeBinding";
import { DEFAULT_COGNITIVE_DTO } from "../../../../types/domainDtos";
import { GlobalNetSection } from "./GlobalNetSection";
import { InfoWarTab, ReliefTab, OpsTab } from "./tabs";
import { IconShield, IconNews, IconBrain } from "../../../shared/common/Icons";
import { SegmentedTabs } from "../../../shared/ui";
import { DistrictRow } from "./ops/DistrictRow";
import { type useCognitiveActions } from "@hooks/actions";

// Tab types
type TabId = "infowar" | "relief" | "ops";

interface TabConfig {
    id: TabId;
    labelKey: TranslationKey;
    Icon: React.FC;
}

const TABS: TabConfig[] = [
    { id: "infowar", labelKey: "UI_CW_TAB_INFOWAR", Icon: IconNews },
    { id: "relief", labelKey: "UI_CW_TAB_RELIEF", Icon: IconShield },
    { id: "ops", labelKey: "UI_CW_TAB_OPS", Icon: IconBrain },
];

// ============================================================================
// Main Component
// ============================================================================

interface CognitiveOpsSectionProps {
    actions: ReturnType<typeof useCognitiveActions>;
}

export const CognitiveOpsSection = memo(({ actions }: CognitiveOpsSectionProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const rawDistricts = useSafeJsonArray(cognitiveDistricts$, [], "cognitiveDistricts");
    const districts = Array.isArray(rawDistricts) ? rawDistricts.filter(isCognitiveDistrictDto) : [];
    const cwState = useCognitive();
    const cw = bindingDataOrDefault(cwState, DEFAULT_COGNITIVE_DTO);
    const globalMode = cw?.InternetMode ?? 0;
    const currentAct = useSafeNumber(currentAct$, Act.PreWar, "currentAct");
    const isPreWar = currentAct === Act.PreWar;

    const [activeTab, setActiveTab] = useState<TabId>("infowar");

    const s = useMemo(() => ({
        container: {
            display: "flex",
            flexDirection: "column" as const,
            height: "100%",
            backgroundColor: theme.colors.paper,
        } as React.CSSProperties,

        // Zone 2: Districts (compact, only takes needed space)
        districtsSection: {
            flexShrink: 0,
            maxHeight: "200rem",
            borderTop: `2rem solid ${theme.colors.border}`,
            borderBottom: `2rem solid ${theme.colors.border}`,
        } as React.CSSProperties,

        districtsHeader: {
            padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
            backgroundColor: theme.colors.surface,
        } as React.CSSProperties,

        districtsScrollArea: {
            maxHeight: "170rem",
            overflow: "auto" as const,
        } as React.CSSProperties,

        districtsTitle: {
            fontSize: "10rem",
            fontWeight: 700,
            color: theme.colors.textMuted,
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
        } as React.CSSProperties,

        emptyState: {
            padding: theme.spacing.lg,
            textAlign: "center" as const,
            color: theme.colors.textMuted,
            fontSize: "12rem",
        } as React.CSSProperties,

        // Zone 3: Tabs (fixed footer)
        tabsSection: {
            flexShrink: 0,
        } as React.CSSProperties,

        tabIcon: {
            marginRight: "6rem",
        } as React.CSSProperties,

        tabContent: {
            height: "180rem",
            overflow: "auto" as const,
        } as React.CSSProperties,

        lockedBanner: {
            padding: "8rem 12rem",
            backgroundColor: hexToRgba(theme.colors.textMuted, 0.12),
            border: `2rem solid ${theme.colors.border}`,
            borderRadius: "4rem",
            fontSize: "11rem",
            fontWeight: 700,
            color: theme.colors.textMuted,
            textAlign: "center" as const,
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
        } as React.CSSProperties,

        lockedContent: {
            opacity: 0.4,
        } as React.CSSProperties,
    }), [theme]);

    // Memoize tab content to avoid re-creating on every render
    const tabContent = useMemo(() => {
        switch (activeTab) {
            case "infowar":
                return <InfoWarTab actions={actions} />;
            case "relief":
                return <ReliefTab actions={actions} />;
            case "ops":
                return <OpsTab actions={actions} />;
            default:
                return <InfoWarTab actions={actions} />;
        }
    }, [actions, activeTab]);

    return (
        <div style={s.container}>
            {/* PreWar lock banner */}
            {isPreWar && (
                <div style={s.lockedBanner}>{l.t("UI_CW_LOCKED_PREWAR")}</div>
            )}

            {/* All interactive zones — locked in PreWar */}
            <div style={isPreWar ? s.lockedContent : undefined}>
                {/* Zone 1: Global Network Mode (fixed header) */}
                <GlobalNetSection actions={actions} disabled={isPreWar} />

                {/* Zone 2: Districts */}
                <div style={s.districtsSection}>
                    {/* Header outside scroll area (C-01: sticky unsupported in Coherent) */}
                    <div style={s.districtsHeader}>
                        <span style={s.districtsTitle}>
                            {l.t("UI_CW_DISTRICT_INTEGRITY")} ({districts.length})
                        </span>
                    </div>

                    {/* Scrollable district rows */}
                    <div style={s.districtsScrollArea}>
                        {districts.length === 0 ? (
                            <div style={s.emptyState}>{l.t("UI_CW_NO_DISTRICTS")}</div>
                        ) : (
                            districts.map((district) => (
                                <DistrictRow
                                    key={district.DistrictIndex}
                                    district={district}
                                    globalMode={globalMode}
                                    districtIndex={district.DistrictIndex}
                                    disabled={isPreWar}
                                />
                            ))
                        )}
                    </div>
                </div>

                {/* Zone 3: Tabs (fixed footer) */}
                <div style={s.tabsSection}>
                    <SegmentedTabs
                        options={TABS.map((tab) => ({
                            value: tab.id,
                            label: <><span style={s.tabIcon}><tab.Icon /></span>{l.t(tab.labelKey)}</>,
                        }))}
                        value={activeTab}
                        onChange={setActiveTab}
                        color={accents.schemes.accent}
                        disabled={isPreWar}
                        style={{
                            backgroundColor: theme.colors.surface,
                            borderBottom: `2rem solid ${theme.colors.border}`,
                        }}
                    />
                    <div style={s.tabContent}>
                        {isPreWar ? (
                            <div style={s.emptyState}>{l.t("UI_CW_LOCKED_PREWAR")}</div>
                        ) : tabContent}
                    </div>
                </div>
            </div>
        </div>
    );
});

CognitiveOpsSection.displayName = "CognitiveOpsSection";
