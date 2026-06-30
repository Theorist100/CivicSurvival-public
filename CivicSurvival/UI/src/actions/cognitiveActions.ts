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
