/**
 * Threat domain hooks.
 * 2 granular hooks: threats (wave/radar) + air defense.
 */

import { threatState$, airDefenseState$, spotterState$ } from "../bindings/domainJsonBindings";
import { useDtoBinding } from "./useDtoBinding";
import {
    DEFAULT_AIR_DEFENSE_DTO,
    DEFAULT_SPOTTER_DTO,
    DEFAULT_THREAT_DTO,
    isThreatDto,
    isAirDefenseDto,
    isSpotterDto,
} from "../../types/domainDtos";

export const useThreat = () =>
    useDtoBinding(threatState$, isThreatDto, { debugName: "threatState", defaultValue: DEFAULT_THREAT_DTO });

export const useAirDefense = () =>
    useDtoBinding(airDefenseState$, isAirDefenseDto, { debugName: "airDefenseState", defaultValue: DEFAULT_AIR_DEFENSE_DTO });

export const useSpotters = () =>
    useDtoBinding(spotterState$, isSpotterDto, { debugName: "spotterState", defaultValue: DEFAULT_SPOTTER_DTO });

export const useThreatState = useThreat;

export const useAirDefenseState = useAirDefense;
