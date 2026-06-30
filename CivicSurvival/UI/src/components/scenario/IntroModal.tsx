/**
 * IntroModal - "04:57 AM" Cold Open Modal
 *
 * Pure narrative air-raid alert. The player clicks "Accept reality" to proceed into
 * the cold-open sequence (silence -> siren -> attack -> reveal).
 *
 * The Online / diagnostics agreement is NOT here — it is a separate one-time modal
 * (OnlineConsentModal, shown by the C# OnlineConsentGateSystem) that preempts this one
 * via modal priority, so the cold-open stays a clean cinematic beat.
 *
 * Visual style:
 * - Dark background with red glow
 * - Monospace font (terminal aesthetic)
 * - Pulsing warning icon
 * - Red button with glow
 */

import React, { useEffect, useMemo } from "react";
import { Z_INDEX, createBaseModalStyles, useModalPalette, useTheme } from "../../themes";
import { acceptReality } from "../../hooks/bindings/introBindings";
import { IconAlert } from "../shared/common/Icons";
import { useLocale } from "../../locales";
import { defineModal } from "../shared/modal";

// Module-level keyframes (theme-independent)
if (typeof document !== "undefined" && !document.querySelector("[data-cs-intro-keyframes]")) {
    const el = document.createElement("style");
    el.setAttribute("data-cs-intro-keyframes", "");
    el.textContent = `@keyframes pulse {
    0%, 100% { opacity: 1; transform: scale(1); }
    50% { opacity: 0.7; transform: scale(1.05); }
}`;
    document.head.appendChild(el);
}

const IntroModalView: React.FC = () => {
    const m = useModalPalette();
    const theme = useTheme();
    const l = useLocale();

    // Theme-aware hover CSS injection for the accept button (no re-render).
    useEffect(() => {
        const existing = document.querySelector("[data-cs-intro-hover]") as HTMLStyleElement | null;
        const el = existing ?? document.createElement("style");
        if (!existing) {
            el.setAttribute("data-cs-intro-hover", "");
            document.head.appendChild(el);
        }
        el.textContent = `.cs-intro-accept:hover {
    background-color: ${m.buttonHover} !important;
    box-shadow: ${m.buttonHoverGlow} !important;
}`;
    }, [m.buttonHover, m.buttonHoverGlow]);

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor: m.accents.crisis,
        overlayOpacity: 0.95,
        zIndex: Z_INDEX.modal,
    }), [m]);

    const customStyles = useMemo(() => ({
        warningIcon: {
            fontSize: "48rem",
            marginBottom: "16rem",
            animation: "pulse 2s ease-in-out infinite",
        } as React.CSSProperties,
        date: {
            color: m.gray888,
            fontSize: "16rem",
            marginTop: "16rem",
            fontWeight: "normal" as const,
        } as React.CSSProperties,
        textBlock: {
            marginBottom: "24rem",
        } as React.CSSProperties,
        textEn: {
            color: m.textContent,
            fontSize: "14rem",
            lineHeight: 1.8,
            marginBottom: "20rem",
        } as React.CSSProperties,
        button: {
            padding: "16rem 48rem",
            backgroundColor: m.accents.crisis,
            border: `2rem solid ${m.crisisBorder}`,
            borderRadius: "6rem",
            color: m.textPrimary,
            fontSize: "16rem",
            fontWeight: "bold" as const,
            fontFamily: theme.typography.fontFamilyMono,
            textTransform: "uppercase" as const,
            letterSpacing: "2rem",
            cursor: "pointer",
            boxShadow: m.introGlow,
            transition: "background-color 0.2s ease, box-shadow 0.2s ease",
            pointerEvents: "auto" as const,
        } as React.CSSProperties,
    }), [m, theme.typography.fontFamilyMono]);

    const modalStyle = useMemo(() => ({
        ...base.modal,
        maxHeight: "920rem",
    }), [base]);
    const bodyStyle = useMemo(() => ({
        ...base.body,
        padding: "32rem",
    }), [base]);
    const buttonContainerStyle = useMemo(() => ({
        ...base.buttonContainer,
        marginTop: "32rem",
        paddingTop: "24rem",
    }), [base]);

    return (
        <div style={base.overlay}>
            <div style={modalStyle}>
                {/* Header */}
                <div style={base.header}>
                    <div style={customStyles.warningIcon}>
                        <IconAlert />
                    </div>
                    <h1 style={base.title}>{l.t("INTRO_TITLE")}</h1>
                    <p style={customStyles.date}>{l.t("INTRO_DATE")}</p>
                </div>

                {/* Body */}
                <div style={bodyStyle}>
                    <div style={customStyles.textBlock}>
                        <p style={customStyles.textEn}>{l.t("INTRO_TEXT_1")}</p>
                        <p style={customStyles.textEn}>{l.t("INTRO_TEXT_2")}</p>
                        <p style={customStyles.textEn}>{l.t("INTRO_TEXT_3")}</p>
                        <p style={customStyles.textEn}>{l.t("INTRO_TEXT_4")}</p>
                        <p style={customStyles.textEn}>{l.t("INTRO_TEXT_5")}</p>
                        <p style={customStyles.textEn}>{l.t("INTRO_TEXT_6")}</p>
                        <p style={{ ...customStyles.textEn, color: m.gray888 }}>{l.t("INTRO_TEXT_7")}</p>
                    </div>

                    <div style={buttonContainerStyle}>
                        <button
                            className="cs-intro-accept"
                            style={customStyles.button}
                            onClick={acceptReality}
                        >
                            {l.t("INTRO_BUTTON")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const IntroModalDef = defineModal({
    id: "Intro",
    render: () => <IntroModalView />,
});
