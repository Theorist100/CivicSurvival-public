/**
 * Mobilization domain hook.
 */

import { mobilizationState$ } from "../bindings/domainJsonBindings";
import { useDtoBinding } from "./useDtoBinding";
import { DEFAULT_MOBILIZATION_DTO, isMobilizationDto } from "../../types/domainDtos";

export const useMobilizationDomain = () =>
    useDtoBinding(mobilizationState$, isMobilizationDto, { debugName: "mobilizationState", defaultValue: DEFAULT_MOBILIZATION_DTO });

export const useMobilizationState = useMobilizationDomain;
