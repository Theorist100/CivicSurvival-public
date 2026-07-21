/**
 * Grid warfare domain hook.
 * Includes type guards and helper hooks from the original useGridWarfare.
 */

import { gridWarfareState$ } from "../bindings/domainJsonBindings";
import { useDtoBinding } from "./useDtoBinding";
import {
    DEFAULT_GRID_WARFARE_DTO,
    isGridWarfareDto,
} from "../../types/domainDtos";

// ============ Hook ============

export const useGridWarfareDomain = () =>
    useDtoBinding(gridWarfareState$, isGridWarfareDto, { debugName: "gridWarfareState", defaultValue: DEFAULT_GRID_WARFARE_DTO });

export const useGridWarfareState = useGridWarfareDomain;
