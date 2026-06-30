/**
 * CIVIC-UI-026: require-min-height-flex
 *
 * Require `minHeight` on style objects that use `flexDirection: "column"`.
 * In Coherent UI (old Chromium ~49), flex column containers without an
 * explicit minHeight can collapse to zero height, making children invisible.
 *
 * Exceptions (no minHeight needed):
 *   - Objects with explicit `height` or `flex` (already sized)
 *   - Function-returning style factories (checked at call site)
 *
 * Historical: H01 across GridWarfare styles (2026-02-23).
 */

"use strict";

const { getStringLiteralValue } = require("./_ast");

const SIZING_KEYS = new Set(["minHeight", "height", "flex"]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "suggestion",
        docs: {
            description:
                "Require minHeight on flexDirection: column containers (Coherent collapse bug)",
        },
        messages: {
            requireMinHeight:
                'flexDirection: "column" without minHeight may collapse to zero height ' +
                "in Coherent UI. Add minHeight or an explicit height/flex.",
        },
        schema: [],
    },
    create(context) {
        return {
            ObjectExpression(node) {
                let hasFlexColumn = false;
                let hasSizing = false;

                for (const prop of node.properties) {
                    // Skip spread elements.
                    if (prop.type !== "Property") continue;

                    const name = prop.key.name || prop.key.value;

                    // Check for flexDirection: "column".
                    if (name === "flexDirection") {
                        const value = getStringLiteralValue(prop.value);
                        if (value && value.startsWith("column")) {
                            hasFlexColumn = true;
                        }
                    }

                    // Check for existing sizing.
                    if (SIZING_KEYS.has(name)) {
                        hasSizing = true;
                    }
                }

                if (hasFlexColumn && !hasSizing) {
                    context.report({
                        node,
                        messageId: "requireMinHeight",
                    });
                }
            },
        };
    },
};
