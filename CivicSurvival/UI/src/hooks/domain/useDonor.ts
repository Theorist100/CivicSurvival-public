/**
 * Donor conference domain hook.
 */

import { donorState$ } from "../bindings/domainJsonBindings";
import { useDtoBinding } from "./useDtoBinding";
import { DEFAULT_DONOR_DTO, isDonorDto } from "../../types/domainDtos";

export const useDonor = () =>
    useDtoBinding(donorState$, isDonorDto, { debugName: "donorState", defaultValue: DEFAULT_DONOR_DTO });

export const useDonorState = useDonor;
