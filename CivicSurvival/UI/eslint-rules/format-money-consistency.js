/**
 * CIVIC-UI-039: format-money-consistency
 *
 * Detect inline money formatting patterns that should use formatMoney() instead.
 * Inline formatting creates inconsistency — some places show "1.2k", others "1200",
 * others "$1,200". formatMoney() from @themes/commonStyles centralizes this.
 *
 * Detected patterns:
 *   (value / 1000).toFixed(N)     ← manual thousands formatting
 *   `${value / 1000}k`            ← template literal with "k" suffix
 *   value.toFixed(N) + "k"        ← concatenation with "k" suffix
 *
 * W3 audit S3-05 (InsiderSection, ArrestedModal) and S4-07 (DonorsContent).
 */

"use strict";

// Skip the file that DEFINES formatMoney — it's the canonical implementation
const FORMAT_MONEY_DEF = /commonStyles\.(ts|tsx)$/;
const DIVISOR_SUFFIXES = {
    1000: ["k", "K"],
    1_000_000: ["M", "m"],
    1_000_000_000: ["B", "b"],
};
const MONEY_SUFFIXES = new Set(Object.values(DIVISOR_SUFFIXES).flat());

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "suggestion",
        docs: {
            description:
                "Enforce formatMoney() for currency display instead of inline formatting",
        },
        messages: {
            useFormatMoney:
                "Inline money formatting detected ({{pattern}}). Use formatMoney() from " +
                "'@themes/commonStyles' for consistent currency display.",
        },
        schema: [],
    },
    create(context) {
        const filename = context.filename || context.getFilename();
        if (FORMAT_MONEY_DEF.test(filename)) {
            return {};
        }

        return {
            // Pattern 1: (expr / 1000).toFixed(N)
            CallExpression(node) {
                if (
                    node.callee.type === "MemberExpression" &&
                    node.callee.property.type === "Identifier" &&
                    node.callee.property.name === "toFixed"
                ) {
                    const obj = node.callee.object;
                    // Check if object is (expr / 1000) — parenthesized division
                    const divisorSuffix = getDivisorSuffix(obj);
                    if (divisorSuffix) {
                        context.report({
                            node,
                            messageId: "useFormatMoney",
                            data: { pattern: `(value / divisor).toFixed() + ${divisorSuffix}` },
                        });
                    }
                }
            },

            // Pattern 2: template literal `${expr / 1000}k` or `${expr}k`
            TemplateLiteral(node) {
                for (let i = 0; i < node.quasis.length; i++) {
                    const quasi = node.quasis[i];
                    const rawValue = quasi.value.raw || quasi.value.cooked || "";
                    // Check if a quasi starts with "k" after an expression
                    if (
                        i > 0 &&
                        rawValue.length > 0 &&
                        MONEY_SUFFIXES.has(rawValue[0])
                    ) {
                        const expr = node.expressions[i - 1];
                        if (expr && (getDivisorSuffix(expr) || hasToFixed(expr))) {
                            context.report({
                                node,
                                messageId: "useFormatMoney",
                                data: { pattern: "`${value / divisor}<suffix>`" },
                            });
                        }
                    }
                }
            },

            // Pattern 3: expr + "k" or expr + "K" where expr involves toFixed or /1000
            BinaryExpression(node) {
                if (node.operator !== "+")
                    return;

                if (
                    node.right.type === "Literal" &&
                    typeof node.right.value === "string" &&
                    MONEY_SUFFIXES.has(node.right.value)
                ) {
                    if (hasToFixed(node.left) || getDivisorSuffix(node.left)) {
                        context.report({
                            node,
                            messageId: "useFormatMoney",
                            data: { pattern: "value.toFixed() + suffix" },
                        });
                    }
                }
            },
        };
    },
};

/**
 * Check if an expression is a division by 1000.
 * Handles: expr / 1000, (expr / 1000)
 */
function getDivisorSuffix(node) {
    const expr = unwrapParens(node);
    if (
        expr.type === "BinaryExpression" &&
        expr.operator === "/" &&
        expr.right.type === "Literal" &&
        typeof expr.right.value === "number"
    ) {
        return DIVISOR_SUFFIXES[expr.right.value]?.[0] ?? null;
    }
    return null;
}

/**
 * Check if an expression contains a .toFixed() call.
 */
function hasToFixed(node) {
    if (
        node.type === "CallExpression" &&
        node.callee.type === "MemberExpression" &&
        node.callee.property.type === "Identifier" &&
        node.callee.property.name === "toFixed"
    ) {
        return true;
    }
    return false;
}

/**
 * Unwrap parenthesized expressions.
 */
function unwrapParens(node) {
    // In ESTree, parenthesized expressions don't have their own node type
    // The actual node IS the inner expression (parens are syntactic only)
    return node;
}
