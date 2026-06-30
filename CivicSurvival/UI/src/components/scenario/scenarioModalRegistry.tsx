/**
 * Single modal registry: maps each ModalId to its ModalDefinition.
 *
 * This is the one place that pairs an id with a component. `Record<ModalId, …>`
 * is exhaustive — adding an id to MODAL_IDS (useModalCoordinator) without a
 * matching entry here is a compile error, and vice versa. IntroOverlay renders
 * only the active modal via this lookup (no switch, no per-modal self-gate).
 */

import type { ModalId } from "../../hooks/scenario/useModalCoordinator";
import type { ModalDefinition } from "../shared/modal";

import { IntroModalDef } from "./IntroModal";
import { OnlineConsentModalDef } from "./OnlineConsentModal";
import { FirstStrikeModalDef } from "./FirstStrikeModal";
import { ExodusWarningModalDef } from "./ExodusWarningModal";
import { RefugeeArrivingModalDef } from "./RefugeeArrivingModal";
import { InfrastructureCollapseModalDef } from "./InfrastructureCollapseModal";
import { GridCollapseModalDef } from "./GridCollapseModal";
import { GridCriticalModalDef } from "./GridCriticalModal";
import { WarFatigueModalDef } from "./WarFatigueModal";
import { VictoryModalDef } from "./VictoryModal";
import { DefeatModalDef } from "./DefeatModal";
import { ArrestedModalDef } from "./ArrestedModal";
import { ModLoadFailureModalDef } from "./ModLoadFailureModal";
import { DebriefingModalDef } from "./DebriefingModal";
import { WarBeginsModalDef } from "./WarBeginsModal";
import { FirstDonorAidModalDef } from "./FirstDonorAidModal";
import { FirstSuccessfulDefenseModalDef } from "./FirstSuccessfulDefenseModal";
import { GeneratorEraModalDef } from "./GeneratorEraModal";
import { SpotterAlertModalDef } from "./SpotterAlertModal";
import { CorruptionOfferModalDef } from "./CorruptionOfferModal";
import { GhostTownModalDef } from "./GhostTownModal";
import { WhoStaysBehindModalDef } from "./WhoStaysBehindModal";

export const MODAL_REGISTRY: Record<ModalId, ModalDefinition> = {
    Intro: IntroModalDef,
    OnlineConsent: OnlineConsentModalDef,
    FirstStrike: FirstStrikeModalDef,
    ExodusWarning: ExodusWarningModalDef,
    Refugee: RefugeeArrivingModalDef,
    Collapse: InfrastructureCollapseModalDef,
    GridCollapse: GridCollapseModalDef,
    GridCritical: GridCriticalModalDef,
    WarFatigue: WarFatigueModalDef,
    Victory: VictoryModalDef,
    Defeat: DefeatModalDef,
    Arrested: ArrestedModalDef,
    ModLoadFailure: ModLoadFailureModalDef,
    Debriefing: DebriefingModalDef,
    WarBegins: WarBeginsModalDef,
    FirstDonorAid: FirstDonorAidModalDef,
    FirstSuccessfulDefense: FirstSuccessfulDefenseModalDef,
    GeneratorEra: GeneratorEraModalDef,
    SpotterAlert: SpotterAlertModalDef,
    CorruptionOffer: CorruptionOfferModalDef,
    GhostTown: GhostTownModalDef,
    WhoStaysBehind: WhoStaysBehindModalDef,
};
