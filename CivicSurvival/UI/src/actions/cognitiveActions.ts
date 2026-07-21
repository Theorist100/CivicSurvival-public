/**
 * @internal Raw trigger wrappers. Import only from hooks/actions feature action hooks.
 *
 * Cognitive domain actions.
 */

import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";
import { type ProcurementLevel } from "../types/semantic";

export const setProcurementLevel = (level: ProcurementLevel): void =>
    triggerCivic(B.SetProcurementLevel, level);

export const distributeAid = (districtIndex: number): void =>
    triggerCivic(B.DistributeAid, districtIndex);

/** Build the Propaganda Center — the counter-propaganda spine (Phase 07). Unlocks
 *  Telemarathon + heroes once online. Result surfaces via PropagandaCenterPlacementRequest. */
export const placePropagandaCenter = (): void =>
    triggerCivic(B.PlacePropagandaCenter);

/** Upgrade the Propaganda Center one tier (broadcast-capacity + reach growth). Result
 *  surfaces via PropagandaCenterUpgradeRequest; blocked at max tier. */
export const upgradePropagandaCenter = (): void =>
    triggerCivic(B.UpgradePropagandaCenter);
