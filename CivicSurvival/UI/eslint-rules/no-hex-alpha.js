/**
 * CIVIC-UI-020: no-hex-alpha
 *
 * Detect `${color}10` pattern in template literals — appending 1-2 hex chars
 * to a color variable creates 8-digit hex (#RRGGBBAA) which is CSS Color
 * Level 4 and NOT supported in Coherent UI's old Chromium fork.
 *
 * Fix: use rgba() instead.
 *   BAD:  backgroundColor: `${accent}10`        → #4488cc10 (invalid)
 *   GOOD: backgroundColor: hexToRgba(accent, 0.06)  → rgba(68,136,204,0.06)
 *
 * Historical: CRIT-03 in shared/common/HelpSection.tsx (2026-02-23).
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow appending hex alpha to color variables in template literals (8-digit hex unsupported in Coherent UI)",
        },
        messages: {
            noHexAlpha:
                "Appending '{{suffix}}' to a color creates 8-digit hex (#RRGGBBAA) — " +
                "unsupported in Coherent UI. Use rgba() instead.",
        },
        schema: [],
    },
    create(context) {
        return {
            BinaryExpression(node) {
                if (node.operator !== "+") return;
                let hex = null;
                let other = null;
                if (isHexAlphaLiteral(node.right)) {
                    hex = node.right;
                    other = node.left;
                } else if (isHexAlphaLiteral(node.left)) {
                    hex = node.left;
                    other = node.right;
                } else {
                    return;
                }
                if (!looksLikeColorExpr(context, other)) return;
                context.report({
                    node,
                    messageId: "noHexAlpha",
                    data: { suffix: hex.value },
                });
            },
            TemplateLiteral(node) {
                const { quasis, expressions } = node;

                for (let i = 0; i < expressions.length; i++) {
                    const quasiAfter = quasis[i + 1];
                    const raw = quasiAfter.value.raw;

                    // Match: quasi after expression is exactly 1-2 hex chars (end of template)
                    // or 1-2 hex chars followed by non-hex char (mid-template)
                    // e.g., `${accent}10` or `${accent}10,`
                    const match = raw.match(/^([0-9a-fA-F]{1,2})(?:[^0-9a-fA-F]|$)/);
                    if (!match) continue;

                    const suffix = match[1];

                    // Skip common non-color patterns:
                    // - `${size}10rem`, `${val}10px` — number + unit
                    if (/^[0-9a-fA-F]{1,2}(?:rem|px|em|%|vh|vw|s|ms)\b/.test(raw)) continue;

                    // Only flag when the interpolated value is actually a color. A URL host, asset
                    // path, or other non-color identifier followed by a hex-ish letter — e.g.
                    // `${ICON_HOST}buildings/40mm.jpg` or `${ICON_HOST}eye.svg` — is not an 8-digit
                    // hex. Mirrors the BinaryExpression branch above, which already guards with
                    // looksLikeColorExpr; the template branch previously omitted it (false positives).
                    if (!looksLikeColorExpr(context, expressions[i])) continue;

                    context.report({
                        node: quasiAfter,
                        messageId: "noHexAlpha",
                        data: { suffix },
                    });
                }
            },
        };
    },
};

function isHexAlphaLiteral(node) {
    return node.type === "Literal"
        && typeof node.value === "string"
        && /^[0-9a-fA-F]{2}$/.test(node.value);
}

function looksLikeColorExpr(context, node) {
    const src = (context.sourceCode ?? context.getSourceCode()).getText(node);
    return /^["']?#/i.test(src) || /\b(color|colors|hex|background|fill|stroke|accent|theme\.)/i.test(src);
}
