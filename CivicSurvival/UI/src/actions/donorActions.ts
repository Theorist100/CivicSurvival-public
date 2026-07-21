/**
 * Donor conference actions.
 * Extracted from viewModelActions.ts.
 */

/** @internal Raw trigger wrappers. Import only from hooks/actions feature action hooks. */
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";

export const openDonorConference = (): void =>
    triggerCivic(B.OpenDonorConference);

export const closeDonorConference = (): void =>
    triggerCivic(B.CloseDonorConference);

export const selectDonorFunds = (): void =>
    triggerCivic(B.SelectDonorFunds);

export const selectDonorPower = (): void =>
    triggerCivic(B.SelectDonorPower);

export const selectDonorDefense = (): void =>
    triggerCivic(B.SelectDonorDefense);
