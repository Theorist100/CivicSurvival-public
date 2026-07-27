/**
 * Attention domain hook.
 */

import { attentionState$ } from "../bindings/domainJsonBindings";
import { useDtoBinding } from "./useDtoBinding";
import { DEFAULT_ATTENTION_DTO, isAttentionDto } from "../../types/domainDtos";

export const useAttention = () =>
    useDtoBinding(attentionState$, isAttentionDto, { debugName: "attentionState", defaultValue: DEFAULT_ATTENTION_DTO });

export const useAttentionState = useAttention;
