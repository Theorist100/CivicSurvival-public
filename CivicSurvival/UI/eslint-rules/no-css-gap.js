/**
 * CIVIC-UI-001: no-css-gap
 *
 * Coherent UI (CS2 engine) does NOT support CSS `gap` property.
 * Use margins on child elements instead.
 *
 * Historical: ~80 bugs fixed across 3 commits (c85cf58e, 9a868e1e, dee50b22).
 */

"use strict";

const GAP_PROPERTIES = new Set(["gap", "rowGap", "columnGap"]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description: "Disallow CSS gap property (not supported in Coherent UI)",
        },
        messages: {
            noGap:
                "CSS '{{prop}}' is not supported in Coherent UI. " +
                "Use marginRight/marginBottom on child elements instead.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                // Skip destructuring patterns (ObjectPattern) — only check ObjectExpression
                if (node.parent && node.parent.type !== "ObjectExpression") return;

                const name = node.key.name || node.key.value;
                if (GAP_PROPERTIES.has(name)) {
                    context.report({
                        node,
                        messageId: "noGap",
                        data: { prop: name },
                    });
                }
            },
        };
    },
};
