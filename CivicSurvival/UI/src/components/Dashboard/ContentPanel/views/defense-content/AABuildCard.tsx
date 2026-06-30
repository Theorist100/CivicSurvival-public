import React from "react";

/** Style subset the AA build cards need (a slice of WarViewStyles). */
type AABuildCardStyles = {
    buildingCard: React.CSSProperties;
    buildingThumbnail: React.CSSProperties;
    buildingInfo: React.CSSProperties;
    buildingName: React.CSSProperties;
    buildingStats: React.CSSProperties;
    buildingStat: React.CSSProperties;
    buildingStatValue: (color: string) => React.CSSProperties;
    buildingPlaceButton: (color: string) => React.CSSProperties;
};

export interface AABuildCardProps {
    title: string;
    /** Thumbnail URL (coui://ui-mods/cs-icons/buildings/...). */
    thumbnail: string;
    badgeText: string;
    badgeColor: string;
    costLabel: string;
    costText: string;
    costColor: string;
    crewLabel: string;
    crew: number;
    crewColor: string;
    buttonText: string;
    buttonColor: string;
    /** Already folds in placementPending; gates click, opacity, and focusability. */
    disabled: boolean;
    marginTop?: string | undefined;
    onPlace: () => void;
    styles: AABuildCardStyles;
}

/**
 * One placement card in the AA build list (Heritage / Donor Patriot / Paid Bofors /
 * Paid Patriot). Presentational — the caller owns the eligibility/pending logic and
 * passes the resolved colors, texts, and the placement callback.
 */
export const AABuildCard: React.FC<AABuildCardProps> = ({
    title,
    thumbnail,
    badgeText,
    badgeColor,
    costLabel,
    costText,
    costColor,
    crewLabel,
    crew,
    crewColor,
    buttonText,
    buttonColor,
    disabled,
    marginTop,
    onPlace,
    styles: s,
}) => (
    <div
        style={{
            ...s.buildingCard,
            ...(marginTop ? { marginTop } : {}),
            opacity: disabled ? 0.5 : 1,
        }}
        aria-disabled={disabled}
        onClick={() => {
            if (!disabled) onPlace();
        }}
        onKeyDown={(e) => {
            if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                e.currentTarget.click();
            }
        }}
        role="button"
        tabIndex={disabled ? -1 : 0}
    >
        <img src={thumbnail} alt={title} style={s.buildingThumbnail} />
        <div style={s.buildingInfo}>
            <div style={s.buildingName}>
                {title}
                <span style={{ marginLeft: "8rem", color: badgeColor, fontSize: "11rem" }}>
                    {badgeText}
                </span>
            </div>
            <div style={s.buildingStats}>
                <div style={s.buildingStat}>
                    <span>{costLabel}:</span>
                    <span style={s.buildingStatValue(costColor)}>{costText}</span>
                </div>
                <div style={s.buildingStat}>
                    <span>{crewLabel}:</span>
                    <span style={s.buildingStatValue(crewColor)}>{crew}</span>
                </div>
            </div>
        </div>
        <div style={s.buildingPlaceButton(buttonColor)}>{buttonText}</div>
    </div>
);
