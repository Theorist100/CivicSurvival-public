/**
 * Hook for radar data.
 * Reads from useThreat() domain hook which parses ThreatDto JSON.
 * Types: domainDtos.ts (single source of truth)
 */

import { triggerCivic } from "@hooks/typedTrigger";
import { bindingDataOrDefault, useThreatState } from "./domain";
import { DEFAULT_THREAT_DTO } from "../types/domainDtos";
import {
    type MapBoundsDto,
    type RadarBroadcastDto,
    type RadarDefenseDto,
    type RadarTargetDto,
    type RadarThreatDto,
} from "../types/domainDtos.generated";
import { type EntityRef } from "../types/entityRef";
import { B } from "./bindingNames.generated";

// ============ Main Hook ============

export interface RadarView {
    // Inbound enemy wave tracks only — drives the enemy radar layer, the contact
    // counters and min-ETA. Outbound player strikes are split out below.
    threats: RadarThreatDto[];
    // The player's outbound counter-strikes (PlayerOutboundThreat enabled). Drawn with
    // allied styling and listed apart; deliberately excluded from the counters above so a
    // player salvo never reads as an incoming wave.
    outbound: RadarThreatDto[];
    targets: RadarTargetDto[];
    defenses: RadarDefenseDto[];
    broadcasts: RadarBroadcastDto[];
    mapBounds: MapBoundsDto;
    shahedCount: number;
    ballisticCount: number;
    totalThreats: number;
    minEta: number | null;
    // World ground-target (pivot) of the active camera — the radar "you are here"
    // marker. Carries CAMERA_MARKER_SENTINEL when no camera position is available.
    cameraX: number;
    cameraZ: number;
}

export const useRadar = (): RadarView => {
    const threat = bindingDataOrDefault(useThreatState(), DEFAULT_THREAT_DTO);
    // Split by faction: inbound wave tracks feed the enemy radar + counters; outbound player
    // strikes get their own list so they never inflate contact counts / min-ETA (the radar-
    // shows-my-own-drones-as-enemy bug).
    const allThreats = threat.RadarThreats;
    const threats = allThreats.filter(t => !t.IsOutbound);
    const outbound = allThreats.filter(t => t.IsOutbound);
    const shahedCount = threats.filter(t => t.Type === "shahed").length;
    const ballisticCount = threats.filter(t => t.Type === "ballistic").length;
    const minEta = threats.length === 0 ? null : Math.min(...threats.map(t => t.Eta));

    return {
        threats,
        outbound,
        targets: threat.RadarTargets,
        defenses: threat.RadarDefenses,
        broadcasts: threat.RadarBroadcasts,
        mapBounds: threat.MapBounds,
        shahedCount,
        ballisticCount,
        totalThreats: threats.length,
        minEta,
        cameraX: threat.CameraX,
        cameraZ: threat.CameraZ,
    };
};

// ============ Focus Helpers ============

export const focusRadarThreat = (entity: EntityRef) => {
    triggerCivic(B.FocusRadarThreat, entity);
};

export const focusRadarTarget = (entity: EntityRef) => {
    triggerCivic(B.FocusThreat, entity);
};

/** Pan the camera to one of our air-defense installations (static → plain pan,
 *  not follow-cam). Sends the installation's world X/Z in metres — not a list
 *  index — so the pan can't desync from a tick-rebuilt defense list. */
export const focusRadarDefense = (worldX: number, worldZ: number) => {
    triggerCivic(B.FocusRadarDefense, Math.round(worldX), Math.round(worldZ));
};

/** Focus camera on next threat (cycles through all active threats) */
export const focusNextThreat = () => {
    triggerCivic(B.FocusNextThreat);
};

/** Release the drone follow-cam. Reuses the threat-focus trigger with an empty entity
 *  ref (index ≤ 0) — CameraTrackingSystem.SetTrackedEntity detaches on a non-positive
 *  index. Used when a radar click is anything other than selecting a drone to track, so
 *  the camera stops chasing the tracked drone and stays where you clicked. */
export const clearRadarTracking = () => {
    focusRadarThreat({ index: 0, version: 0 });
};
