/**
 * CIVIC-UI-002: no-px-units
 *
 * Coherent UI scales only `rem` units. Using `px` produces elements
 * that don't scale with the game resolution — text/borders become
 * invisible or misaligned on non-1080p displays.
 *
 * Now also catches template literals: `1px solid ${color}`.
 *
 * Historical: ~15 bugs fixed (484bd8e8, 9f7b7141, de9ef614).
 * Extended: C3 in LeaderboardPanel — template literal borders (2026-02-23 audit).
 */

"use strict";

const PX_PATTERN = /\d+px/;

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description: "Disallow px units in styles (use rem for Coherent UI)",
        },
        messages: {
            noPx:
                "Use 'rem' instead of 'px' in Coherent UI. Found: '{{value}}'. " +
                "Replace e.g. '12px' → '12rem', '1px' → '2rem' (min 2rem for borders).",
        },
        schema: [],
    },
    create(context) {
        function reportTemplatePx(quasis) {
            for (let i = 0; i < quasis.length; i++) {
                const raw = quasis[i].value.raw;
                if (PX_PATTERN.test(raw) || (i > 0 && /^px(?![a-z])/i.test(raw))) {
                    context.report({
                        node: quasis[i],
                        messageId: "noPx",
                        data: { value: raw.trim() || "${...}px" },
                    });
                }
            }
        }

        return {
            TemplateLiteral(node) {
                if (node.parent?.type === "Property" || node.parent?.type === "AssignmentExpression") return;
                reportTemplatePx(node.quasis);
            },
            AssignmentExpression(node) {
                if (node.left.type !== "MemberExpression") return;
                if (node.left.object.type !== "MemberExpression") return;
                if (node.left.object.property.type !== "Identifier" || node.left.object.property.name !== "style") return;
                if (node.right.type === "Literal" && typeof node.right.value === "string" && PX_PATTERN.test(node.right.value)) {
                    context.report({
                        node,
                        messageId: "noPx",
                        data: { value: node.right.value },
                    });
                } else if (node.right.type === "TemplateLiteral") {
                    reportTemplatePx(node.right.quasis);
                }
            },
            Property(node) {
                // String literal: border: "1px solid red"
                if (
                    node.value &&
                    node.value.type === "Literal" &&
                    typeof node.value.value === "string" &&
                    PX_PATTERN.test(node.value.value)
                ) {
                    context.report({
                        node: node.value,
                        messageId: "noPx",
                        data: { value: node.value.value },
                    });
                }

                // Template literal: border: `1px solid ${color}`
                if (node.value && node.value.type === "TemplateLiteral") {
                    reportTemplatePx(node.value.quasis);
                }
            },
        };
    },
};
