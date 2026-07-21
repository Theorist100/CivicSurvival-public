/**
 * Typed wrapper around `cs2/api`'s untyped `trigger(group, name, ...args)`.
 *
 * Compile-time guarantees:
 * - `name` must be one of the generated trigger binding values.
 *   `triggerCivic(B.PlantRepair)` fails to compile unless C# registers that
 *   exact binding as a trigger.
 * - `args` must match the per-trigger arity declared in `TriggerArgRegistry`.
 *   Triggers without a registry entry are rejected at compile time.
 *
 * Action wrapper modules extend the registry via declaration merging:
 *
 *   declare module "../hooks/typedTrigger" {
 *     interface TriggerArgRegistry {
 *       [B.PlaceAABuilding]: [string];
 *       [B.SetDistrictSchedule]: [number, number];
 *     }
 *   }
 */

import { trigger } from "cs2/api";
import { B } from "./bindingNames.generated";
// Side-effect import: loads `declare module` augmentation that fills TriggerArgRegistry
// with per-trigger arg tuples derived from C# `Triggers.Add<...>()` signatures.
import "../types/triggerSignatures.generated";

/**
 * Per-trigger argument signatures. Action wrapper modules augment this via
 * declaration merging, e.g. `[B.PlaceAABuilding]: [string]`.
 */
// eslint-disable-next-line @typescript-eslint/no-empty-object-type
export interface TriggerArgRegistry {
    // Per-trigger entries are augmented by action wrapper modules.
}

/** All registered trigger name string literals. */
export type TriggerName = Extract<keyof TriggerArgRegistry, string>;

export type TriggerArgs<T extends TriggerName> = TriggerArgRegistry[T];

/**
 * Fire a typed CS2 trigger. Equivalent to `trigger(B.Group, name, ...args)` with
 * compile-time name and arity checks.
 *
 * Use the constants from `B` for `name`:
 *   triggerCivic(B.PlaceAABuilding, "AA_40mm_Bofors|Heritage")
 */
export function triggerCivic<T extends TriggerName>(name: T, ...args: TriggerArgs<T>): void {
    trigger(B.Group, name, ...(args as unknown[]));
}
