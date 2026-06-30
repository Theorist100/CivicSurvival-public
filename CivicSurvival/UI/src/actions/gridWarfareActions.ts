/**
 * Grid Warfare domain actions — prepare, execute, cancel operations.
 */

/** @internal Raw trigger wrappers. Import only from hooks/actions feature action hooks. */
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";
import { type GridOperationType } from "../components/GridWarfare/GridWarfare.types";

export const prepareOperation = (attackType: GridOperationType): void =>
    triggerCivic(B.PrepareOperation, attackType);

export const executeOperation = (attackType: GridOperationType): void =>
    triggerCivic(B.ExecuteOperation, attackType);

export const cancelOperation = (attackType: GridOperationType): void =>
    triggerCivic(B.CancelOperation, attackType);

