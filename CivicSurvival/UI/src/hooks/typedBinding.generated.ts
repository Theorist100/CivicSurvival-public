/**
 * Auto-generated typed value binding helpers.
 * Source: Tools/binding-manifest.generated.json
 * Run: node Tools/sync-binding-codegen.js
 */
import { bindValue, type ValueBinding } from "cs2/api";
import { B, type ValueBindingName, type ValueBindingShape } from "./bindingNames.generated";

export function bindCivicValue<K extends ValueBindingName>(
    name: K,
    fallback: ValueBindingShape[K],
): ValueBinding<ValueBindingShape[K]> {
    return bindValue<ValueBindingShape[K]>(B.Group, name, fallback);
}
