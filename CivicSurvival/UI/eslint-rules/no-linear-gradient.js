/**
 * CIVIC-UI-008: no-linear-gradient
 *
 * Coherent UI has limited support for CSS `linear-gradient()`.
 * Gradients may render incorrectly or not at all depending on the
 * Coherent version. Prefer solid colors or pre-rendered gradient images.
 *
 * Severity: warn (not a hard crash, but unreliable).
 */

"use strict";

const GRADIENT_PATTERN = /linear-gradient/i;
const BG_PROPERTIES = new Set(["background", "backgroundImage"]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "suggestion",
        docs: {
            description:
                "Warn on linear-gradient in styles (limited Coherent UI support)",
        },
        messages: {
            noLinearGradient:
                "linear-gradient has limited support in Coherent UI and may not render. " +
                "Consider a solid color or pre-rendered image instead.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                if (node.parent && node.parent.type !== "ObjectExpression") return;

                const name = node.key.name || node.key.value;
                if (!BG_PROPERTIES.has(name)) return;

                const value = node.value;
                if (!value) return;

                // String literal: background: "linear-gradient(...)"
                if (
                    value.type === "Literal" &&
                    typeof value.value === "string" &&
                    GRADIENT_PATTERN.test(value.value)
                ) {
                    context.report({
                        node: value,
                        messageId: "noLinearGradient",
                    });
                    return;
                }

                // Template literal: background: `linear-gradient(${...})`
                if (value.type === "TemplateLiteral") {
                    for (const quasi of value.quasis) {
                        if (GRADIENT_PATTERN.test(quasi.value.raw)) {
                            context.report({
                                node: value,
                                messageId: "noLinearGradient",
                            });
                            return;
                        }
                    }
                }
            },
        };
    },
};
