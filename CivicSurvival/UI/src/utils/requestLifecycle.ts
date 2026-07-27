import type { RequestResult } from "../types/dtoSubTypes";

/**
 * Request id to treat as already handled when an editor mounts.
 *
 * A terminal result stays in the DTO after the request settles, so a freshly
 * mounted editor would otherwise replay the previous session's error or echo as
 * if it had just arrived. Seeding the "last handled" ref with the id present at
 * mount makes only genuinely new terminals fire.
 */
export const terminalRequestIdOnMount = (request: RequestResult | undefined): number | null =>
    request && request.RequestId > 0 && (request.Status === "failed" || request.Status === "success")
        ? request.RequestId
        : null;
