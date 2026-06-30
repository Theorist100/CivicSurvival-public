/**
 * CIVIC-UI-032: require-webkit-user-select
 *
 * When using `userSelect` in an inline style object, require
 * `WebkitUserSelect` in the same object. Coherent UI's old Chromium fork
 * needs the `-webkit-user-select` prefix (React maps `WebkitUserSelect`
 * to `-webkit-user-select`).
 *
 * Without the prefix, `user-select: none` is silently ignored and text
 * becomes selectable during drag operations.
 *
 * Pattern: same as require-webkit-filter (CIVIC-UI-013).
 *
 * Historical: C04 in devtools audit (2026-02-23) — drag header selected
 * text because `userSelect: "none"` was ignored without prefix.
 */

"use strict";

const { sourceTextEquals } = require("./_ast");

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Require WebkitUserSelect alongside userSelect for Coherent UI compatibility",
        },
        messages: {
            missingPrefix:
                '"userSelect" without "WebkitUserSelect" may not work in Coherent UI. ' +
                "Add WebkitUserSelect with the same value for -webkit-user-select prefix.",
            mismatchedValue:
                '"userSelect" and "WebkitUserSelect" must use the same value for Coherent UI compatibility.',
        },
        schema: [],
    },
    create(context) {
        return {
            ObjectExpression(node) {
                let hasUserSelect = false;
                let hasWebkitUserSelect = false;
                let userSelectNode = null;
                let webkitUserSelectNode = null;

                for (const prop of node.properties) {
                    if (prop.type !== "Property") continue;

                    const name = prop.key.name || prop.key.value;
                    if (name === "userSelect") {
                        hasUserSelect = true;
                        userSelectNode = prop;
                    }
                    if (name === "WebkitUserSelect") {
                        hasWebkitUserSelect = true;
                        webkitUserSelectNode = prop;
                    }
                }

                if (hasUserSelect && !hasWebkitUserSelect && userSelectNode) {
                    context.report({
                        node: userSelectNode,
                        messageId: "missingPrefix",
                    });
                }
                if (
                    hasUserSelect &&
                    hasWebkitUserSelect &&
                    userSelectNode &&
                    webkitUserSelectNode &&
                    !sourceTextEquals(context, userSelectNode.value, webkitUserSelectNode.value)
                ) {
                    context.report({
                        node: webkitUserSelectNode,
                        messageId: "mismatchedValue",
                    });
                }
            },
        };
    },
};
