/**
 * CIVIC-UI-012: no-sticky
 *
 * Disallow `position: "sticky"` in inline styles. Coherent UI ships an old
 * Chromium fork that does not support `position: sticky` (requires Chrome 56+).
 * The element will NOT stick — it silently falls back to `static`, scrolling
 * away with the content.
 *
 * Fix: restructure layout so the sticky element is outside the scrollable
 * container, or use `position: "fixed"` with manual offset.
 *
 * Historical: Districts header in CognitiveOpsSection used sticky and scrolled
 * away, making column labels invisible.
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                'Disallow position: "sticky" — unsupported in Coherent UI',
        },
        messages: {
            noSticky:
                'position: "sticky" is not supported in Coherent UI (requires Chrome 56+). ' +
                "Move the element outside the scrollable container instead.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                if (node.parent && node.parent.type !== "ObjectExpression") return;

                const name = node.key.name || node.key.value;
                if (name !== "position") return;

                if (
                    node.value &&
                    node.value.type === "Literal" &&
                    typeof node.value.value === "string" &&
                    node.value.value.toLowerCase() === "sticky"
                ) {
                    context.report({
                        node: node.value,
                        messageId: "noSticky",
                    });
                }
            },
        };
    },
};
