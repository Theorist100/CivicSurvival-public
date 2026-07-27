/**
 * CIVIC-UI-052: no-mixed-calc
 *
 * Disallow calc() expressions that combine `%` with any other length unit
 * (rem, px, em, vh, ...). cohtml 1.64 cannot build such an expression:
 * it logs "Combining percents in calc() expressions with other types is
 * not supported!", leaves a null node in the expression tree, and the
 * layout thread null-derefs it during the next style/layout pass —
 * a hard native crash (c0000005), not a visual glitch.
 *
 * Crash evidence: dump 2026-06-12 00:27:36 — `maxHeight: "calc(100% - 40rem)"`
 * in modalStyles.ts took down the game when a crisis modal opened.
 * Player.log tail right before the AV:
 *   [UI] [ERROR] Combining percents in calc() expressions with other types is not supported!
 *   [UI] [WARN]  Unable to create binary operation for calc() expression (op: 45, type1: 2, type2: 3)
 *   [UI] [WARN]  Unable to evaluate calc() expression: calc(100% - 40rem)
 *
 * Fix: keep calc() single-unit. To subtract a fixed margin from a percent
 * size, put padding on the parent and use 100% (percent resolves against
 * the parent's content box), or compute pixels from window dimensions.
 *
 * Template literals are scanned with their quasis joined, so
 * `calc(100% - ${gap}rem)` is caught even though the units sit in
 * different static segments.
 */

"use strict";

const { unwrapTransparent } = require("./_ast");

const LENGTH_UNIT_PATTERN = /\d(?:rem|px|em|vw|vh|vmin|vmax|pt|cm|mm|in|pc|ch|ex)\b/i;

function getJoinedStaticText(node) {
    const value = unwrapTransparent(node);
    if (!value) return undefined;
    if (value.type === "Literal" && typeof value.value === "string") {
        return value.value;
    }
    if (value.type === "TemplateLiteral") {
        return value.quasis
            .map((quasi) => quasi.value.cooked ?? quasi.value.raw)
            .join(" ");
    }
    return undefined;
}

function findCalcExpressions(text) {
    const results = [];
    const lower = text.toLowerCase();
    let idx = 0;
    while ((idx = lower.indexOf("calc(", idx)) !== -1) {
        let depth = 0;
        let end = -1;
        for (let i = idx + 4; i < text.length; i++) {
            if (text[i] === "(") {
                depth++;
            } else if (text[i] === ")") {
                depth--;
                if (depth === 0) {
                    end = i;
                    break;
                }
            }
        }
        if (end === -1) {
            // Unterminated (split across dynamic parts) — take the rest.
            results.push(text.slice(idx));
            break;
        }
        results.push(text.slice(idx, end + 1));
        idx = end + 1;
    }
    return results;
}

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow calc() mixing % with other length units (native cohtml crash)",
        },
        messages: {
            noMixedCalc:
                "calc() combining % with another unit ('{{value}}') crashes cohtml: " +
                "the expression cannot be built and the layout thread null-derefs it. " +
                "Use parent padding + 100%, or compute pixels from window dimensions.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                const text = getJoinedStaticText(node.value);
                if (text === undefined || !text.includes("%")) return;
                for (const expr of findCalcExpressions(text)) {
                    if (expr.includes("%") && LENGTH_UNIT_PATTERN.test(expr)) {
                        context.report({
                            node: node.value,
                            messageId: "noMixedCalc",
                            data: { value: expr.trim() },
                        });
                        return;
                    }
                }
            },
        };
    },
};
