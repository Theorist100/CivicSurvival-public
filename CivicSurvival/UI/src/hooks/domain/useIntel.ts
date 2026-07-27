import { intelState$ } from "../bindings/domainJsonBindings";
import { useDtoBinding } from "./useDtoBinding";
import { DEFAULT_INTEL_DTO, isIntelDto } from "../../types/domainDtos";

export const useIntel = () =>
    useDtoBinding(intelState$, isIntelDto, { debugName: "intelState", defaultValue: DEFAULT_INTEL_DTO });

export const useIntelState = useIntel;
