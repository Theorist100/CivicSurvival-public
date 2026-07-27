/**
 * CIVIC-UI-027: no-backdrop-filter
 *
 * Disallow `backdropFilter` in inline styles. Coherent UI ships an old
 * Chromium fork (~Chrome 49) that does NOT support `backdrop-filter`
 * (requires Chrome 76+). The property is silently ignored, leaving the
 * element without the expected blur/saturate effect.
 *
 * Fix: increase background opacity to compensate, or restructure the
 * overlay so it doesn't rely on backdrop blur.
 *
 * Historical: C1 in LeaderboardPanel overlay (2026-02-23 audit).
 */

"use strict";

const BANNED = new Set(["backdropFilter", "WebkitBackdropFilter"]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow backdropFilter — unsupported in Coherent UI (Chrome 49)",
        },
        messages: {
            noBackdropFilter:
                "'{{prop}}' is not supported in Coherent UI (requires Chrome 76+). " +
                "Increase background opacity or restructure the overlay instead.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                if (node.parent && node.parent.type !== "ObjectExpression") return;

                const name = node.key.name || node.key.value;
                if (BANNED.has(name)) {
                    context.report({
                        node,
                        messageId: "noBackdropFilter",
                        data: { prop: name },
                    });
                }
            },
        };
    },
};
