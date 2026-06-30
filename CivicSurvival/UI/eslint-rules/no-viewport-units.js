/**
 * CIVIC-UI-033: no-viewport-units
 *
 * Disallow `vh`, `vw`, `vmin`, `vmax` units in inline style values.
 * Coherent UI renders inside a subview whose dimensions may differ from
 * the game window. Viewport units refer to the subview, not the game
 * window, causing elements to be the wrong size.
 *
 * Fix: use `rem` units (which scale with the Coherent root font size)
 * or calculate pixel values from binding data.
 *
 * Pattern: same as no-px-units (CIVIC-UI-002) — scan Literal + TemplateLiteral.
 *
 * Historical: M02 in devtools audit (2026-02-23) — maxHeight: "90vh"
 * caused debug panel to be wrong height when game resolution != subview.
 */

"use strict";

const { getStaticStringSegments } = require("./_ast");

const VU_PATTERN = /(^|[^a-z])v(?:h|w|min|max)\b/i;

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow viewport units (vh/vw/vmin/vmax) in Coherent UI styles",
        },
        messages: {
            noViewportUnits:
                "Viewport units ('{{value}}') behave unexpectedly in Coherent UI — " +
                "they refer to the subview, not the game window. Use rem instead.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                for (const segment of getStaticStringSegments(node.value)) {
                    if (VU_PATTERN.test(segment)) {
                        context.report({
                            node: node.value,
                            messageId: "noViewportUnits",
                            data: { value: segment.trim() },
                        });
                        return;
                    }
                }
            },
        };
    },
};
