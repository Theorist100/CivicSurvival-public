/** @internal Raw trigger wrappers. Import only from hooks/actions feature action hooks. */
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";

export const purchaseInsider = (): void =>
    triggerCivic(B.PurchaseInsider);

export const upgradeIntel = (): void =>
    triggerCivic(B.UpgradeIntel);
