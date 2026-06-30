/**
 * Cognitive warfare domain hook.
 * Thin wrapper: returns BindingState<CognitiveDto> exactly as wired from C#.
 *
 * Enum constants and label-key lookups live in cognitiveLabels.ts.
 * cognitiveDistricts stays as separate ValueBinding with custom IWriter.
 */

import { cognitiveState$ } from "../bindings/domainJsonBindings";
import { useDtoBinding } from "./useDtoBinding";
import { DEFAULT_COGNITIVE_DTO, isCognitiveDto } from "../../types/domainDtos";

export const useCognitive = () =>
    useDtoBinding(cognitiveState$, isCognitiveDto, {
        debugName: "cognitiveState",
        defaultValue: DEFAULT_COGNITIVE_DTO,
    });
