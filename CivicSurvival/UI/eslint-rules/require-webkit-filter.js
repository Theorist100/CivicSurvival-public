/**
 * CIVIC-UI-013: require-webkit-filter
 *
 * When using `filter:` in an inline style object, require `WebkitFilter:` to
 * be present in the same object. Coherent UI's old Chromium fork may need the
 * `-webkit-filter` prefix (which React maps from `WebkitFilter`).
 *
 * Ignores objects that don't contain a `filter` property.
 *
 * Historical: `filter: "grayscale(100%)"` and `filter: "hue-rotate(-30deg)"` in
 * CognitiveInfoSection.tsx had no `-webkit-` prefix, silently failing in Coherent.
 */

"use strict";

const { sourceTextEquals } = require("./_ast");

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Require WebkitFilter alongside filter for Coherent UI compatibility",
        },
        messages: {
            missingWebkitFilter:
                '"filter" without "WebkitFilter" may not work in Coherent UI. ' +
                "Add WebkitFilter with the same value for -webkit-filter prefix.",
            mismatchedValue:
                '"filter" and "WebkitFilter" must use the same value for Coherent UI compatibility.',
        },
        schema: [],
    },
    create(context) {
        return {
            ObjectExpression(node) {
                let hasFilter = false;
                let hasWebkitFilter = false;
                let filterNode = null;
                let webkitFilterNode = null;

                for (const prop of node.properties) {
                    // Skip spread elements
                    if (prop.type !== "Property") continue;

                    const name = prop.key.name || prop.key.value;
                    if (name === "filter") {
                        hasFilter = true;
                        filterNode = prop;
                    }
                    if (name === "WebkitFilter") {
                        hasWebkitFilter = true;
                        webkitFilterNode = prop;
                    }
                }

                if (hasFilter && !hasWebkitFilter && filterNode) {
                    context.report({
                        node: filterNode,
                        messageId: "missingWebkitFilter",
                    });
                }
                if (
                    hasFilter &&
                    hasWebkitFilter &&
                    filterNode &&
                    webkitFilterNode &&
                    !sourceTextEquals(context, filterNode.value, webkitFilterNode.value)
                ) {
                    context.report({
                        node: webkitFilterNode,
                        messageId: "mismatchedValue",
                    });
                }
            },
        };
    },
};
