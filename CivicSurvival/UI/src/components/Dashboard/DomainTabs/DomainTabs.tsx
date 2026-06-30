/**
 * DomainTabs - Main domain navigation
 * Switches between GRID, WAR, SHADOW domains
 */

import React, { memo, useMemo, useState, useCallback } from "react";
import { useTheme, useAccents } from "../../../themes";
import { createDomainTabsStyles, DOMAINS, type DomainId, type DomainConfig } from "./DomainTabs.styles";
import { getIconComponent } from "../../shared/common/Icons";
import { useLocale } from "../../../locales";

function getDomainLocked(_domain: DomainConfig): boolean {
    // Feature gating handled via GlassCase wrapper inside individual sections.
    // Tab-level lock state will be reintroduced when wave manifest is wired.
    return false;
}

// ============================================================================
// Tab Button Component
// ============================================================================

interface TabButtonProps {
    domain: DomainConfig;
    isActive: boolean;
    onClick: (e: React.MouseEvent<HTMLButtonElement>) => void;
    styles: ReturnType<typeof createDomainTabsStyles>;
}

const TabButton: React.FC<TabButtonProps> = memo(({ domain, isActive, onClick, styles: s }) => {
    const l = useLocale();
    const [isHovered, setIsHovered] = useState(false);
    const isLocked = getDomainLocked(domain);

    const style = useMemo(() => {
        if (isLocked) {
            return s.tabLocked();
        }
        const base = s.tab(isActive, domain.accent);
        if (isHovered && !isActive) {
            return { ...base, ...s.tabHover(domain.accent) };
        }
        return base;
    }, [s, isActive, isHovered, domain.accent, isLocked]);

    const IconComponent = getIconComponent(domain.icon);

    return (
        <button
            type="button"
            style={style}
            data-domain={domain.id}
            onClick={onClick}
            onMouseEnter={() => setIsHovered(true)}
            onMouseLeave={() => setIsHovered(false)}
            aria-disabled={isLocked || undefined}
            disabled={isLocked}
        >
            <span style={isLocked ? s.tabIconLocked : s.tabIcon(isActive, domain.accent)}>
                {IconComponent && <IconComponent />}
            </span>
            <span style={isLocked ? s.tabLabelLocked : s.tabLabel(isActive, domain.accent)}>
                {domain.label}
            </span>
            {isLocked && <span style={s.tabLockedBadge}>{l.t("UI_TAB_SOON")}</span>}
        </button>
    );
});
TabButton.displayName = "TabButton";

// ============================================================================
// Main DomainTabs Component
// ============================================================================

interface DomainTabsProps {
    activeDomain: DomainId;
    onDomainChange: (domain: DomainId) => void;
}

const DomainTabsComponent: React.FC<DomainTabsProps> = ({ activeDomain, onDomainChange }) => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createDomainTabsStyles(theme, accents), [theme, accents]);
    const l = useLocale();

    const handleTabClick = useCallback((e: React.MouseEvent<HTMLButtonElement>) => {
        const domainId = e.currentTarget.dataset.domain as DomainId | undefined;
        if (!domainId) return;
        const domain = DOMAINS.find((d) => d.id === domainId);
        if (!domain) return;
        onDomainChange(domainId);
    }, [onDomainChange]);

    return (
        <div style={s.container}>
            {DOMAINS.map((domain) => (
                <TabButton
                    key={domain.id}
                    domain={domain}
                    isActive={activeDomain === domain.id}
                    onClick={handleTabClick}
                    styles={s}
                />
            ))}
            <div style={s.spacer} />
            <div style={s.betaBadge}>{l.t("UI_BETA_TRAINING_MODE")}</div>
        </div>
    );
};

export const DomainTabs = memo(DomainTabsComponent);
DomainTabs.displayName = "DomainTabs";
export type { DomainId };
