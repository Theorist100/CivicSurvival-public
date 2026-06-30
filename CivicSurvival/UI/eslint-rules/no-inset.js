/**
 * CIVIC-UI-024: no-inset
 *
 * Disallow `inset` CSS property in inline styles. Coherent UI ships an old
 * Chromium fork (~Chrome 49) that does not support the `inset` shorthand
 * (requires Chrome 87+). The property is silently ignored, so elements
 * that rely on it for positioning will break.
 *
 * Fix: expand to `top: 0, left: 0, right: 0, bottom: 0`.
 *
 * Historical: C02 in GridWarfarePanel overlay (2026-02-23).
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                'Disallow CSS "inset" property — unsupported in Coherent UI (Chrome 49)',
        },
        messages: {
            noInset:
                '"inset" is not supported in Coherent UI (requires Chrome 87+). ' +
                "Use top/left/right/bottom instead.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                if (node.parent && node.parent.type !== "ObjectExpression") return;

                const name = node.key.name || node.key.value;
                if (name !== "inset") return;

                context.report({
                    node,
                    messageId: "noInset",
                });
            },
        };
    },
};
