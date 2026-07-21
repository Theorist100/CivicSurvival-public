/**
 * IntroOverlay - Container for scenario modals.
 *
 * Previously also rendered a full-screen HUD blocker driven by the backend
 * `IntroHudVisible` flag during the Cold Open sequence. The blocker was removed
 * because if the modal coordinator failed to surface a modal (race, crash,
 * desync), the player ended up stuck behind an opaque overlay with no way to
 * interact. Modals now provide their own backdrop; nothing locks the screen
 * outside of an actual visible modal.
 */

import React from "react";
import { scLog } from "../../utils/logging";
import { useIntroScenario } from "../../hooks/scenario/useIntroScenario";
import { useModalCoordinator, type ModalId } from "../../hooks/scenario/useModalCoordinator";
import { acceptReality } from "../../hooks/bindings/introBindings";
import { dismissArrested, dismissModLoadFailure } from "../../hooks/bindings/modalCoordinatorBindings";
import {
    dismissDebriefing,
    dismissDefeat,
    dismissGridCollapse,
    dismissGridCritical,
    dismissWarFatigue,
    endlessMode,
} from "../../hooks/bindings/scenarioDirectorBindings";
import { dismissCollapseModal, dismissRefugeeModal } from "../../hooks/bindings/refugeeBindings";
import { toggleGlobalConnection } from "../../hooks/bindings/networkBindings";
import { dismissExodusWarning, dismissFirstStrike } from "../../hooks/bindings/shockActBindings";
import {
    dismissCorruptionOffer,
    dismissFirstDonorAid,
    dismissFirstSuccessfulDefense,
    dismissGeneratorEra,
    dismissGhostTown,
    dismissSpotterAlert,
    dismissWarBegins,
    dismissWhoStaysBehind,
} from "../../hooks/bindings/milestoneTutorialBindings";
import { ErrorBoundary } from "../ErrorBoundary";
import { MODAL_REGISTRY } from "./scenarioModalRegistry";

// ===== Component =====

interface ModalRecoveryAction {
    recover: () => void;
    label: string;
}

const getModalRecoveryAction = (id: ModalId | null): ModalRecoveryAction | null => {
    switch (id) {
        case "OnlineConsent": return { recover: () => toggleGlobalConnection(false), label: "Continue offline" };
        case "Intro": return { recover: acceptReality, label: "Advance intro" };
        case "FirstStrike": return { recover: dismissFirstStrike, label: "Dismiss first strike" };
        case "ExodusWarning": return { recover: dismissExodusWarning, label: "Dismiss warning" };
        case "Refugee": return { recover: dismissRefugeeModal, label: "Dismiss refugee notice" };
        case "Collapse": return { recover: dismissCollapseModal, label: "Dismiss collapse notice" };
        case "GridCollapse": return { recover: dismissGridCollapse, label: "Dismiss grid collapse" };
        case "GridCritical": return { recover: dismissGridCritical, label: "Dismiss grid critical" };
        case "WarBegins": return { recover: dismissWarBegins, label: "Dismiss war notice" };
        case "FirstDonorAid": return { recover: dismissFirstDonorAid, label: "Dismiss donor aid" };
        case "FirstSuccessfulDefense": return { recover: dismissFirstSuccessfulDefense, label: "Dismiss defense notice" };
        case "GeneratorEra": return { recover: dismissGeneratorEra, label: "Dismiss generator notice" };
        case "SpotterAlert": return { recover: dismissSpotterAlert, label: "Dismiss spotter alert" };
        case "CorruptionOffer": return { recover: dismissCorruptionOffer, label: "Dismiss corruption offer" };
        case "GhostTown": return { recover: dismissGhostTown, label: "Dismiss ghost town" };
        case "WhoStaysBehind": return { recover: dismissWhoStaysBehind, label: "Dismiss notice" };
        case "WarFatigue": return { recover: dismissWarFatigue, label: "Dismiss war fatigue" };
        case "Victory": return { recover: endlessMode, label: "Continue endless" };
        case "Defeat": return { recover: dismissDefeat, label: "Dismiss defeat" };
        case "Arrested": return { recover: dismissArrested, label: "Dismiss arrest" };
        case "ModLoadFailure": return { recover: dismissModLoadFailure, label: "Continue anyway" };
        case "Debriefing": return { recover: dismissDebriefing, label: "Dismiss debriefing" };
        default: return null;
    }
};

export const IntroOverlay: React.FC = () => {
    const { introPhase: phase } = useIntroScenario();
    const { activeId, activeData } = useModalCoordinator();
    const recovery = getModalRecoveryAction(activeId);

    // Log phase transitions for debugging (Axiom 2: diagnostic log retained)
    React.useEffect(() => {
        scLog(`IntroOverlay: phase=${phase}`);
    }, [phase]);

    React.useEffect(() => {
        scLog(`IntroOverlay: activeId=${activeId ?? "null"}`);
    }, [activeId]);

    // Render only the active modal via the registry lookup. activeId comes from
    // C#; an id with no registry entry (theoretical C#↔TS drift) yields null, not
    // a crash. Payload-free modals ignore activeData; the sole payload modal
    // (Arrested) validates it inside its own render.
    const renderModal = (id: ModalId | null): React.ReactNode => {
        if (id == null) return null;
        const def = MODAL_REGISTRY[id];
        return def ? def.render(activeData) : null;
    };

    const boundaryProps = recovery
        ? {
            name: "scenario-modals",
            variant: "modal" as const,
            resetKey: activeId,
            onRecover: recovery.recover,
            recoverLabel: recovery.label,
        }
        : {
            name: "scenario-modals",
            variant: "modal" as const,
            resetKey: activeId,
        };

    const scenarioModal = (
        <ErrorBoundary {...boundaryProps}>
            {renderModal(activeId)}
        </ErrorBoundary>
    );

    // HUD blocker intentionally removed: if the modal coordinator fails to
    // surface a modal during intro phase, the player would be stuck behind a
    // full-screen blocker with no way to interact. Now only the modals
    // themselves provide their own backdrops.
    return scenarioModal;
};
