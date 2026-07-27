/**
 * CIVIC-UI-011: no-zero-gap
 *
 * Disallow `gap="0rem"`, `gap="0px"`, `gap="0"`, `gap={0}` on JSX elements.
 * A zero gap is the default behavior (no gap between children), so specifying
 * it explicitly is dead code that adds clutter.
 *
 * Historical: `<Column gap="0rem">` found in CognitiveInfoSection — unnecessary.
 */

"use strict";

const ZERO_VALUES = new Set(["0", "0rem", "0px", "0em"]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "suggestion",
        docs: {
            description:
                'Disallow gap="0rem" / gap="0" on JSX — zero gap is the default',
        },
        messages: {
            noZeroGap:
                'gap="{{value}}" is the default behavior (no gap). Remove the prop.',
        },
        schema: [],
    },
    create(context) {
        return {
            JSXAttribute(node) {
                if (!node.name || node.name.name !== "gap") return;

                // gap="0rem" — StringLiteral
                if (
                    node.value &&
                    node.value.type === "Literal" &&
                    typeof node.value.value === "string" &&
                    ZERO_VALUES.has(node.value.value.trim().toLowerCase())
                ) {
                    context.report({
                        node,
                        messageId: "noZeroGap",
                        data: { value: node.value.value },
                    });
                    return;
                }

                // gap={0} — JSXExpressionContainer wrapping a numeric 0
                if (
                    node.value &&
                    node.value.type === "JSXExpressionContainer" &&
                    node.value.expression.type === "Literal" &&
                    node.value.expression.value === 0
                ) {
                    context.report({
                        node,
                        messageId: "noZeroGap",
                        data: { value: "0" },
                    });
                }
            },
        };
    },
};
