import { useMemo } from "react";
import { activeModalState$, EMPTY_MODAL_SNAPSHOT_JSON } from "../bindings/modalCoordinatorBindings";
import { useSafeString } from "../useSafeBinding";
import { safeJsonParse } from "../../utils/jsonParse";
import { isModalSnapshotDto, type ModalSnapshotDto } from "../../types/domainDtos.generated";
import { MODAL_IDS, type ModalId } from "../../types/modalIds.generated";

// Modal ids are single-sourced from Docs/Contracts/modal.contract.yaml, which
// generates both this set (ModalId union + runtime guard + registry
// exhaustiveness) and the C# ModalPriority.Get switch — they cannot drift.
// Re-exported here so existing consumers keep importing from the hook module.
export { MODAL_IDS, type ModalId };

export interface ModalCoordinatorState {
    activeId: ModalId | null;
    activePriority: number;
    activeData: unknown;
    queue: string[];
    version: number;
}

const MODAL_ID_SET: ReadonlySet<string> = new Set(MODAL_IDS);

const isModalId = (value: unknown): value is ModalId =>
    typeof value === "string" && MODAL_ID_SET.has(value);

const EMPTY_STATE: ModalCoordinatorState = {
    activeId: null,
    activePriority: 0,
    activeData: null,
    queue: [],
    version: 0,
};

const toState = (snapshot: ModalSnapshotDto): ModalCoordinatorState => ({
    activeId: isModalId(snapshot.ActiveId) ? snapshot.ActiveId : null,
    activePriority: snapshot.ActivePriority,
    activeData: snapshot.ActiveData,
    queue: snapshot.Queue,
    version: snapshot.Version,
});

export function useModalCoordinator(): ModalCoordinatorState {
    const raw = useSafeString(activeModalState$, EMPTY_MODAL_SNAPSHOT_JSON, "activeModalState");

    return useMemo(() => {
        const parsed = safeJsonParse(raw, isModalSnapshotDto);
        if (!parsed) return EMPTY_STATE;
        return toState(parsed);
    }, [raw]);
}
