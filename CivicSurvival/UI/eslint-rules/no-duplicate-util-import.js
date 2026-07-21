/**
 * CIVIC-UI-035: no-duplicate-util-import
 *
 * Ban importing `safeJsonParse` from `useSafeBinding`. The canonical
 * (and only) implementation lives in `utils/jsonParse.ts`.
 *
 * The `useSafeBinding` version was removed, but this rule prevents
 * anyone from re-adding it or importing from the wrong module.
 *
 * The two implementations had different behavior:
 *   - utils/jsonParse:    treats `"{}"` as empty → returns fallback (correct for domain bindings)
 *   - useSafeBinding:     parsed `"{}"` into `{}` → lost the fallback
 *
 * Historical: H-01 in hooks audit (2026-02-23) — duplicate `safeJsonParse`
 * with silent behavioral difference.
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Ban importing safeJsonParse from useSafeBinding — use utils/jsonParse instead",
        },
        messages: {
            wrongSource:
                "Import 'safeJsonParse' from 'utils/jsonParse', not from 'useSafeBinding'. " +
                "The useSafeBinding copy was removed to prevent behavioral divergence.",
        },
        schema: [],
    },
    create(context) {
        return {
            ImportDeclaration(node) {
                const source = node.source.value;
                if (typeof source !== "string") return;
                if (!source.includes("useSafeBinding")) return;

                for (const spec of node.specifiers) {
                    if (
                        spec.type === "ImportSpecifier" &&
                        spec.imported &&
                        spec.imported.name === "safeJsonParse"
                    ) {
                        context.report({
                            node: spec,
                            messageId: "wrongSource",
                        });
                    }
                }
            },
        };
    },
};
