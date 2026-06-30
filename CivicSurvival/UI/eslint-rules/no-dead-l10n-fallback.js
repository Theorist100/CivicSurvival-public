/**
 * CIVIC-UI-017: no-dead-l10n-fallback
 *
 * Detect `l.t("KEY") || "fallback"` pattern.
 * `l.t()` returns `[KEY]` (truthy string) when a key is missing — the `||`
 * right-hand side can never be reached. The fallback is dead code that
 * silently diverges from the actual missing-key display.
 *
 * Historical: Found in 6 files / 25+ call sites during UI audit (2026-02-23).
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow || fallback after l.t() (l.t returns truthy [KEY] on miss)",
        },
        messages: {
            deadFallback:
                "l.t() returns '[KEY]' (truthy) when a key is missing — " +
                "the || fallback '{{fallback}}' can never be reached. Remove it.",
        },
        schema: [],
    },
    create(context) {
        return {
            LogicalExpression(node) {
                if (node.operator !== "||") return;

                const left = node.left;

                // Match: l.t(...) || ...
                // l.t is a MemberExpression call: callee.object = Identifier, callee.property.name = "t"
                if (
                    left.type !== "CallExpression" ||
                    left.callee.type !== "MemberExpression" ||
                    left.callee.property.type !== "Identifier" ||
                    left.callee.property.name !== "t"
                ) {
                    return;
                }

                // Extract fallback text for the message
                const right = node.right;
                let fallbackText = "";
                if (right.type === "Literal" && typeof right.value === "string") {
                    fallbackText = right.value.length > 40
                        ? right.value.substring(0, 37) + "..."
                        : right.value;
                } else if (right.type === "TemplateLiteral") {
                    fallbackText = "(template literal)";
                } else {
                    fallbackText = context.sourceCode.getText(right).substring(0, 40);
                }

                context.report({
                    node,
                    messageId: "deadFallback",
                    data: { fallback: fallbackText },
                });
            },
        };
    },
};
